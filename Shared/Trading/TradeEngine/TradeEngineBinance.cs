using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using System.Text.Json.Nodes;
using static AlgoTrader.Shared.BinanceAPI;
using static AlgoTrader.Shared.ExchangeConnection.ExchangeDataProcessorBinance;

namespace AlgoTrader.Shared
{
    public class TradeEngineBinance : TradeEngine
    {
        public override async void Test()
        {

        }

        /*
         * BUY - QuoteOrderQuantity will buy as much of the base asset as can be purchased for the underlying asset,
         *  so BNBBTC will buy as much BNB as can be purchased for a specified amount of BTC.
         * SELL - QuoteOrderQuantity will sell as much of the underlying asset as needed to receive the base asset amount specified
         *  so BNBBTC will sell as much BNB as needed to receive the specified amount of BTC, so the amount specified is in BTC NOT BNB
         *  it seems that, since the minimum trade of BNB is 0.01, the exchange will only trade in MULTIPLES of this amount. If the sell would
         *  require 0.015 BNB to receive the specified amount of BTC, it will only sell 0.01 BNB
         * */

        //TODO
        public override void ShowAccountBalances()
        {

        }

        #region Binance data classes (JSON deserializes into these directly)

        protected static T Deserialize<T>(JsonNode jsonNode)
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
        ExchangeInfoData exchangeInfoData = null;
        protected BinanceAPI binanceAPI;
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

        #region Send REST requests

        #region Account info
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

        //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset
        protected override double GetFreeBaseAssetBalance(string symbol) 
        {
            try
            {
                var accountData = GetAccountInfo();

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

        protected override (double freeBalance, double totalEquity) GetTotalEquity() //e.g. USDT, BTC
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
        #endregion

        #region Exchange info
        ExchangeInfoData GetExchangeInfo()
        {
            if (exchangeInfoData == null || DateTime.UtcNow.Subtract(lastExchangeInfoRequest).TotalDays > 1)
            {
                //fetch a new data set, once a day should be fine to keep up to date with any changes
                lastExchangeInfoRequest = DateTime.UtcNow;

                try
                {
                    exchangeInfoData = Deserialize<ExchangeInfoData>(binanceAPI.ExchangeInfoAsync().Result);
                }
                catch (Exception e)
                {
                    LogTrades.WriteLine($"Error getting exchange info: {e.Message}");
                    return null;
                }
            }
            return exchangeInfoData;
        }

        #endregion

        #region Orders
        protected override bool SendOrder(double orderQuantity, Side side, string orderId, out double averagePrice, out double executedQuantity)
        {
            averagePrice = executedQuantity = 0;
            var data = new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {SIDE, side.ToString()},
                {ORDER_TYPE, MARKET },
                {QUANTITY, orderQuantity.ToString() },
                {CLIENT_ORDER_ID_NEW, orderId },
                {RESPONSE_TYPE, FULL }
            };
            try
            {
                var result = Deserialize<NewOrderData>(binanceAPI.NewOrderAsync(data).Result);
                if (result == null)
                {
                    LogTrades.WriteLine("Send order returned null");
                    return false;
                }
                if (!string.IsNullOrEmpty(result.Status) && result.Status != "FILLED")
                {
                    if (result.AveragePrice == 0)
                    {
                        LogTrades.WriteLine ("Order returned zero average price");
                    }
                    if (result.ExecutedQty == 0)
                    {
                        LogTrades.WriteLine("Order returned zero executed quantity");
                    }
                    return false;

                }
                averagePrice = result.AveragePrice;
                executedQuantity = result.ExecutedQty;
                return true;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return false;
            }
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

            try
            {
                var ret = Deserialize<NewOCOData>(binanceAPI.NewOcoAsync(data).Result);

                var stopResponse = ret.OrderReports.FirstOrDefault(x => x.Type == "STOP_LOSS" && x.Status == "NEW");
                if (stopResponse == null)
                {
                    //presumably if the TakeProfit was rejected, the entire list would fail so there would be no stop anyway
                    LogConnection.WriteLine($"Stop (OCO) order was rejected: {stopResponse.Status}");
                    return null;
                }

                return ret;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing order: {e.Message}");
                return null;
            }
        }

        //this would only be used for limit orders, not stops, as they are part of an OCO which is cancelled via OrderListId
        public NewOrderData CancelOrder(string origClientOrderId)
        {
            //there is a stop price as well as a price field???
            var data = new Dictionary<string, string>
            {
                { SYMBOL, symbolName },
                { CLIENT_ORDER_ID_ORIGINAL, origClientOrderId },
                { RESPONSE_TYPE, FULL }
            };
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
        #endregion

        #region Price info
        protected override double GetLastPrice()
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
        #endregion

        #endregion

        #region Enter and close trades
        protected override bool InitForEntry()
        {
            if (GetExchangeInfo() == null)
            {
                KillEntryOrCloseOrder(true);
                return false;
            }
            return true;
        }
        protected override bool InitForClose(_Trade triggeringTrade) 
        {
            if (GetExchangeInfo() == null)
            {
                KillEntryOrCloseOrder(true);
                return false;
            }
            return true;
        }
        
        protected override bool CancelOpenOrders()
        {
            bool ret = false;
            var openOrders = GetOpenOrders();
            if (openOrders.Count == 0)
            {
                ret = true;
            }
            else
            {
                //if there are no orders to cancel then this will throw an exception with 'unknown order sent' hence need to check for open orders first
                //generally when null is returned the operation has failed so assume null is a fail to cancel the orders for some reason without exception or error
                var v = DeserializeToList(binanceAPI.CancelOpenOrdersAsync(symbolName).Result.AsArray());
                if (v != null)
                {
                    if (v.Count != 0)
                    {
                        //if any order does not have cancelled status, then assume stops not removed
                        if (!v.Any(x => x.Status != null && x.Status != "CANCELED"))
                        {
                            ret = true;
                        }
                    }
                }
            }

            return ret;
        }

        #endregion

        #region Stop and Take Profit (OCO)
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
            if (
                USE_SIMULATED_EXCHANGE ||
                Entry == null ||
                Entry.TradeExchangeStatus != TradeExchangeStatus.ENTERED_ON_EXCHANGE ||
                Stop == null
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

            if (Stop.TradeExchangeStatus == TradeExchangeStatus.ENTERED_OFF_EXCHANGE) // && Stop.Type != TradeType.STOP_WAITING)
            {
                LogTrades.WriteLine($"Did not set Stop on exchange, exchange status was {Stop.TradeExchangeStatus} - you need to add a new stop!");
                Stop.TradeExchangeStatus = TradeExchangeStatus.NO_WARNING;
                SetUpdateRequest(Stop);
                return;
            }

            if (
                Stop.Position == TradePosition.SHORT && Stop.Price < lastPrice || 
                Stop.Position == TradePosition.LONG && Stop.Price > lastPrice
                )
            {
                LogTrades.WriteLine($"Did not set Stop on exchange, Stop price was wrong side of candle data");
                return;
            }

            //check take profit price as well, if it is set
            if ( TakeProfit != null &&
                (TakeProfit.Position == TradePosition.SHORT && TakeProfit.Price > lastPrice ||
                TakeProfit.Position == TradePosition.LONG && TakeProfit.Price < lastPrice)
                )
            {
                LogTrades.WriteLine($"Did not set Take Profit on exchange, Take Profit price was wrong side of candle data");
                return;
            }

            //will add a Take Profit since Binance require an OCO
            if (TakeProfit == null)
            {
                TakeProfit = Entry.Clone();
                TakeProfit.Action = TradeAction.TAKE_PROFIT;
                TakeProfit.Timestamp = GetTimeStamp();
                TakeProfit.Price =
                    Entry.Position == TradePosition.LONG
                        ? lastPrice * (1 + INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100)
                        : lastPrice * (1 - INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100);
                TakeProfit.Result = TakeProfit.Price;   

                AddLineToTrade_synchronised(TakeProfit, DateTime.UtcNow.AddMinutes(-1));
                SetUpdateRequest(TakeProfit);
            }

            Stop.ItemId ??= _Trade.GetNewTradeId();
            TakeProfit.ItemId ??= _Trade.GetNewTradeId();

            if (Stop.Quantity == 0 && Entry != null && Order.ActionValue != 0)
            {
                //this will be called from Order when setting stops automatically
                Stop.Quantity = TakeProfit.Quantity = Entry.Quantity;
                LogTrades.WriteLine($"Stop quantity was zero, Stop not yet set, setting Stop...");
            }

            if (Stop.TradeExchangeStatus == TradeExchangeStatus.ENTERING_ON_EXCHANGE)
            {
                //trade is entering, run synchronously and allow enclosing InitForDataContext() to save changes
                Stop.TradeExecutionStatus = TakeProfit.TradeExecutionStatus = TradeExecutionStatus.EXCHANGE_NOT_SYNCHED;

                if (SendStopToExchange_Synchronised(false)) //not updating, so will not remove OCO first
                {
                    LogConnection.WriteLine("OCO order accepted.");
                    Stop.TradeExchangeStatus = TakeProfit.TradeExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                    Stop.TradeExecutionStatus = TakeProfit.TradeExecutionStatus = TradeExecutionStatus.OK;
                }
                else
                {
                    LogConnection.WriteLine("OCO order NOT accepted.");
                }
            }
            else
            {
                //trade is running, update stop
                //however, we don't want to send updates unless the stop price or take profit price has actually changed
                //can just use the Result property to save the previous price
                if (Stop.Price !=  Stop.Result || TakeProfit.Price != TakeProfit.Result)
                {
                    //can assume this is a new stop if either the stop or take profit has no result, since this will be zero
                    // for a new stop or TP. This would happen if the stop or TP had been force-removed in error
                    bool isUpdating = Stop.Result != 0 && TakeProfit.Result != 0;

                    Stop.Result = Stop.Price;
                    TakeProfit.Result = TakeProfit.Price;

                    Stop.TradeExecutionStatus = TakeProfit.TradeExecutionStatus = TradeExecutionStatus.EXCHANGE_NOT_SYNCHED;

                    LogConnection.WriteLine("Sending OCO...");
                    if (SendStopToExchange_Synchronised(true)) 
                    {
                        LogConnection.WriteLine("OCO order accepted.");
                        Stop.TradeExchangeStatus = TakeProfit.TradeExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                        Stop.TradeExecutionStatus = TakeProfit.TradeExecutionStatus = TradeExecutionStatus.OK;

                    }
                    else
                    {
                        LogConnection.WriteLine("OCO order NOT accepted.");
                    }
                }
            }

            SetUpdateRequest(Stop);
            SetUpdateRequest(TakeProfit);

        }

        //could not get a stop order to work, there is no API call for this specifically and a trade set as a stop just won't be accepted
        protected override bool SendStopToExchange_Synchronised(bool isUpdating)
        {
            if (Stop == null || Stop.Quantity == 0 || TakeProfit == null) //can be zero if attempting to move a closed stop
            {
                return false; //happens with locally placed order
            }

            try
            {
                //you cannot add a new OCO order if there is already one on the exchange, so remove it first if this is an update
                //unfortunately this also means you can't have a (safety) Floor instead

                if (!isUpdating || RemoveStopFromExchange_Synchronised())
                { 
                    //it is vital that the precision is correct, in case this has been missed in the UI somewhere do it here as well
                    var stopPrice = GetCorrectPrecisionForPrice(Stop.Price);
                    var takeProfitPrice = GetCorrectPrecisionForPrice(TakeProfit.Price);
                    var orderStop = SendOCO(Entry.Quantity, stopPrice, Stop.ItemId, takeProfitPrice, TakeProfit.ItemId);

                    if (orderStop == null)
                    {
                        return false; //log output and error checking handled by ExecuteOCO
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
            if (Stop.OrderListId == null)
            {
                //if a stop was removed in error, this would be a replacement but the OrderListID will be lost
                CancelOpenOrders();
            }
            else
            {
                var data = new Dictionary<string, string>
                {
                    {SYMBOL, symbolName },
                    {LIST_CLIENT_ORDER_ID, Stop.OrderListId}
                };
                try
                {
                    var orderData = Deserialize<NewOrderData>(binanceAPI.CancelOrderListAsync(data).Result);

                    if (orderData != null)
                    {
                        if (!string.IsNullOrEmpty(orderData.Status))
                        {
                            WriteDeliniatedMessage($"Stop cancel order was rejected: {orderData.Status}, stop not updated / replaced");
                            return false;
                        }
                    }
                }
                catch (Exception e)
                {
                    LogTrades.WriteLine($"Error cancelling order list: {e.Message}");
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
                if (Entry != null && Entry.TradeExecutionStatus == TradeExecutionStatus.OK && Entry.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                {
                    //this is tricky with Binance, because there is no live running trade - only account balances
                    //so if there is a base asset balance, the trade is still running. Otherwise, close it entirely.
                    Entry.TradeExecutionStatus = TradeExecutionStatus.UPDATE_LIVE_STATE;

                    if (IsTradeClosed())
                    {
                        //not accurate, there could be a minute elapsed since the stop triggered on the exchange
                        //BUT the app should have closed the trade locally by then.
                        //alternative is to interrogate the stop and TP orders to see which has triggered

                        //just mark the trade as closed here
                        CloseTradeOnSystem(GetLastPrice());
                    }
                    else
                    {
                        //in the event there is a pending entry or stop operation on the exchange, do not do anything. This is unlikely but it would mess things up.
                        if (Stop != null && Stop.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE && Stop.TradeExecutionStatus != TradeExecutionStatus.EXCHANGE_NOT_SYNCHED)
                        {
                            var orders = GetOpenOrders();
    
                            if (orders != null && !orders.Any(x => x.ClientOrderId == Stop.ItemId))
                            {
                                //mark the stop as not synchronised
                                Stop.TradeExecutionStatus = TradeExecutionStatus.EXCHANGE_NOT_SYNCHED;
                                SetUpdateRequest(Stop);
                                LogTrades.WriteLine("No locked balance on exchange, assume stop cancelled, check exchange!!");
                            }
                        }
                    }
                }
                
            });
        }
        #endregion

        #region Utility methods  

        public override double AdjustResultForFees(double result)
        {
            return base.AdjustResultForFees(result);
        }

        protected override double GetValueForSymbol(string symbolName)
        {
            if (USE_TESTNET)
            {
                //this can run straight away after starting app
                var priceData = Deserialize<QuoteData>(binanceAPI.TickerPriceAsync(symbolName).Result);
                return priceData.Price;
            }

            return base.GetValueForSymbol(symbolName);
        }

        bool IsTradeClosed()
        {
            var exchangeInfo = GetExchangeInfo();
            if (exchangeInfo != null)
            {
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
            }
            return false;
        }

        //Get the correct precision for a trade and return the amended amount, this is for the base asset (USDT)
        //Note, the symbol will be BTCUSDT, all the symbols are trading pairs
        //All trades are in base asset, both sell and buy (e.g. BTC)
        protected override double GetBalanceToTrade(double freeBalance, double lastQuoteValue)
        {
            var balanceToTrade = RESTRICTED_TRADE_VALUE > 0 ? RESTRICTED_TRADE_VALUE : freeBalance * 0.95; //this will just be a 95% order for now
            if (RESTRICTED_TRADE_VALUE > 0) //get the minimum tradeable amount of the quote asset expressed in base asset (i.e. BTC -> USDT)
            {
                var _minValue = 0.0001; //no idea where this comes from, not in exchange info etc. but it is the minimum trade amount BTC
                _minValue *= 1.1; //to be sure it will enter, although it does on 0.0001 anyway
                balanceToTrade = _minValue * lastQuoteValue;
                if (RESTRICTED_TRADE_VALUE > balanceToTrade)
                {
                    balanceToTrade = RESTRICTED_TRADE_VALUE;
                }
            }

            var fiatAmount = balanceToTrade / lastQuoteValue;

            return GetCorrectPrecisionForTrade(fiatAmount);       
        }

        //Get the correct precision for a trade and return the amended amount, this is for the base asset
        //Note, the symbol will be BTCUSDT, all the symbols are trading pairs
        //All trades are in base asset, both sell and buy (e.g. BTC)
        protected override double GetCorrectPrecisionForTrade(double balanceToTrade)
        {
            if (balanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
            {

                var exchangeInfo = GetExchangeInfo();

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
        protected double GetCorrectPrecisionForPrice(double balanceToTrade)
        {
            if (balanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
            {
                var exchangeInfo = GetExchangeInfo();

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

        /***************************************/
        //retain some BNB to reduce trading fees
        /***************************************/
        protected override double AdjustCoinBalanceForFees(string baseAsset, double coinBalance)
        {
            if (baseAsset == "BNB" && RETAINED_BNB_PERCENTAGE > 0)
            {
                coinBalance *= (1 - RETAINED_BNB_PERCENTAGE / 100);
                LogTrades.WriteLine($"Retaining {RETAINED_BNB_PERCENTAGE}% of BNB");
                return GetCorrectPrecisionForTrade(coinBalance);
            }
            else
            {
                return coinBalance;
            }
        }
        void WriteDeliniatedMessage(string msg) => LogTrades.WriteLine($"\n**************************************\n{msg}\n**************************************");

        #endregion
    } 
}