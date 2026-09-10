using AlgoTrader.Shared;
using AlgoTrader.Shared.Enums;
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

        #region Strings
        public const string IS_ISOLATED = "isIsolated";
        public const string AUTO_REPAY_AT_CANCEL = "autoRepayAtCancel";
        public const string SIDE_EFFECT_TYPE = "sideEffectType";
        public const string NO_SIDE_EFFECT = "NO_SIDE_EFFECT";
        public const string MARGIN_BUY = "MARGIN_BUY";
        public const string AUTO_REPAY = "AUTO_REPAY";
        public const string AUTO_BORROW_REPAY = "AUTO_BORROW_REPAY"; //this will repay any liabilitites as well as borrow to buy or sell.
        #endregion

        #region Binance data classes (JSON deserializes into these directly)

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

        #endregion

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
            binanceAPI = new BinanceMarginAPI(
                   LIVE_KEY,
                   LIVE_SECRET,
                   null,
                   15000,
                   true,
                   "https://api.binance.com/"
                );
        }



        public override void Test()
        {
            var r = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (r == null)
            {
                return;
            }
            var _baseAsset = r.GetBaseAsset(symbolName);
            var _quoteAsset = r.GetQuoteAsset(symbolName);

            return;
            var data1 = new Dictionary<string, string>
            {
                { "asset", "BTC" },
                { "isolatedSymbol", "BTCUSDT" }
            };

            var api = binanceAPI;
            var response1 = Deserialize<MaxBorrowData>(api.QueryMaxBorrowAsync(data1).Result);



            //todo - check free and locked assets, max borrow after orders
            //todo - get the borrowed amount to pay back when closing
            try
            {
                var response = Deserialize<IsolatedMarginAccountData>(api.QueryIsolateMarginAccountInfoAsync().Result);
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

                double orderQuantityBuy = GetBalanceToEnterTrade(leveragedAmount, GetLastPrice(), TradePosition.LONG);

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

                var result = Deserialize<NewOrderData>(api.NewOrderAsync(dataBuy).Result);
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

                response = Deserialize<IsolatedMarginAccountData>(api.QueryIsolateMarginAccountInfoAsync().Result);
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
            JsonNode response3 = api.QueryIsolateMarginAccountFeeAsync(symbolName).Result;
            var d1 = Deserialize<List<IsolatedMarginFeeData>>(response3);
        }


        #region Send Margin REST requests

        public bool EnableIsolatedMargin()
        {
            try
            {
                JsonNode response = binanceAPI.EnableIsolateMarginAsync(symbolName).Result;
                return response["success"].ToString() == "true";
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

        protected override double GetBalanceToEnterTrade(double freeBalance, double lastQuoteValue, TradePosition position)
        {
            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response == null)
            {
                return 0;
            }
            var baseAsset = response.GetBaseAsset(symbolName);
            var quoteAsset = response.GetQuoteAsset(symbolName);
            var assetData = response.GetAssetData(symbolName);

            //with margin trading, the balance is leverage-derived rather than setting a leverage value on the exchange
            var marginLevel = assetData.MarginLevel; //assume this is the correct figure for margin              
            if (marginLevel > 10) marginLevel = 10;
            if (marginLevel < Leverage) marginLevel = Leverage;

            var leveragedAmount = Leverage / marginLevel * quoteAsset.Free;

            double orderQuantity = 0;
            switch (position)
            {
                case TradePosition.LONG:

                    double orderQuantityBuy = base.GetBalanceToEnterTrade(leveragedAmount, GetLastPrice());

                    var data1 = new Dictionary<string, string>
                    {
                        { "asset", "BTC" },
                        { "isolatedSymbol", "BTCUSDT" }
                    };
                    var response1 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync(data1).Result);
                    var maxAmount = double.TryParse(response1.Amount, out var amount);
                    if (maxAmount && amount > 0)
                    {
                        orderQuantity = GetCorrectPrecisionForBaseAssetTrade(Math.Min(amount, orderQuantityBuy));
                    }
                    break;
                case TradePosition.SHORT:
                    orderQuantity = GetCorrectPrecisionForBaseAssetTrade(baseAsset.Free);
                    break;
            }

            return orderQuantity;
        }

        protected override double GetFreeBaseAssetBalance(string symbol)
        {

        }

        protected override (double freeBalance, double totalEquity) GetTotalEquity() //e.g. USDT, BTC
        {
            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response == null)
            {
                return (0, 0);
            }
            var baseAsset = response.GetBaseAsset(symbolName);
            var quoteAsset = response.GetQuoteAsset(symbolName);

            var price = GetLastPrice();
            var freeEquity = baseAsset.Free + quoteAsset.Free * price;
            var totalEquity = baseAsset.TotalAsset + quoteAsset.TotalAsset * price;

            return (Math.Round(freeEquity, 2), Math.Round(totalEquity, 2));
        }

        protected override Dictionary<string, string> GetOrderData(TradePosition position, double orderQuantity, Side side, string orderId)
        {
            //short: Enter: {SIDE, SELL}, {SIDE_EFFECT_TYPE, MARGIN_BUY}
            //       Close: {SIDE, BUY},  {SIDE_EFFECT_TYPE, AUTO_REPAY}
            //Long:  Enter: {SIDE, BUY},  {SIDE_EFFECT_TYPE, MARGIN_BUY}
            //       Close: {SIDE, SELL}, {SIDE_EFFECT_TYPE, AUTO_REPAY}

            string sideValue = string.Empty;
            string sideEffectType = string.Empty;
            switch (position)
            {
                case TradePosition.LONG:
                    switch (side)
                    {
                        case Side.ENTER:
                            sideValue = BUY;
                            sideEffectType = MARGIN_BUY;
                            break;
                        case Side.CLOSE:
                            sideValue = SELL;
                            sideEffectType = AUTO_REPAY;
                            break;
                    }
                    break;

                case TradePosition.SHORT:
                    switch (side)
                    {
                        case Side.ENTER:
                            sideValue = SELL;
                            sideEffectType = MARGIN_BUY;
                            break;
                        case Side.CLOSE:
                            sideValue = BUY;
                            sideEffectType = AUTO_REPAY;
                            break;
                    }
                    break;

            }
            return new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {SIDE, sideValue},
                {ORDER_TYPE, MARKET },
                {QUANTITY, orderQuantity.ToString() },
                {CLIENT_ORDER_ID_NEW, orderId },
                {RESPONSE_TYPE, FULL },

                //additional data for margin trade
                //{AUTO_REPAY_AT_CANCEL, TRUE }, //not sure what this does unless you need to borrow to set a limit order?
                {SIDE_EFFECT_TYPE, sideEffectType},
                {IS_ISOLATED, TRUE }
            };
        }

        //TODO
        protected override Dictionary<string, string> GetOCOData(TradePosition position, double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId)
        {
            var sideEffectType = AUTO_REPAY; //NOT SURE ABOUT THIS WITH DIFFERENT POSITIONS??

            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { SIDE, SELL},
                { ABOVE_TYPE, TAKE_PROFIT},
                { ABOVE_STOP_PRICE, abovePrice.ToString()},
                { ABOVE_CLIENT_ORDER_ID, TPId },
                { BELOW_CLIENT_ORDER_ID, stopId },
                { QUANTITY, orderQuantity.ToString()},
                { BELOW_TYPE, STOP_LOSS},
                { BELOW_STOP_PRICE, belowPrice.ToString()},

                {SIDE_EFFECT_TYPE, sideEffectType},
                {IS_ISOLATED, TRUE }
            };
        }
    }
}
