using AlgoTrader.Shared;
using AlgoTrader.Shared.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;

namespace Shared.Trading.TradeEngine
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
            public class AssetData
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
                public BaseQuoteAssetData BaseAsset { get; set; }
                public BaseQuoteAssetData QuoteAsset { get; set; }
                public string Symbol { get; set; }
                public bool IsolatedCreated { get; set; }
                public bool Enabled { get; set; }
                public double MarginLevel { get; set; }
                public string MarginLevelStatus { get; set; }
                public double MarginRatio { get; set; }
                public double IndesPrice { get; set; }
                public double LiquidatePrice { get; set; }
                public double LiquidateRate { get; set; }
                public bool TradeEnabled { get; set; }
            }
            public List<AssetData> Assets { get; set; }
            public double TotalAssetOfBtc { get; set; }
            public double TotalLiabilityOfBtc { get; set; }
            public double TotalNetAssetOfBtc { get; set; }
        }

        public TradeEngineBinanceMargin(string symbolName, int timeInterval) : base(symbolName, timeInterval)
        {

        }

        public override void Test()
        {
            //var response = Deserialize<IsolatedMarginData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
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

            var response1 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync("BTC").Result);
            var response2 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync("USDT").Result);

            var response = binanceAPI.QueryIsolateMarginAccountInfoAsync().Result;
            var d = Deserialize<IsolatedMarginAccountData>(response);

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
