using AlgoTrader.Shared;
using AlgoTrader.Shared.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using static AlgoTrader.Shared.BinanceAPI;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace AlgoTrader.Shared
{
    public class TradeEngineBinanceMargin : TradeEngineBinance
    {

        public class MaxBorrowData
        {
            public string Amount { get; set; }
            public string BorrowLimit { get; set; }
        }

        //The JSON node here is an unnamed array of these objects, so to deserialize: Deserialize<List<IsolatedMarginFeeData>>(response);
        public class IsolatedMarginFeeData
        {
            public class IsolatedMarginData
            {
                public string Coin { get; set; }
                public double DailyInterest { get; set; }
                public double BorrowLimit { get; set; }
            }
            public double VipLevel { get; set; }
            public string Symbol { get; set; }
            public double Leverage { get; set; }
            public List<IsolatedMarginData> Data { get; set; }
        }

        public class IsolatedMarginAccountData
        {   
            public class BaseQuoteAssetData
            {
                public string Asset { get; set; }
                public bool BorrowEnabled { get; set; }
                public double Borrowed { get; set; }
                public double Free { get; set; }
                public double Interest { get; set; }
                public double Locked { get; set; }
                public double NetAsset { get; set; }
                public double NetAssetOfBTC { get; set; }
                public bool RepayEnabled { get; set; }
                public double TotalAsset { get; set; }
            }            
            public class AssetData
            {
                public BaseQuoteAssetData BaseAsset { get; set; }
                public BaseQuoteAssetData QuoteAsset { get; set; }
                public string Symbol { get; set; }
                public bool IsolatedCreated { get; set; }
                public bool Enabled { get; set; }
                public double MarginLevel { get; set; }
                public string MarginLevelStatus { get; set; }
                public double MarginRatio { get; set; }
                public double IndexPrice { get; set; }
                public double LiquidatePrice { get; set; }
                public double LiquidateRate { get; set; }
                public bool TradeEnabled { get; set; }
            }
            public List<AssetData> Assets { get; set; }
            public double TotalAssetOfBtc { get; set; }
            public double TotalLiabilityOfBtc { get; set; }
            public double TotalNetAssetOfBtc { get; set; }
            public double BorrowEnabled { get; set; }
            public double MarginLevel { get; set; }
            public double CollateralMarginLevel { get; set; }
            public double TotalCollateralValueInUSDT { get; set; }
            public double TradeEnabled { get; set; }
            public double AccountType { get; set; }
            public double TransferInEnabled { get; set; }
            public double TransferOutEnabled { get; set; }


            public BaseQuoteAssetData GetBaseAsset(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName)?.BaseAsset;
            }
            public BaseQuoteAssetData GetQuoteAsset(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName)?.QuoteAsset;
            }
            public AssetData GetAssetData(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName);
            }
        }

        /*
         * ISOLATED MARGIN:
         * Leverage is set to 10X permanantly, you can't change that and there is no need because you just borrow what you need
         * You can change the margin level in Cross margin but the API method AdjustCrossMarginMaxLeveradgeAsync() only accepts 3 or 5
         *  - however there is an error saying you need to complete a questionaire if you want to go up to 10.
         * Isolated is nothing to do with Spot, you only trade with your isolated balance.
         * 
         * Your total available borrowing is the amount of BTC + USDT, multiplied by leverage (always 10x with the API)
         * 
         * You can determine the amount to borrow from QueryMaxBorrowAsync()
         * You can then either set the amount to borrow or repay with SetMarginBorrowRepay() or auto-borrow by setting
         *  the sideEffectType parameter when submitting the buy order.
         *  
         *  MarginLevel will trigger liquidation when it is <= 1.05 (starts of at 999)
         * 
         *  
         * */
        public TradeEngineBinanceMargin(string symbolName, int timeInterval) : base(symbolName, timeInterval)
        {

        }

        public override void Test()
        {
 


            var data1 = new Dictionary<string, string>
            {
                { "asset", "BTC" },
                { "isolatedSymbol", "BTCUSDT" }
            };

            var response1 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync(data1).Result);



            //todo - check free and locked assets, max borrow after orders
            //todo - get the borrowed amount to pay back when closing
            try
            {
                var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
                if (response == null)
                {
                    return;
                }
                var baseAsset = response.GetBaseAsset(symbolName);
                var quoteAsset = response.GetQuoteAsset(symbolName);
                var assetData = response.GetAssetData(symbolName);

                var marginLevel = assetData.MarginLevel; //assume this is the correct figure for margin              
                if (marginLevel > 10) marginLevel = 10;
                if (marginLevel < Leverage) marginLevel = Leverage;

                var leveragedAmount = Leverage / marginLevel * quoteAsset.Free;
                
                double orderQuantityBuy = GetBalanceToTrade(leveragedAmount, GetLastPrice());

                var maxAmount = double.TryParse(response1.Amount, out var amount);
                if (maxAmount && amount > 0)
                {
                    orderQuantityBuy = GetCorrectPrecisionForBaseAssetTrade(Math.Min(amount, orderQuantityBuy));
                }
                double orderQuantitySell = GetCorrectPrecisionForBaseAssetTrade(baseAsset.Free);

                //orderQuantityBuy = GetCorrectPrecisionForBaseAssetTrade(16 / GetLastPrice());
                var orderId = Guid.NewGuid().ToString();

                //short: Enter: {SIDE, SELL}, {SIDE_EFFECT_TYPE, MARGIN_BUY}
                //       Close: {SIDE, BUY},  {SIDE_EFFECT_TYPE, AUTO_REPAY}
                //Long:  Enter: {SIDE, BUY},  {SIDE_EFFECT_TYPE, MARGIN_BUY}
                //       Close: {SIDE, SELL}, {SIDE_EFFECT_TYPE, AUTO_REPAY}

                //price calculations are the same but you are starting with USDT so this must be converted to BTC (GetCorrectPrecisionForBaseAssetTrade(...))

                //this is setup for a short
                var dataSell = new Dictionary<string, string>
                {
                    {SYMBOL, symbolName },
                    {SIDE, SELL},
                    {ORDER_TYPE, MARKET },
                    {QUANTITY, orderQuantityBuy.ToString() },
                    {CLIENT_ORDER_ID_NEW, orderId },
                    //{AUTO_REPAY_AT_CANCEL, TRUE }, //not sure how this affects a market order??
                    {SIDE_EFFECT_TYPE, MARGIN_BUY},
                    {IS_ISOLATED, TRUE },
                    {RESPONSE_TYPE, FULL }
                };

                var dataBuy = new Dictionary<string, string>
                {
                    {SYMBOL, symbolName },
                    {SIDE, BUY},
                    {ORDER_TYPE, MARKET },
                    {QUANTITY, orderQuantityBuy.ToString() },
                    {CLIENT_ORDER_ID_NEW, orderId },
                    //{AUTO_REPAY_AT_CANCEL, TRUE }, //not sure how this affects a market order??
                    {SIDE_EFFECT_TYPE, AUTO_REPAY},
                    {IS_ISOLATED, TRUE },
                    {RESPONSE_TYPE, FULL }
                };

                var result = Deserialize<NewOrderData>(binanceAPI.NewMarginOrderAsync(dataBuy).Result);
                if (result == null)
                {
                    var v = 0;
                }

                if (!string.IsNullOrEmpty(result.Status) && result.Status != "FILLED")
                {
                    if (result.AveragePrice == 0)
                    {
                        LogTrades.WriteLine("Order returned zero average price");
                    }
                    if (result.ExecutedQty == 0)
                    {
                        LogTrades.WriteLine("Order returned zero executed quantity");
                    }

                }

                var averagePrice = result.AveragePrice;
                var executedQuantity = result.ExecutedQty;

                response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
                baseAsset = response.GetBaseAsset(symbolName);
                quoteAsset = response.GetQuoteAsset(symbolName);


            }
            catch (Exception ex)
            {

            }

            return;
            //var response6 = binanceAPI.AdjustCrossMarginMaxLeveradgeAsync(data).Result;
            //var response7 = binanceAPI.EnableIsolateMarginAsync(symbolName).Result;
            //var response0 = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            //var baseAsset = response0.GetBaseAsset(symbolName);
            //var quoteAsset = response0.GetQuoteAsset(symbolName);



            //try
            //{
            //    if (double.TryParse(response["price"].ToString(), out double price))
            //    {
            //        return price;
            //    }
            //    LogTrades.WriteLine("Failed to get symbol price");
            //    return 0;
            //}
            //catch (Exception e)
            //{
            //    LogTrades.WriteLine($"Error getting price info: {e.Message}");
            //    return 0;
            //}

            //var data1 = new Dictionary<string, string>
            //{
            //    { "asset", "BTC" },
            //    { "isolatedSymbol", "BTCUSDT" }
            //};

            //var response1 = binanceAPI.QueryMaxBorrowAsync(data1).Result;
            ////var response2 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync("USDT").Result);


            return;
            //contains interest / fee data
            JsonNode response3 = binanceAPI.QueryIsolateMarginAccountFeeAsync(symbolName).Result;
            var d1 = Deserialize<List<IsolatedMarginFeeData>>(response3);
        }


        #region Send Margin REST requests

        public bool EnableIsolatedMargin()
        {
            JsonNode response = binanceAPI.EnableIsolateMarginAsync(symbolName).Result;
            try
            {
                string s = response["success"].ToString();
                return s == "true";
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error enabling isolated margin: {e.Message}");
                return false;
            }
        }

        public void QueryIsolatedMarginAccountInfo()
        {
            try
            {
                JsonNode response = binanceAPI.QueryIsolateMarginAccountInfoAsync().Result;
            }
            catch (Exception ex)
            {

            }
        }
        #endregion
    }
}
