using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using OpenExchangeRates;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Windows.Data.Json;
using static AlgoTrader.Shared.BinanceAPI;
using static AlgoTrader.Shared.ExchangeConnection.ExchangeDataProcessorBinance;

/*
 * Quote-to-base trading and stub orders: This code has been removed but is still in a project-excluded copy of the original file.
 * */
namespace AlgoTrader.Shared
{
    internal class TradeEngineBinance : TradeEngine
    {

        #region Binance data classes (JSON deserializes into these directly)

        static T Deserialize<T>(JsonNode jsonNode)
        {
            return JsonConvert.DeserializeObject<T>(jsonNode.ToString());
        }

        #region Exchange info data
        //ExchangeInfo, global
        public class ExchangeInfoData
        {
            public class RateLimitData
            {
                public string RateLimitType { get; set; }
                public string Interval { get; set; }
                public long IntervalNum { get; set; }
                public long Limit { get; set; }
            }
            public class ExchangeFilterData
            {
                public string FilterType { get; set; }
                public long MaxNumOrders { get; set; }
                public long MaxNumAlgoOrders { get; set; }
                public double MinQty { get; set; }
                public double MaxQty { get; set; }
                public double StepSize { get; set; }
                public double MinNotional { get; set; }
                public bool ApplyToMarket { get; set; }
                public long AvgPriceMins { get; set; }
                public double TickSize { get; set; }
            }
            public class SymbolData
            {
                public string Symbol { get; set; }
                public string Status { get; set; }
                public string QuoteAsset { get; set; } //this would be BTC for example, in ETHBTC
                public List<string> OrderTypes { get; set; }
                public bool OcoAllowed { get; set; }
                public bool IsSpotTradingAllowed { get; set; }
                public bool IsMarginTradingAllowed { get; set; }
                public List<string> permissions { get; set; }
                public List<ExchangeFilterData> Filters { get; set; }
                #region Unused properties
                //public bool QuoteOrderQtyMarketAllowed { get; set; }
                //public bool IcebergAllowed { get; set; }

                public string BaseAsset { get; set; } //this would be ETH for example in ETHBTC
                public long BaseAssetPrecision { get; set; }
                public long QuotePrecision { get; set; } // will be removed in future api versions (v4+)
                public long QuoteAssetPrecision { get; set; }
                public long BaseCommissionPrecision { get; set; }
                public long QuoteCommissionPrecision { get; set; }
                #endregion
            }

            public string TimeZone { get; set; }
            public long ServerTime { get; set; }
            public List<RateLimitData> RateLimits { get; set; }
            public List<ExchangeFilterData> ExchangeFilters { get; set; }
            public List<SymbolData> Symbols { get; set; }
        }
        //the exchange data, filtered out before passing back
        public class ExchangeInfoDataFiltered
        {
            public long RequestWeightPerMin { get; set; } //the total request weights for all orders must not excede this in a minute
            public long MaxOrdersPer10Secs { get; set; }
            public long MaxOrdersPerDay { get; set; }
            public long MaxOpenAlgoOrders { get; set; } //these are STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, and TAKE_PROFIT_LIMIT orders, not market orders

            //all the trading pairs. The booleans are: spot trading allowed, margin trading allowed, oco allowed. There is also iceberg allowed, and some quote precision data available
            //the tuples items are named (string SymbolName, bool SpotAllowed, bool MarginAllowed, bool OcoAllowed)
            public List<(string, bool, bool, bool)> TradingPairs { get; set; }
        }
        #endregion

        #region Quote price data
        public class QuoteData
        {
            public string Symbol { get; set; }
            public double Price { get; set; }
        }

        #endregion

        #region Time data
        public class TimeData
        {
            public long ServerTime { get; set; }
            public DateTime ServerTimeAsDateTime
            {
                get
                {
                    return TimeStampFromBinance(ServerTime);
                }
            }
        }
        #endregion

        #region Account data
        //AccountData, used to get balance prior to submitting order
        //this is not symbol-sprcific
        public class AccountData
        {
            public class BalanceData
            {
                public string Asset { get; set; } //this is just BTC, USDT etc
                public double Free { get; set; }
                public double Locked { get; set; }
            }
            public bool CanTrade { get; set; }
            public bool MakerCommission { get; set; }
            public List<BalanceData> Balances { get; set; }

            //unused here so not filled
            public bool TakerCommission { get; set; }
            public bool CanWidthdraw { get; set; }
            public bool CanDeposit { get; set; }
            public long UpdateTime { get; set; } //won't convert to DateTime from javascript format

            public string AccountType { get; set; }
            public List<string> Permissions { get; set; }

            public DateTime UpdateTimeAsDateTime
            {
                get
                {
                    return TimeStampFromBinance(UpdateTime);
                }
            }
        }
        //public class AccountDataFiltered
        //{
        //    public List<AccountData.BalanceData> Balances { get; set; }
        //}
        #endregion

        #region Order data
        //the ExchangeData-derived objects are all symbol-specific
        public class ExchangeData
        {
            public string Symbol { get; set; }
            public string ClientOrderId { get; set; }
        }

        public class OrderData : ExchangeData
        {
            public long OrderId { get; set; }
            public long OrderListId { get; set; }

            public double Price { get; set; }
            public double OrigQty { get; set; }
            public double ExecutedQty { get; set; }
            public double CummulativeQuoteQty { get; set; }

            //status:
            //Order: NEW, PARTIALLY_FILLED, FILLED, CANCELED, PENDING_CANCEL, REJECTED, EXPIRED (PENDING_CANCEL is currenty Unused)
            //there are some additional enum strings for OCO's
            public string Status { get; set; }

            public string TimeInForce { get; set; } //e.g. GTC
            public string Type { get; set; } //LIMIT, MARKET, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER
            public string Side { get; set; } //BUY or SELL
            public double StopPrice { get; set; }
            public double IcebergQty { get; set; }
            public long Time { get; set; }
            public long UpdateTime { get; set; }
            public long TransactTime { get; set; }
            public bool IsWorking { get; set; }
            public double OrigQuoteOrderQty { get; set; }
            public long OrigClientOrderId { get; set; } //this is for a cancel order, not sure if the OrderId prop. gets populated or this one
            public string ListClientOrderId { get; set; } //for stops
            public string AboveClientOrderId { get; set; } //for stops
            public string BelowClientOrderId { get; set; } //for stops

            public DateTime TimeAsDateTime
            {
                get
                {
                    return TimeStampFromBinance(Time);
                }
            }
            public DateTime UpdateTimeAsDateTime
            {
                get
                {
                    return TimeStampFromBinance(UpdateTime);
                }
            }
            public DateTime TransactTimeAsDateTime
            {
                get
                {
                    return TimeStampFromBinance(TransactTime);
                }
            }
        }

        public class NewOrderData : OrderData
        {
            //note, not all OrderData properties are filled for NewOrderData
            // - properties returned are: OrderId, OrderListId, TransactTime, Price, OrigQty, ExecutedQty, CummulativeQuoteQty, Status, TimeInForce, Type, Side

            public class FillsData
            {
                public double Price { get; set; }
                public double Qty { get; set; }
                public double Commission { get; set; }
                public string CommissionAsset { get; set; }
            }
            public List<FillsData> Fills { get; set; }

            public double AveragePrice
            {
                get
                {
                    return CalculateAverageFillPrice();
                }
            }
            //new order returns a single fill for a market order, but will do average price anyway
            //however, the executed quantity is available in "executedQty" so no need to set that
            internal double CalculateAverageFillPrice()
            {
                var totalQty = Fills.Select(x => x.Qty).Sum();
                if (totalQty > 0)
                {
                    return Fills.Select(x => (double)x.Price * x.Qty / totalQty).Sum();
                }
                return -1;
            }
        }

        public class NewOCOData
        {
            public class OrderReportsData
            {
                public string Symbol { get; set; }
                public string Status { get; set; }
                public string Type  { get; set; } //e.g. STOP_LOSS, TAKE_PRIFIT
                public double StopPrice { get; set; }   
                //Price would presumably be included for stop limit, etc.
                //there are many other properties as well
            }
            public class OrdersData
            {
                public string Symbol { get; set; }
                public long OrderId { get; set; }
                public string ClientOrderId { get; set; }
            }

            public long OrderListId { get; set; }
            public string ListClientOrderId { get; set; } 
            public string ContingencyType { get; set; } //e.g. "OCO"
            public string ListStatusType { get; set; } //e.g. "EXEC_STARTED"
            public string ListOrderStatus { get; set; } //e.g. "EXECUTING"

            public List<OrderReportsData> OrderReports { get; set; }
            public List<OrderData> Orders { get; set; }
        }

        //order lists returns JSON with an anonymous array so can't deserialize it to a specific type
        public List<OrderData> DeserializeToList(System.Text.Json.Nodes.JsonArray jsonArray)
        {
            List<OrderData> orders = [];
            foreach (var v in jsonArray)
            {
                orders.Add(JsonConvert.DeserializeObject<OrderData>(v.ToString()));
            }
            return orders;
        }
        public List<OrderData> GetAllOrders()
        {
            try
            {
                return DeserializeToList(binanceAPI.AllOrdersAsync(symbolName).Result.AsArray());
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to deserialize GetAllOrders(...): {ex}");
                return [];
            }
        }

        public List<OrderData> GetOpenOrders()
        {
            try 
            { 
                return DeserializeToList(binanceAPI.OpenOrdersAsync(symbolName).Result.AsArray());
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to deserialize GetOpenOrders(...): {ex}");
                return [];
            }
        }

        public bool CancelAllOpenOrders()
        {
            try
            {
                var openOrders = GetOpenOrders();
                if (openOrders.Count == 0)
                {
                    return true;
                }
                var v = DeserializeToList(binanceAPI.CancelOpenOrdersAsync(symbolName).Result.AsArray());
                if (v.Count != 0)
                {
                    return (v == null) || !v.Any(x => x.Status != null && x.Status != "CANCELED");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to deserialize CancelAllOpenOrders(...): {ex}");
                return false;
            }
        }
        #endregion

        #endregion

        #region API URLs
        const string TESTNET_URL = "https://testnet.binance.vision/";
        const string LIVE_URL = "https://api.binance.com/";
        #endregion

        #region Trading parameters
        public static double RETAINED_BNB_PERCENTAGE { get; set; }
        #endregion

        #region Fields
        DateTime lastExchangeInfoRequest = new DateTime();
        ExchangeInfoData exchangeInfo = null;
        private long drift;
        BinanceAPI binanceAPI;
        #endregion

        #region Init
        public TradeEngineBinance(string symbolName, int timeInterval) : base(symbolName, timeInterval) => binanceAPI = USE_TESTNET

                ? new BinanceAPI(
                    TESTNET_KEY,
                    TESTNET_SECRET,
                    null,
                    15000,
                    true,
                    TESTNET_URL
                )
                : new BinanceAPI(
                    LIVE_KEY,
                    LIVE_SECRET,
                    null,
                    15000,
                    true,
                    LIVE_URL
                 );

        #endregion

        /*
         * BUY - QuoteOrderQuantity will buy as much of the base asset as can be purchased for the underlying asset,
         *  so BNBBTC will buy as much BNB as can be purchased for a specified amount of BTC.
         * SELL - QuoteOrderQuantity will sell as much of the underlying asset as needed to receive the base asset amount specified
         *  so BNBBTC will sell as much BNB as needed to receive the specified amount of BTC, so the amount specified is in BTC NOT BNB
         *  it seems that, since the minimum trade of BNB is 0.01, the exchange will only trade in MULTIPLES of this amount. If the sell would
         *  require 0.015 BNB to receive the specified amount of BTC, it will only sell 0.01 BNB
         * */


        public override async void Test()
        {


            // exchangeInfo = Deserialize<ExchangeInfoData>(binanceAPI.ExchangeInfoAsync().Result);
            //var _symbol = exchangeInfo.Symbols.Where(x => x.Symbol == "BTCUSDT").FirstOrDefault();

            //var data = new Dictionary<string, string>
            //{
            //    { SYMBOL, "BTCUSDT"},
            //    { SIDE, SELL},
            //    { ABOVE_TYPE, TAKE_PROFIT},
            //    { ABOVE_STOP_PRICE, "85000"},
            //    { QUANTITY, "0.00012"},
            //    { BELOW_TYPE, STOP_LOSS},
            //    { BELOW_STOP_PRICE, "75000"},      
            //};

            //var json =  binanceAPI.NewOcoAsync(data).Result;
            //var r = Deserialize<NewOCOData>(json);


            try
            {

                //todo - stop and tp do not have same ClientOrderId and _Trade.ItemId
                //todo - there are 2 separate orders so can the stop just be updated??
                var stopId = "BRwFaq5dPlJFS830P8N6XX";
                //var re1t = CancelAllOpenOrders();               
                //var ret = GetOpenOrders();
                //var rey1 = CancelAllOpenOrders();  
                //var ret2 = GetOpenOrders();
                return;
              var data = new Dictionary<string, string>
          {
              {SYMBOL, symbolName },
              {LIST_CLIENT_ORDER_ID, stopId}
          };
              var v =  CancelOrderList(data);
                var ret1 = GetOpenOrders();

                int i = 0;

                //var ret2 = GetOpenOrders("BTCUSDT");

      //          var allOrders = GetAllOrders("BTCUSDT");

                //var fiat = GetFreeBalance("USDT");
                //var p = GetValueForSymbol("BTCUSDT");
                //btc *= 0.95;

                #region KEEP - example buy and sell
                //var orderValue = 10.99356;
                //var btc = GetFreeBalance("BTC");
                //Debug.WriteLine($"order value: {orderValue}, BTC value: {btc}");
                //var lastQuoteValue = GetLastPrice(symbolName);
                //var v = GetCorrectPrecisionForTrade(orderValue / lastQuoteValue);
                //Debug.WriteLine($"Corrected order value: {v}");
                //var id = _Trade.GetNewTradeId();
                //var orderEntry = SendOrder(v, Side.BUY, id);
                //if (orderEntry != null ) Debug.WriteLine("order OK");
                //btc = GetFreeBalance("BTC");
                //Debug.WriteLine($"BTC value: {btc}");
                //v = GetCorrectPrecisionForTrade(btc);
                //Debug.WriteLine($"Corrected btc value: {v}");
                //orderEntry = SendOrder(v, Side.SELL, id);
                //if (orderEntry != null ) Debug.WriteLine("order OK");
                //btc = GetFreeBalance("BTC");
                //Debug.WriteLine($"BTC value: {btc}");
                #endregion
            }
            catch (Exception ex)
            {

            }

            return;
        }

        #region Send REST requests

        public NewOrderData SendOrder(double orderQuantity, Side side, string orderId)
        {
            var data = new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {SIDE, side.ToString()},
                {ORDER_TYPE, MARKET },
                {QUANTITY, orderQuantity.ToString() },
                {CLIENT_ORDER_ID_NEW, orderId },
                {RESPONSE_TYPE, FULL }
            };
            return ExecuteOrder(data);           
        }

        public NewOCOData SendOCO(double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId)
        {
            var data = new Dictionary<string, string>
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

                //{ABOVE_STOP_PRICE, 70000}, //so presumably this would be for a buy, not a sell
                //{ABOVE_TIMEINFORCE, 1000},
                //{TRADE_ID_NEW, Stop.ItemId} //generates -1104 if sent
                //{RESPONSE_TYPE, FULL}
                //{TIME_IN_FORCE, GTC}- default anyway
            };

            return ExecuteOCO(data);
        }

        public NewOrderData CancelOrder(string origClientOrderId)
        {
            //there is a stop price as well as a price field???
            var data = new Dictionary<string, string>
            {
                { SYMBOL, symbolName },
                { CLIENT_ORDER_ID_ORIGINAL, origClientOrderId },
                { RESPONSE_TYPE, FULL }
            };
            return CancelReplaceOrder(data);
        }

        #endregion

        #region Receive and process REST data

        public override double GetValueForSymbol(string symbolName)
        {
            if (USE_TESTNET)
            {
                //this can run straight away after starting app
                var priceData = Deserialize<QuoteData>(binanceAPI.TickerPriceAsync(symbolName).Result);
                return priceData.Price;
            }

            return base.GetValueForSymbol(symbolName);

        }

        AccountData GetAccountInfo()
        {
            try 
            {
                return Deserialize<AccountData>(binanceAPI.AccountAsync().Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return null;
            }
        }


        public override double GetFreeBalance(string symbol) //e.g. USDT, BTC
        {
            try
            {
                var accountData = GetAccountInfo();

                //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

                //can ignore locked amount, this is just for trades placed on the exchange which have not yet entered

                if (accountData != null)
                {
                    return (double)accountData?.Balances.FirstOrDefault(x => x.Asset == symbol)?.Free;
                }
                
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return -1;
            }
            return 0;
        }
        public override (double freeBalance, double totalEquity) GetTotalEquity() //e.g. USDT, BTC
        {
            try
            {
                var accountData = GetAccountInfo();

                //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

                double totalEquity = 0;
                double freeEquity = 0;

                //can really ignore locked amount, this is just for trades placed on the exchange which have not yet entered


                /*************/
                /* Note, this only tests for BTC and USDT since testnet has every symbol with some asset valuu */
                foreach (var v in accountData.Balances.Where(x => x.Asset == SymbolNames.FIAT_ASSET || x.Asset == "BTC" && (x.Free > 0 || x.Locked > 0)))
                {
                    if (SymbolNames.SYMBOLS_TO_EXCLUDE.Contains(v.Asset))
                    {
                        continue;
                    }

                    if (v.Asset == SymbolNames.FIAT_ASSET)
                    {
                        //e.g. USDT
                        totalEquity += v.Free + v.Locked;
                        freeEquity += v.Free;
                        //resultMsg += $"{v.Asset}:\t{pc.ToString("0.0")}%\t\t${value.ToString("0.00")}\n";
                    }
                    else if (v.Free + v.Locked > 0)
                    {
                        //e.g. BTC, so get the price of BTC in USDT and calculate the total equity in USDT
                        try
                        {
                            //get the current average price, rather than just look for the last price on the system, since we may not be
                            // actually trading the asset.
                            var priceData = Deserialize<QuoteData>(binanceAPI.TickerPriceAsync(v.Asset + SymbolNames.FIAT_ASSET).Result);
                            var price = priceData.Price;
                            totalEquity += (v.Free + v.Locked) * price;
                            freeEquity += v.Free * price;
                        }
                        catch
                        {
                            LogTrades.WriteLine($"Could not get price for {v.Asset}{SymbolNames.FIAT_ASSET}, skipping in total equity calculation");
                        }
                    }
                }
                
                return (Math.Round(freeEquity, 2), Math.Round(totalEquity, 2));
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return (0, 0);
            }
        }


        bool GetExchangeInfo()
        {
            exchangeInfo = null;
            try
            {
                exchangeInfo = Deserialize<ExchangeInfoData>(binanceAPI.ExchangeInfoAsync().Result); 
                return true;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting exchange info: {e.Message}");
                return false;
            }
        }

        double GetLastPrice()
        {
            JsonNode response = binanceAPI.TickerPriceAsync(symbolName).Result;
            try
            {
                if (double.TryParse(response["price"].ToString(), out double price))
                {
                    return price;
                }
                LogTrades.WriteLine("Failed to get symbol price");
                return 0;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting price info: {e.Message}");
                return 0;
            }

        }

        NewOrderData ExecuteOrder(Dictionary<string, string> data)
        {
            try
            {
                return Deserialize<NewOrderData>(binanceAPI.NewOrderAsync(data).Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return null;
            }
        }

        NewOCOData ExecuteOCO(Dictionary<string, string> data)
        {
            try
            {
                return Deserialize<NewOCOData>(binanceAPI.NewOcoAsync(data).Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return null;
            }
        }

        NewOrderData CancelOrder(Dictionary<string, string> data)
        {
            try
            {
                return Deserialize<NewOrderData>(binanceAPI.CancelOrderAsync(data).Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return null;
            }
        }

        NewOrderData CancelOrderList(Dictionary<string, string> data)
        {
            try
            {
                return Deserialize<NewOrderData>(binanceAPI.CancelOrderListAsync(data).Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error cancelling order list: {e.Message}");
                return null;
            }
        }

        //this is just to reduce the quantity of an existing limit order
        NewOrderData CancelReplaceOrder(Dictionary<string, string> data)
        {
            try
            {
                return Deserialize<NewOrderData>(binanceAPI.CancelReplaceOrderAsync(data).Result);
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return null;
            }
        }

        #endregion

        public override async void StartEntryOrClose_Synchronised()
        {           
            //while we can get the price from the exchange, any stops will be set from that then next update the candle value will be used.
            //This could result in a stop being updated on the exchange unnecessarily if the values differ

            if (!USE_TESTNET && !dataReady) return;

           //todo - error handling

            //1) Get exchange info, if not already present
            if (DateTime.UtcNow.Subtract(lastExchangeInfoRequest).TotalDays > 1)
            {
                exchangeInfo = null; //fetch a new data set, once a day should be fine to keep up to date with any changes
                lastExchangeInfoRequest = DateTime.UtcNow;
            }
            if (exchangeInfo == null)
            {
                if (!GetExchangeInfo())
                {
                    LogTrades.WriteLine($"Could not get exchange info for {symbolName}, exiting StartEntryOrClose_Synchronised");
                    KillEntryOrCloseOrder();
                    return;
                }
                //can have this as a static field, and only request it once. Otherwise, refresh every time a trade executes
                // sending symbol name just validates it's presence, it still returns the lot, (ApiParamNames.SYMBOL, symbolName));
            }


            //2 Execute the trade
            if (Entry != null && Entry.ExchangeMethodScript != null)
            {
                switch (Entry.ExchangeMethodScript)
                {
                    #region New order
                    case NEW_ORDER_SCRIPT:

                        var p = Entry.Position == TradePosition.LONG ? "long" : "short";
                        LogTrades.WriteLine($"Starting new {p} order:");
                        entryTimeStamp = DateTime.UtcNow;

                        //since we are not removing orders if the exchange returns as order closed (no open positions) or stop filled, because these things
                        // can happen in error (and has been observed), there may still be stops on the exchange. These cannot increase positions, but MUST be
                        // removed prior to placing any more orders.

                        //TODO - find if there are any residual open orders and remove them

                        LogTrades.WriteLine("Deleting any residual stops...");
                        //this throws an exception, "unknown order sent"
                        try
                        {
                            var result = binanceAPI.CancelOpenOrdersAsync(symbolName).Result;
                        }
                        catch { }

                        void _KillOrder(string msg, bool interrupted = false)
                        {
                            WriteDeliniatedMessage(msg);
                            KillEntryOrCloseOrder(interrupted);
                        }
                        (_, var balance) = GetTotalEquity();
                        if (balance <= 0)
                        {
                            LogTrades.WriteLine($"Could not get balance for {symbolName}, exiting StartEntryOrClose_Synchronised");
                            KillEntryOrCloseOrder();
                            return;
                        }
                        Entry.Balance = balance;

                        balance = RESTRICTED_TRADE_VALUE > 0 ? RESTRICTED_TRADE_VALUE : balance * 0.95; //this will just be a 95% order for now

                        LogTrades.WriteLine($"Trade amount is {balance} for new order");

                        //save balance (previously saved equity as well, which would be total for all symbols)
                        Entry.TradeReference = Order.TradeReference;//shared with all trades in this group
                        SetUpdateRequest(Entry);

                        //if this has just restarted, will try to enter any trades immediately but the price won't be available from the websocket yet.
                        //so use the REST api instead
                        var lastQuoteValue = GetLastPrice();
                        if (lastQuoteValue == 0)
                        {
                            _KillOrder("Could not get symbol price");
                        }

                        //the problem here is the stops are only calculated AFTER the trade has entered, including any reversal trades.
                        //So that needs to be done first

                        var trades = AllTrades.Where(x => x.TradeReference == Order.TradeReference);

                        //Ceiling = trades.FirstOrDefault(x => x.Action == TradeAction.CEILING);
                        TakeProfit = trades.FirstOrDefault(x => x.Action == TradeAction.TAKE_PROFIT);
                        Stop = trades.FirstOrDefault(x => x.Action == TradeAction.STOP);
                        //Floor = trades.FirstOrDefault(x => x.Action == TradeAction.FLOOR);
                        //need the closest of the current stop or opposing trade. If neither found, use the default floor value
                        double stopPC = 0;
                        double stopPrice = 0;
                        if (Stop != null)
                        {

                            var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == Stop.Id);
                            if (stopLine != null)
                            {
                                (stopPrice, _) = stopLine.GetPriceAtTime(DateTime.UtcNow, true);
                                stopPC = MainObj.GetPriceDiffPercent(currentDataItem, stopPrice, Order.Position) * 100;
                            }
                        }
                        //if (Floor != null)
                        //{
                        //    var floorDiff = MainObj.GetPriceDiffPercent(currentDataItem, Floor.Price, Order.Position) * 100;
                        //    stopPC = floorDiff < stopPC ? floorDiff : stopPC;
                        //}

                        //if there is no stop, take the opposing order as one instead
                        if (stopPC == 0)
                        {
                            var _oppOrder = GetOppositeOrder(Order.Position);
                            if (_oppOrder != null)
                            {
                                var orderLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == _oppOrder.Id);
                                if (orderLine != null)
                                {
                                    LogTrades.WriteLine("Taking opposite order as stop");
                                    (var oppOrderPrice, _) = orderLine.GetPriceAtTime(DateTime.UtcNow, true);
                                    stopPC = MainObj.GetPriceDiffPercent(currentDataItem, oppOrderPrice, Order.Position) * 100;
                                }
                            }
                        }

                        // - is it necessary to pre-compute precision when trading fiat?
                        // surely you can't calculate the amount based on lot size as the price will change before you make the trade?
                        //It may be necessary if you want to sell a specific amount, not the full amount returned by the exchange
                        // when the buy order executed??

                        var tradeAmount = GetCorrectPrecisionForTrade(balance / lastQuoteValue);

                        #region Calculate Leverage
                        //var lotSize = GetLotSize();
                        //var leverage = CalculateLeverageFromStop(stopPC, Entry.Position);
                        //LogTrades.WriteLine($"Stop is at {stopPC} at entry time");

                        //if (MainObj.BACKTEST)
                        //{
                        //    leverage = DEFAULT_LEVERAGE; //so it will always enter
                        //}

                        //(var orderQuantity, var actualLeverage) = CalculateOrderQuantity(amount, lastQuoteValue, leverage, lotSize, Entry.Position);

                        //if (
                        //   (Entry.Position == TradePosition.LONG && actualLeverage < MIN_LEVERAGE_LONG) ||
                        //   (Entry.Position == TradePosition.SHORT && actualLeverage < MIN_LEVERAGE_SHORT))
                        //{
                        //    var s = Entry.Position == TradePosition.LONG ? "long" : "short";
                        //    _KillOrder($"Leverage of {actualLeverage} was too small to make {s} trade worthwhile, exiting");
                        //    SetUpdateRequest(Entry, TradeUpdateRequest.FORCE_DELETE);
                        //    break;
                        //}

                        //Leverage = actualLeverage;

                        ////save the calculated leverage, even if we are not using it
                        //Order.Leverage = Entry.Leverage = Leverage;
                        //Order.Result = AdjustResultForFees(stopPC);

                        ////the correct order quantity is determined by leverage
                        //if (!SetLeverageOnExchange(symbolName, Leverage))
                        //{
                        //    _KillOrder("Failed to set leverage");
                        //    break;
                        //}

                        #endregion

                        //make sure there are no orphaned stops 
                        if (!CancelAllOpenOrders())
                        {
                            LogTrades.WriteLine("Failed to cancel all open orders, exiting new trade");
                            KillEntryOrCloseOrder();
                            break;
                        }
                        Order.ExchangeStatus = Entry.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;
                        Entry.ItemId = _Trade.GetNewTradeId();
                        LogTrades.WriteLine("Getting equity...");
                        (_, Entry.Equity) = GetTotalEquity();
                        LogTrades.WriteLine($"Equity returned as ${Entry.Equity}");
                        LogTrades.WriteLine("Sending order to exchange...");
                        var orderEntry = SendOrder(tradeAmount, Side.BUY, Entry.ItemId);
                        if (orderEntry != null)
                        {
                            var orderValid = true;
                            var orderError = string.Empty;
                            if (!string.IsNullOrEmpty(orderEntry.Status) && orderEntry.Status != "FILLED")
                            {
                                //Possible values are:
                                //  NEW
                                //  PARTIALLY_FILLED
                                //  FILLED
                                //  CANCELED
                                //  REJECTED
                                //  EXPIRED
                                //  EXPIRED_IN_MATCH

                                orderValid = false;
                                orderError += $"Order was not filled, status was {orderEntry.Status}\n";

                                if (orderEntry.AveragePrice == 0)
                                {
                                    orderValid = false;
                                    orderError += "Order returned zero average price\n";
                                }
                                if (orderEntry.ExecutedQty == 0)
                                {
                                    orderValid = false;
                                    orderError += "Order returned zero executed quantity\n";
                                }

                                if (!orderValid)
                                {
                                    _KillOrder(orderError, true);
                                    return;
                                }
                            }
                            var pos = Entry.Position == TradePosition.LONG ? "Long" : "Short";
                            LogTrades.WriteLineOrder($"{pos} order FILLED for {Entry.SymbolName} at {DateTime.UtcNow}");

                            //at this point, trade is entered live
                            EnterTradeOnSystem_synchronised(orderEntry.AveragePrice, orderEntry.ExecutedQty, TradeExchangeStatus.ENTERED_ON_EXCHANGE);

                            EndEntryOrCloseScript();

                            //set stop

                            var stop = tradeEngine.GetTradeForTradeReference(TradeAction.STOP, Entry.TradeReference);
                            if (stop != null)
                            {
                                LogTrades.WriteLine("Using existing stop.");
                                Stop = stop;
                                Stop.Price = stopPrice;
                                Stop.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE; //or will not be entered by base method
                                Stop.Quantity = Entry.Quantity;
                            }
                            else
                            {
                                var stopPercent = GetDefaultStop();// CalculateStopFromLeverage(Entry.Leverage, Entry.Position);
                                if (stopPercent == 0)
                                {
                                    LogTrades.WriteLine("Could not get default value for stop for this timeframe, using 1%");
                                    stopPercent = 1;
                                }
                                else
                                {
                                    LogTrades.WriteLine($"Setting stop at {stopPercent}%");
                                }

                                Stop = Entry.Clone();
                                //although precision is set when OCO is sent to exchange, it helps to keep the number of decimal places down anyway.
                                var price = GetCorrectPrecisionForPrice(MainObj.GetFloorPrice(Entry.Price, stopPercent, Order.Position));
                                Stop.Price = Stop.Result = price;
                                Stop.Action = TradeAction.STOP;
                                Stop.Quantity = Entry.Quantity;
                                Stop.ItemId = null; //need a new ID for this as it is a new order.
                                Stop.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;

                                AddLineToTrade_synchronised(Stop);
                            }

                            LogTrades.WriteLine("Sending stop to exchange");
                            SetStopOnExchange_Synchronised();
                            SetUpdateRequest(Stop);

                            //cannot have any synched objects after this as synchronisation will exit before this method completes
                            LogTrades.WriteLine("Getting exchange conversion rate");
                            var gbp = await GetConversionRate(Entry.ItemId);
                            LogTrades.WriteLine($"Conversion rate received of {gbp.ToString("F2")}, Entry complete!");
                        }
                        break;
                        #endregion

                    #region Close order
                    case CLOSE_ORDER_SCRIPT:

                        entryTimeStamp = DateTime.UtcNow;
                        if (Close == null)
                        {
                            LogTrades.WriteLine($"No Close set in CLOSE_ORDER_SCRIPT for {symbolName}, cannot close trade, exiting OnExchangeDataReceived");
                            KillEntryOrCloseOrder(true);
                            return;
                        }

                        LogTrades.WriteLine("Closing order...");

                        //var baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                        //if (GetFreeBalance(baseAsset) == -1)
                        //{
                        //    LogTrades.WriteLine($"Could not get balance info for {symbolName}");
                        //}
                        /****************************************************************************/
                        //cancel stops if they are still present, but if trade has closed, we are done
                        /****************************************************************************/


                        //if the base asset balance is very small, assume the trade has exited on the exchange, so go no further

                        try
                        {
                            //remove the stop
                            CancelAllOpenOrders();

                            if (IsTradeClosed()) //i.e there is no BTC
                            {
                                LogTrades.WriteLine("No BTC, closing trade locally.");
                                CloseTradeOnSystem_synchronised(currentDataItem.Close);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTrades.WriteLine("Could not get asset balance, will attempt to close trade anyway...");
                        }

                        /*********************/
                        //generate close order
                        /*********************/

                        //a QUANTITY order specifies the amount of base asset to buy or sell, so can go directly from here

                        LogTrades.WriteLine($"Closing order for {symbolName}...");
                        var _baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                        if (_baseAsset != null)
                        {
                            var coinBalance = GetFreeBalance(_baseAsset);

                            LogTrades.WriteLine($"Using received balance of {coinBalance} {_baseAsset}");

                            /***************************************/
                            //retain some BNB to reduce trading fees
                            /***************************************/

                            double amountToSell = 0;
                            if (_baseAsset == "BNB" && RETAINED_BNB_PERCENTAGE > 0)
                            {
                                coinBalance *= (1 - RETAINED_BNB_PERCENTAGE / 100);
                                LogTrades.WriteLine($"Retaining {RETAINED_BNB_PERCENTAGE}% of BNB");
                                var amount1 = GetCorrectPrecisionForTrade(coinBalance);
                                amountToSell = amount1;
                            }
                            else
                            {
                                amountToSell = coinBalance;
                            }


                            if (amountToSell > 0)
                            {
                                Close.Quantity = GetCorrectPrecisionForTrade(amountToSell);

                                LogTrades.WriteLine($"Placing sell order of {Close.Quantity}, adjusted for step size, at {DateTime.Now}");
                                SetUpdateRequest(Close);

                                var dataClose = SendOrder(Close.Quantity, Side.SELL, Close.ItemId);

                                if (dataClose == null)
                                {
                                    LogTrades.WriteLine("Failed to close order on the exchange, exiting");
                                    return;
                                }

                                /*********************/
                                //Set order on system
                                /*********************/
                                //if status is 'NEW' then order is accepted but not yet filled
                                //status could be 'PARTIALLY_FILLED' (limit orders only??)

                                switch (dataClose.Status)
                                {
                                    case "FILLED":
                                        //price will be zero on testnet, so use the current price instead
                                        dataClose.Price = dataClose.Price == 0 ? currentDataItem.Close : dataClose.Price;

                                        LogTrades.WriteLineOrder($"Close order filled, closing trade on system at {DateTime.UtcNow}");
                                        CloseTradeOnSystem_synchronised(dataClose.Price);

                                        EndEntryOrCloseScript();
                                        LogTrades.WriteSpacer();

                                        //this will cause synchronisation to exit before this awaitable method completes,
                                        // so CANNOT have any synched objects accessed after this (like Close, etc.)
                                        await GetConversionRate(Close.ItemId);

                                        break;

                                    case "CANCELED":
                                    case "REJECTED":
                                    case "EXPIRED":
                                        //can try again
                                        LogTrades.WriteLineOrder($"Trade was {dataClose.Status} in CLOSE_ORDER_SCRIPT for {Close.SymbolName} at {DateTime.UtcNow}");
                                        KillEntryOrCloseOrder(true);

                                        break;
                                }

                                tradeStatusChanged = true;

                                ShowAccountBalances(); //will log balance
                            }
                            else
                            {
                                LogTrades.WriteLine($"Quote asset balance was zero so could not close trade for {symbolName}"); //this can happen multiple times if unsyncronised with server
                            }
                        }

                        break;
                    #endregion

                    #region unused
                    /*                         
                    if (Stop != null && Stop.ExchangeMethodScript != null)//  || (TempStop != null && TempStop.ExchangeMethodScript != null))
                    {

                        switch (Stop.ExchangeMethodScript)
                        {

                            case SET_STOP_SCRIPT:

                                //Stop will not be null here

                                //process is:
                                //1) remove any existing stop
                                //2) set a new stop to the current order value


                                //remove existing stop


                                if (!string.IsNullOrEmpty(Stop.ItemId))
                                {
                                    response = binanceAPI.CancelOrderAsync(new Dictionary<string, string>
                            {
                                {SYMBOL, symbolName},
                                {ORIG_CLIENT_ORDER_ID, Stop.ItemId }
                            }).Result;

                                    //kill the stop ItemId - 
                                    // if the stops are cancelled on the exchange, but the confirmation is not received, as seems to happen quite often,
                                    // then the original ItemId will persist here and next time, an attempt to remove this stop from the exchange will fail
                                    // so the stop will never get put on the exchange.
                                    Stop.ItemId = null;
                                    SetUpdateRequest(Stop);
                                }

                                //set new stop


                                //don't want to pull the quote data each time a stop gets updated, so will just use the curent ticker price
                                var lastValue = GetValueForSymbol(symbolName);

                                Stop.ItemId = _Trade.GetNewTradeId();
                                SetUpdateRequest(Stop);
                                baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                                var coinBalance = GetFreeBalance(baseAsset);

                                var amount = GetCorrectPrecisionForTrade(coinBalance);


                                //An OCO has 2 legs called the above leg and below leg.
                                //One of the legs must be a LIMIT_MAKER order and the other leg must be STOP_LOSS or STOP_LOSS_LIMIT order.
                                //Price restrictions:
                                //If the OCO is on the SELL side: LIMIT_MAKER price > Last Traded Price > stopPrice
                                //If the OCO is on the BUY side: LIMIT_MAKER price < Last Traded Price < stopPrice


                                //this is calling the rest.js method correctly, so the error is within this data
                                //(-1104 "not all sent parameters were read")
                                //(-1106 "a parameter was sent when not required")
                                //(-1102 "A mandatory parameter was not sent, was empty/null, or malformed")
                                //(-1128 "Combination of optional parameters invalid.")
                                //(-2011 "Unknown order sent")
                                //(-1013 - this was "Stop loss orders are not supported for this symbol." but is not documented as an error code (only 1014 and beyond)

                                //can swap prices around, still get the same -1013 error
                                //"Stop loss orders are not supported for this symbol."


                                float abovePrice = 0;
                                if (TakeProfit != null)
                                {
                                    abovePrice = (float)TakeProfit.Price;
                                }
                                else
                                {
                                    //must have an initial t/p for the OCO. In case the auto-set ceiling has been removed for some reason, make the calculation here
                                    var initialTP = 0.1; //10%
                                    abovePrice = (float)(
                                        Entry.Position == TradePosition.LONG
                                            ? lastValue * (1 + initialTP)
                                            : lastValue * (1 - initialTP)
                                    ); //same calculation as would be used for ceiling
                                }
                                float triggerPrice = (float)Stop.Price;

                                //this is just to ensure the stop executes immediately, not as a limit order
                                //but if the belowPrice is too low (like half the trigger price) a small order (test order) may not have a large enough notional value
                                float belowPrice = (float)(triggerPrice * 0.9);
                                //var symbol = exchangeInfo.Symbols.FirstOrDefault(x => x.Symbol == symbolName);
                                //var minSize = symbol.Filters.Where(x => x.FilterType == "NOTIONAL").FirstOrDefault();


                                //THIS IS DEPRECATED, DIFFERENT PARAMETERS NOW 

                                response = binanceAPI.NewOcoAsync(new Dictionary<string, string>
                        {
                            //{SYMBOL, Stop.SymbolName},
                            //{SIDE, SELL},
                            //{QUANTITY, coinBalance.ToString()},         //generates -1013 for btc or usdt value - minimum trade value is 0.0001 BTC
                            //{ABOVE_TYPE, LIMIT_MAKER},
                            //{ABOVE_PRICE, abovePrice.ToString()},       //generates -1102 if missing
                            //{BELOW_TYPE, STOP_LOSS_LIMIT},
                            //{BELOW_STOP_PRICE, triggerPrice.ToString()},//generates -1128 if missing
                            //{BELOW_PRICE, belowPrice.ToString()},       //this is needed also
                            //{BELOW_TIMEINFORCE, "GTC"}
                            //{ABOVE_STOP_PRICE, 70000}, //so presumably this would be for a buy, not a sell
                            //{ABOVE_TIMEINFORCE, 1000},
                            //{TRADE_ID_NEW, Stop.ItemId} //generates -1104 if sent
                            //{RESPONSE_TYPE, FULL}
                            //{TIME_IN_FORCE, GTC}- default anyway
                        }).Result;

                                var data = JsonConvert.DeserializeObject<NewOCOData>(response.ToString());
                                if (data != null)
                                {
                                    Stop.ItemId = null;
                                    Stop.ExchangeMethodScript = null;
                                    Stop.ExchangeMethodIndex = 0;
                                    Stop.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                                    SetUpdateRequest(Stop);

                                    if (isManualSynch)
                                    {
                                        LogTrades.WriteLine("OCO synchronised");
                                        isManualSynch = false;
                                    }
                                }

                                tradeStatusChanged = true;
                                break;

                            case CANCEL_STOP_SCRIPT:

                                response = binanceAPI.CancelOrderAsync(new Dictionary<string, string>
                        {
                            {SYMBOL, symbolName},
                            {ORIG_CLIENT_ORDER_ID, Stop.ItemId}
                        }).Result;

                                //todo - needs error handling
                                RemoveTradeFromLine_UI_synchronised(Stop);
                                SetUpdateRequest(Stop, TradeUpdateRequest.DELETE);
                                break;

                        }
                    }
                    */
                    #endregion

                    //No more access to Entry, Close etc. after call to async GetConversionRate()!!!
                }

                MainObj.DrawCurrentDispatcher();
            }


        }

        _Trade GetOppositeOrder(TradePosition position)
        {
            var oppositeSide = position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
            return AllTrades.FirstOrDefault(x =>
                                            x.Action == TradeAction.ORDER &&
                                            x.Position == oppositeSide &&
                                            x.ItemStatus == TradeItemStatus.OPEN &&
                                            x.GlobalStatus == TradeGlobalStatus.ACTIVE);
        }

        protected override void EnterTradeOnSystem_synchronised(double price, double quantity, TradeExchangeStatus orderType)
        {

            //note, Quantity is double not decimal, as decimal has 18 digits and 2 decimal places only in db
            try
            {
                hasSpiked = false;

                var currentTradeIndicator = GetCurrentTradeIndicator(Order);

                //get the EF tracked trade - if this is a live entry, will be using a different data context to the one the parameter is running on

                Order.ExchangeStatus = orderType;

                SetUpdateRequest(Order);

                //set trade time, frame and price to entry point (line entry will be different from order position)
                //enter trade on system here, but not active yet
                if (Entry == null)
                {
                    //if not trading live, will bypass exchange (if not using simulator) and come straight here, so no entry yet
                    Entry = Order.Clone();
                    Entry.Action = TradeAction.ENTRY;
                    Entry.Timestamp = GetTimeStamp();
                    SetUpdateRequest(Entry);
                }

                Entry.Price = Order.Price = (double)price;
                Entry.Quantity = Order.Quantity = quantity; //set both the entry and order quantity, as either may be used when closing, etc.
                Entry.ExchangeStatus = orderType;
                SetUpdateRequest(Entry);

                Order.ItemStatus = TradeItemStatus.CLOSED; //close the order, this is now an active trade
                SetUpdateRequest(Order);

                //Set flip from opposing trade
                //if there is an opposing trade:
                // 1) convert any opposing trade stop to waiting stop
                // 2) convert opposing trade to flip order



                var firstOppositeOrder = GetOppositeOrder(Order.Position);
                if (firstOppositeOrder != null)
                {
                    //if (stop != null)
                    //{
                    //    //remove any line
                    //    var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == stop.Id);
                    //    if (stopLine != null)
                    //    {
                    //        SetUpdateRequest(stopLine, TradeUpdateRequest.FORCE_DELETE);
                    //    }
                    //    SetUpdateRequest(stop, TradeUpdateRequest.FORCE_DELETE);
                    //}


                    // 1) convert any opposing trade stop to waiting stop and remove any opposite take profit, ceiling or break even trades
                    List<_Trade> oppTrades = [.. AllTrades.Where(x => x.TradeReference == firstOppositeOrder.TradeReference)];

                    var oppStop = oppTrades.FirstOrDefault(x => x.Action == TradeAction.STOP);
                    if (oppStop != null)
                    {
                        //use this stop
                        oppStop.TradeReference = Order.TradeReference;
                        oppStop.Type = TradeType.STOP_WAITING;
                        oppStop.Action = TradeAction.WAITING_STOP;
                        SetUpdateRequest(oppStop);

                        var oppStopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeReference == oppStop.TradeReference);
                        if (oppStopLine != null)
                        {
                            oppStopLine.TradeReference = Order.TradeReference;
                            oppStopLine.Type = LineType.SUPPORT;
                            SetUpdateRequest(oppStopLine);
                        }
                    }

                    else
                    {
                        //add a waiting stop at the original order price
                        var trade = Order.Clone();
                        trade.Id = 0; //so it will be added to db
                        trade.ItemId = null;
                        trade.ItemStatus = TradeItemStatus.OPEN;
                        trade.Action = TradeAction.WAITING_STOP;
                        trade.Timestamp = DateTime.UtcNow;
                        trade.TimeFrame = TimeFrame;

                        //this may be an existing Moving Average stop, if so just changing the Type will stop the moving average update loop (see Order::ExecuteStop_synchronised())
                        trade.Type = TradeType.STOP_WAITING;
                        //flip side if this is a waiting stop
                        trade.Quantity = Order.Quantity;
                        trade.Position = Order.Position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
                        trade.ExchangeStatus = TradeExchangeStatus.NO_WARNING;
                        SetUpdateRequest(trade);
                    }

                    // 2) convert opposing trade to flip order (which is also a stop)
                    var line = AllLinesForSymbol.FirstOrDefault(x => x.TradeReference == firstOppositeOrder.TradeReference);
                    if (line != null)
                    {
                        line.TradeReference = Order.TradeReference;
                        line.Type = LineType.SUPPORT;
                        SetUpdateRequest(line);
                    }

                    firstOppositeOrder.TradeReference = Order.TradeReference;
                    firstOppositeOrder.Action = TradeAction.STOP;
                    firstOppositeOrder.Type = TradeType.FLIP_ORDER;
                    firstOppositeOrder.Position = Order.Position; //because a flip is the same side as the order it is flipping.
                    SetUpdateRequest(firstOppositeOrder);
                }

            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Exception in EnterTradeOnSystem  at {DateTime.UtcNow} (1): {e}");
            }

            if (!MainObj.IsBacktesting)
            {
                //MainObj.PlaySound(MainObj.ALERT_SOUND);
            }
        }

        bool IsTradeClosed()
        {
            if (exchangeInfo == null)
            {
                GetExchangeInfo();
            }
            var __baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
            if (__baseAsset != null)
            {
                var __symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                if (__symbol != null)
                {
                    var lotSize = __symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
                    if (lotSize != null)
                    {
                        var accountData = GetAccountInfo();
                        var _minValue = lotSize.MinQty * 2; //it should always use the lot size notional value, same as MinQty, to calculate trade quantity so would only leave < MinQty but double this to be safe
                        var _baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault();
                        var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
                        return _baseAssetBalanceLocked < _minValue && _baseAssetBalanceFree < _minValue; //i.e there is no BTC
                    }
                }
            }
            return false;
        }

        #region Stop 
        public override void SynchStopsWithExchange_synchronised()
        {
            InitForDataContext(() =>
            {
                if (Entry != null)
                {
                    if (Stop != null)
                    {
                        isManualSynch = true;
                        SetStopOnExchange_Synchronised();

                        //do a timed check to make sure this has synchronised correctly
                        var timer = new System.Timers.Timer(2000)
                        {
                            AutoReset = false
                        };
                        timer.Start();
                        timer.Elapsed += (o, e) =>
                        {
                            if (isManualSynch)
                            {
                                LogTrades.WriteLine("Failed to synchronise OCO with exchange");
                                isManualSynch = false;
                            }
                        };
                    }
                }
                else
                {
                    LogTrades.WriteLine("Not entered on exchange - cannot synchronise stops with exchange.");
                }
            });
        }

        // will create a new TakeProfit if one is not already set
        public override void SetStopOnExchange_Synchronised()
        {
            base.SetStopOnExchange_Synchronised();

            if (
                USE_SIMULATED_EXCHANGE ||
                Entry == null ||
                Entry.ExchangeStatus != TradeExchangeStatus.ENTERED_ON_EXCHANGE ||
                Stop == null ||
                Stop.Action == TradeAction.WAITING_STOP
                )
            {
                return;
            }


            double lastPrice;
            lastPrice = GetValueForSymbol(symbolName);
            
            if (lastPrice == 0)
            {
                LogTrades.WriteLine($"Could not get price for Stop, Stop not set");
                return;
            }

            //don't want to do updates on the exchange if the stop is flat
            if (Math.Abs(Stop.Price / lastPrice) > 0.9999) //less than 0.01% difference 
            {
                return;
            }

            if (Stop.ExchangeStatus == TradeExchangeStatus.ENTERED_OFF_EXCHANGE && Stop.Type != TradeType.STOP_WAITING)
            {
                LogTrades.WriteLine($"Did not set Stop on exchange, exchange status was {Stop.ExchangeStatus} - you need to add a new stop!");
                Stop.ExchangeStatus = TradeExchangeStatus.NO_WARNING;
                SetUpdateRequest(Stop);
                return;
            }

            if (Stop.Position == TradePosition.SHORT && Stop.Price < lastPrice || Stop.Position == TradePosition.LONG && Stop.Price > lastPrice)
            {
                LogTrades.WriteLine($"Did not set Stop on exchange, price was wrong side of candle data");
                return;
            }

            if (Stop.Quantity == 0 && Entry != null && Order.ActionValue != 0)
            {
                //this will be called from Order when setting stops automatically
                Stop.Quantity = Entry.Quantity;
                LogTrades.WriteLine($"Stop quantity was zero, Stop not yet set, setting Stop...");
            }

            bool isUpdating = Stop.ItemId != null;

            Stop.ItemId ??= _Trade.GetNewTradeId();

            //will add a Take Profit since Binance require an OCO
            if (TakeProfit == null)
            {
                TakeProfit = Entry.Clone();
                TakeProfit.Action = TradeAction.TAKE_PROFIT;
                TakeProfit.Timestamp = GetTimeStamp();
                TakeProfit.Quantity = Entry.Quantity;
                TakeProfit.ItemId = _Trade.GetNewTradeId();
                TakeProfit.Price =
                    Entry.Position == TradePosition.LONG
                        ? lastPrice * (1 + INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100)
                        : lastPrice * (1 - INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100);
                    

                AddLineToTrade_synchronised(TakeProfit);
                SetUpdateRequest(TakeProfit);
            }


            if (Stop.ExchangeStatus == TradeExchangeStatus.ENTERING_ON_EXCHANGE)
            {
                //trade is entering, run synchronously and allow enclosing InitForDataContext() to save changes
                Stop.ExchangeMethodScript = TakeProfit.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                SetUpdateRequest(Stop);
                SetUpdateRequest(TakeProfit);

                if (SendOCOToExchange(false)) //not updating, so will not remove OCO first
                {
                    Stop.ExchangeStatus = TakeProfit.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                    Stop.ExchangeMethodScript = TakeProfit.ExchangeMethodScript = null;
                }
            }
            else
            {
                //trade is running, update stop (asynchronously using a new data context???)
                //however, we don't want to send updates unless the stop price has actually changed
                //can just use the Result property to save the previous price
                if (Stop.Result != Stop.Price)
                {
                    Stop.Result = Stop.Price;

                    Stop.ExchangeMethodScript = TakeProfit.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                    SetUpdateRequest(Stop);
                    SetUpdateRequest(TakeProfit);

                    //await Task.Run(() =>
                    //{
                    //    InitForDataContext(() =>
                    //    {

                    if (SendOCOToExchange(true))
                    {
                        Stop.ExchangeStatus = TakeProfit.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                        Stop.ExchangeMethodScript = TakeProfit.ExchangeMethodScript = null;
                        SetUpdateRequest(Stop);
                        SetUpdateRequest(TakeProfit);
                    }

                    //    });
                    //});
                }
            }
            

        }

        //could not get a stop order to work, there is no API call for this specifically and a trade set as a stop just won't be accepted
        public bool SendOCOToExchange(bool isUpdating)
        {
            if (Stop == null || Stop.Quantity == 0 || TakeProfit == null) //can be zero if attempting to move a closed stop
            {
                return false; //happens with locally placed order
            }

            try
            {
                if (!isUpdating || RemoveStopFromExchange_Synchronised())
                { 
                    //TradeReference - shared with all trades in this set
                    //ItemId - unique to this trade, sent to exchange
                    //TradeIdExchange - long, returned from exchange

                    //it is vital that the precision is correct, in case this has been missed in the UI somewhere do it here as well
                    var stopPrice = GetCorrectPrecisionForPrice(Stop.Price);
                    var takeProfitPrice = GetCorrectPrecisionForPrice(TakeProfit.Price);
                    var orderStop = SendOCO(Entry.Quantity, stopPrice, Stop.ItemId, takeProfitPrice, TakeProfit.ItemId);

                    if (orderStop == null)
                    {
                        return false;
                    }
                    if (orderStop.OrderReports != null)
                    {
                        var stopResponse = orderStop.OrderReports.FirstOrDefault(x => x.Type == "STOP_LOSS" && x.Status == "NEW");
                        if (stopResponse == null)
                        {
                            WriteDeliniatedMessage($"Stop order was rejected: {stopResponse.Status}");
                            return false;
                        }
                    }

                    //can only cancel the OCO from the listClientOrderId so this can be set in place of the stop itemId
                    Stop.OrderListId = orderStop.ListClientOrderId;
                    SetUpdateRequest(Stop);                
                }
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to amend or send stop: {ex}");
                return false;
            }
            return true;
        }


        protected override bool RemoveStopFromExchange_Synchronised()
        {
            //originally with Bitmex this would have the stop or floor ID passed in, but with Binance since we don't have Floors now,
            // just the single stop (OCO) then that is not needed
            var data = new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {LIST_CLIENT_ORDER_ID, Stop.OrderListId}
            };
            var orderData = CancelOrderList(data);

            if (orderData != null)
            {
                if (!string.IsNullOrEmpty(orderData.Status))
                {
                    WriteDeliniatedMessage($"Stop cancel order was rejected: {orderData.Status}, stop not updated / replaced");
                    return false;
                }
            }
            return true;
            
        }

        #endregion

        #region Update state from server
        protected override void UpdateLiveState()
        {

            if (USE_SIMULATED_EXCHANGE)
            {
                return;
            }

            InitForDataContext(() =>
            {
                if (Entry != null && Entry.ExchangeMethodScript == null && Entry.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                {
                    //this is tricky with Binance, because there is no live running trade - only account balances
                    //so if there is a base asset balance, the trade is still running. Otherwise, close it entirely.
                    Entry.ExchangeMethodScript = UPDATE_LIVE_STATE_SCRIPT;

                    if (IsTradeClosed())
                    {
                        //not accurate, there could be a minute elapsed since the stop triggered on the exchange
                        //BUT the app should have closed the trade locally by then.
                        //alternative is to interrogate the stop and TP orders to see which has triggered

                        //just mark the trade as closed here
                        CloseTradeOnSystem_synchronised(GetLastPrice());
                    }
                    else
                    {
                        //in the event there is a pending entry or stop operation on the exchange, do not do anything. This is unlikely but it would mess things up.
                        if (Stop != null && Stop.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE && Stop.ExchangeMethodScript != EXCHANGE_NOT_SYNCHED)
                        {
                            var orders = GetOpenOrders();
    
                            if (orders != null && !orders.Any(x => x.ClientOrderId == Stop.ItemId))
                            {
                                //mark the stop as not synchronised
                                Stop.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                                SetUpdateRequest(Stop);
                                LogTrades.WriteLine("No locked balance on exchange, assume stop cancelled, check exchange!!");
                            }
                        }
                    }
                }
                
            });
        }

        #endregion

        #region Conversion rate
        //NOTE on use of async:
        //If you want to call the method outside of an async method:
        // You can only use Result if the method has a return type (obvious), BUT it will call the method on the same thread which
        // will completely block execution. Not sure why but the BinanceAPI methods all use ConfigureAwaiter(false) which seems to stop the problem
        // but that only causes the return type to be an awaitable Task. The BinanceAPI methods then retrieve content from that and nothing seems 
        // to block but that does not work here. Just leave everything async.
        async Task<double> GetConversionRate(string tradeId)
        {
            try
            {
                using var client = new OpenExchangeRatesClient("51581f4479b84a29921ca25369900524");
                var latestRatesResponse = await client.GetLatestRatesAsync();
                var gbp = latestRatesResponse.Rates.FirstOrDefault(x => x.Key == "GBP").Value;

                using DataContext db = new(); //Close can drop out of context after the await method call above
                var trade = db.Trades.FirstOrDefault(x => x.ItemId == tradeId);
                if (trade != null)
                {
                    trade.ExchangeRate = (double)gbp;
                    db.SaveChanges();
                }

                return (double)gbp;
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to get currency conversion rate: {ex}");
                return 0;
            }
        }
    #endregion

        #region Utility methods  

        //Get the correct precision for a trade and return the amended amount, this is for the base asset
        //Note, the symbol will be BTCUSDT, all the symbols are trading pairs
        //All trades are in base asset, both sell and buy (e.g. BTC)
        private double GetCorrectPrecisionForTrade(double balanceToTrade)
        {
            if (balanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
            {
                if (exchangeInfo == null)
                {
                    GetExchangeInfo();
                }
                if (exchangeInfo != null)
                {
                    var _symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                    if (_symbol == null)
                    {
                        LogTrades.WriteLine($"Could not find symbol in ExchangeInfo for {symbolName}");
                        return -1;
                    }

                    var lotSize = _symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
                    if (lotSize == null)
                    {
                        LogTrades.WriteLine($"Could not find lot size in ExchangeInfo for {symbolName}");
                        return -1;
                    }
                    var stepSize = lotSize.StepSize;
                    var ret = (int)(balanceToTrade / stepSize) * stepSize;
                    var p = (int)Math.Log10(stepSize);
                    ret = Math.Round(ret, -p);
                    return ret;
                }
            }
            return -1;
        }

        //as above, but this is for the quote asset (USDT)
        private double GetCorrectPrecisionForPrice(double balanceToTrade)
        {
            if (balanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
            {
                if (exchangeInfo == null)
                {
                    GetExchangeInfo();
                }
                if (exchangeInfo != null)
                {
                    var _symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                    if (_symbol == null)
                    {
                        LogTrades.WriteLine($"Could not find symbol in ExchangeInfo for {symbolName}");
                        return -1;
                    }

                    var priceFilter = _symbol.Filters.Where(x => x.FilterType == "PRICE_FILTER").FirstOrDefault();
                    if (priceFilter == null)
                    {
                        LogTrades.WriteLine($"Could not find lot size in ExchangeInfo for {symbolName}");
                        return -1;
                    }
                    var stepSize = priceFilter.TickSize;
                    if (stepSize > 0)
                    {
                        var ret = (int)(balanceToTrade / stepSize) * stepSize;
                        var p = (int)Math.Log10(stepSize);
                        ret = Math.Round(ret, -p);
                        return ret;
                    }

                }
                //just in case, just round it down
                return Math.Round(balanceToTrade, 2);
            }
            return -1;
        }

        void WriteDeliniatedMessage(string msg)
        {
            LogTrades.WriteLine($"\n**************************************\n{msg}\n**************************************");
        }

        #endregion
    } 
}

