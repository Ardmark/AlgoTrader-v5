using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Interfaces;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static AlgoTrader.Shared.ExchangeConnection.ExchangeDataProcessorBinance;

namespace AlgoTrader.Shared
{
    abstract public class TradeEngineBinance : TradeEngine
    {
        #region Strings 
        public const string BUY = "BUY";
        public const string SELL = "SELL";
        public const string SYMBOL = "symbol";
        public const string SIDE = "side";
        public const string ORDER_TYPE = "type"; //LIMIT, MARKET, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER
        public const string MARKET = "MARKET";
        public const string QUANTITY = "quantity";
        public const string CLIENT_ORDER_ID_NEW = "newClientOrderId";
        public const string RESPONSE_TYPE = "newOrderRespType";
        public const string FULL = "FULL";
        public const string LIST_CLIENT_ORDER_ID = "listClientOrderId";

        /* unused strings
        public const string BTCUSDT = "BTCUSDT";
        public const string CLIENT_ORDER_ID_ORIGINAL = "origClientOrderId";
        public const string ORIG_CLIENT_ORDER_ID = "origClientOrderId";
        public const string EXCHANGE_ORDER_ID = "orderId";
        public const string QUOTE_ORDER_QTY = "quoteOrderQty";
        public const string ORIG_QUANTITY = "origQty";
        public const string ICEBERG_QUANTITY = "icebergQty";
        public const string TIME_IN_FORCE = "timeInForce";
        public const string GOOD_TILL_CANCELLED = "GTC";
        public const string FILL_OR_KILL = "FOK";
        public const string IMMEDIATE_OR_CANCEL = "IOC";
        public const string TIMEOUT_MS = "recvWindow"; //this defaults to 5000ms. It is recommended to use this value or less, cannot go beyond 60000
        public const string LIMIT = "LIMIT";
        public const string STOP_LOSS_LIMIT = "STOP_LOSS_LIMIT";
        public const string LIMIT_MAKER = "LIMIT_MAKER";
        public const string TAKE_PROFIT_LIMIT = "TAKE_PROFIT_LIMIT";
        public const string ABOVE_TIMEINFORCE = "aboveTimeInForce";
        public const string BELOW_PRICE = "belowPrice";
        public const string BELOW_TIMEINFORCE = "belowTimeInForce";
        public const string ACK = "ACK";
        public const string RESULT = "RESULT";
        public const string CANCEL_REPLACE_MODE = "cancelReplaceMode";
        public const string ALLOW_FAILURE = "ALLOW_FAILURE";
        public const string STOP_ON_FAILURE = "STOP_ON_FAILURE";
        public const string ABOVE_PRICE = "abovePrice";
        */


        #endregion
        
        #region Enums
        public enum OrderTypes
        {
            [Display(Name = "LIMIT")]
            LIMIT,
            [Display(Name = "MARKET")]
            MARKET,
            [Display(Name = "STOP_LOSS")]
            STOP_LOSS,
            [Display(Name = "STOP_LOSS_LIMIT")]
            STOP_LOSS_LIMIT,
            [Display(Name = "TAKE_PROFIT")]
            TAKE_PROFIT,
            [Display(Name = "TAKE_PROFIT_LIMIT")]
            TAKE_PROFIT_LIMIT,
            [Display(Name = "LIMIT_MAKER")]
            LIMIT_MAKER
        }


        #endregion
        
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
                public bool ApplyToMarket { get; set; }
                public long MaxNumOrders { get; set; }
                public long MaxNumAlgoOrders { get; set; }
                public long AvgPriceMins { get; set; }
                public double MinQty { get; set; }
                public double MaxQty { get; set; }
                public double StepSize { get; set; }
                public double MinNotional { get; set; }
                public double TickSize { get; set; }
                public string FilterType { get; set; }
            }
            public class SymbolData
            {
                public List<string> OrderTypes { get; set; }
                public List<string> permissions { get; set; }
                public List<ExchangeFilterData> Filters { get; set; }
                public bool OcoAllowed { get; set; }
                public bool IsSpotTradingAllowed { get; set; }
                public bool IsMarginTradingAllowed { get; set; }
                public string Symbol { get; set; }
                public string Status { get; set; }
                public string QuoteAsset { get; set; } //this would be BTC for example, in ETHBTC
                public string BaseAsset { get; set; } //this would be ETH for example in ETHBTC
                public long BaseAssetPrecision { get; set; }
                public long QuotePrecision { get; set; } // will be removed in future api versions (v4+)
                public long QuoteAssetPrecision { get; set; }
                public long BaseCommissionPrecision { get; set; }
                public long QuoteCommissionPrecision { get; set; }

                #region Unused properties
                //public bool QuoteOrderQtyMarketAllowed { get; set; }
                //public bool IcebergAllowed { get; set; }
                #endregion
            }

            public string TimeZone { get; set; }
            public long ServerTime { get; set; }
            public List<RateLimitData> RateLimits { get; set; }
            public List<ExchangeFilterData> ExchangeFilters { get; set; }
            public List<SymbolData> Symbols { get; set; }
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
            public List<BalanceData> Balances { get; set; }
            public List<string> Permissions { get; set; }
            public bool CanTrade { get; set; }
            public bool MakerCommission { get; set; }
            public string AccountType { get; set; }

            //unused here so not filled
            public bool TakerCommission { get; set; }
            public bool CanWidthdraw { get; set; }
            public bool CanDeposit { get; set; }
            public long UpdateTime { get; set; } //won't convert to DateTime from javascript format

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
            public long Time { get; set; }
            public long UpdateTime { get; set; }
            public long TransactTime { get; set; }
            public long OrigClientOrderId { get; set; } //this is for a cancel order, not sure if the OrderId prop. gets populated or this one
            public double Price { get; set; }
            public double OrigQty { get; set; }
            public double ExecutedQty { get; set; }
            public double CummulativeQuoteQty { get; set; }
            public double StopPrice { get; set; }
            public double OrigQuoteOrderQty { get; set; }
            public double IcebergQty { get; set; }
            public string Status { get; set; } //Order: NEW, PARTIALLY_FILLED, FILLED, CANCELED, PENDING_CANCEL, REJECTED, EXPIRED (PENDING_CANCEL is currenty Unused) - there are some additional enum strings for OCO's
            public string TimeInForce { get; set; } //e.g. GTC
            public string Type { get; set; } //LIMIT, MARKET, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER
            public string Side { get; set; } //BUY or SELL
            public string ListClientOrderId { get; set; } //for stops
            public string AboveClientOrderId { get; set; } //for stops
            public string BelowClientOrderId { get; set; } //for stops
            public bool IsWorking { get; set; }

            //Margin trading
            public string SideEffectType { get; set; }
            public string AutoRepayAtCancel { get; set; }
            public bool IsIsolated { get; set; }

            //properties to do conversions, etc.
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
                public string ClientOrderId { get; set; }
                public long OrderId { get; set; }
            }

            public List<OrderReportsData> OrderReports { get; set; }
            public List<OrderData> Orders { get; set; }
            public long OrderListId { get; set; }
            public string ListClientOrderId { get; set; } 
            public string ContingencyType { get; set; } //e.g. "OCO"
            public string ListStatusType { get; set; } //e.g. "EXEC_STARTED"
            public string ListOrderStatus { get; set; } //e.g. "EXECUTING"
        }

        //order lists returns JSON with an anonymous array so can't deserialize it to a specific type
        public List<OrderData> DeserializeToList(JsonArray jsonArray)
        {
            List<OrderData> orders = [];
            foreach (var v in jsonArray)
            {
                orders.Add(JsonConvert.DeserializeObject<OrderData>(v.ToString()));
            }
            return orders;
        }

        //keep as a note on how to do this as a generic return type
        /*public List<T> DeserializeToList<T>(JsonArray jsonArray)
        {
            List<T> orders = [];
            foreach (var v in jsonArray)
            {
                orders.Add(JsonConvert.DeserializeObject<T>(v.ToString()));
            }
            return orders;
        }
        */


        protected List<OrderData> GetOpenOrders()
        {
            try 
            { 
                return DeserializeToList(binanceAPI.OpenOrdersAsync(GetOpenOrderData()).Result.AsArray());
            }
            catch (Exception ex)
            {
                LogTrades.WriteLine($"Failed to deserialize GetOpenOrders(...): {ex}");
                return null;
            }
        }

        #endregion

        #endregion

        #region Fields
        protected IBinanceAPI binanceAPI;
        protected DateTime lastExchangeInfoRequest = new DateTime();
        protected ExchangeInfoData exchangeInfoData = null;
        #endregion

        #region Init
        public TradeEngineBinance(string symbolName, int timeInterval) : base(symbolName, timeInterval) {}
        #endregion

        #region Spot and Margin-specific abstract methods
        protected abstract Dictionary<string, string> GetOrderData(TradePosition position, double orderQuantity, Side side, string orderId);
        protected abstract Dictionary<string, string> GetOpenOrderData();
        protected abstract Dictionary<string, string> GetCancelOrderData();
        protected abstract Dictionary<string, string> GetOCOData(TradePosition position, double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId);
        protected abstract Dictionary<string, string> GetCancelOCOData(string orderListId);
        #endregion

        #region Send REST requests

        #region Account info
        internal AccountData GetAccountInfo()
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
        #endregion

        #region Exchange info
        protected ExchangeInfoData GetExchangeInfo()
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

        protected override bool SendOrder(TradePosition position, double orderQuantity, string orderId, Side side, out double averagePrice, out double executedQuantity)
        {
            averagePrice = executedQuantity = 0;
            var data = GetOrderData(position, orderQuantity, side, orderId);
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


        public NewOCOData SendOCO(TradePosition position, double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId)
        {

            var data = GetOCOData(position, orderQuantity, belowPrice, stopId, abovePrice, TPId);
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

        #endregion

        #region Price info

        protected override double GetLastPrice()
        {
            //difference between currentDataItem.Close and TickerPriceAsync(...) is 0 to 0.02%

            if (currentDataItem != null)
            {
                return currentDataItem.Close;
            }

            //this takes about a second to execute so it is better not to do this unless necessary
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

        protected override bool InitForEntryOrClose() 
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
            //so will return true if:
            // 1) there are no open orders
            // 2) orders have been cancelled and there are no orders returning status of CANCELED
            // 3) CancelOpenOrdersAsync returns a list of length zero, indicating no failure

            var openOrders = GetOpenOrders();
            if (openOrders != null && openOrders.Count == 0)
            {
                //if there are no open orders to cancel this will throw an exception
                return true;
            }
            else
            {
                try
                {
                    LogTrades.WriteLine("Cancelling open orders...");
                    var v = DeserializeToList(binanceAPI.CancelOpenOrdersAsync(GetCancelOrderData()).Result.AsArray());
                    return v != null && (v.Count == 0 || !v.Any(x => x.Status != null && x.Status != "CANCELED"));
                }
                catch (Exception ex)
                {
                    LogTrades.WriteLine($"Failed to deserialize CancelAllOpenOrders(...): {ex}");
                    return false;
                }
            }
        }

        #endregion

        #region Stop and Take Profit (OCO)        

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
                    var stopPrice = GetCorrectPrecisionForQuoteAssetTrade(Stop.Price);
                    var takeProfitPrice = GetCorrectPrecisionForQuoteAssetTrade(TakeProfit.Price);
                    var orderStop = SendOCO(Entry.Position, Entry.Quantity, stopPrice, Stop.ItemId, takeProfitPrice, TakeProfit.ItemId);

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
                var data = GetCancelOCOData(Stop.OrderListId);
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
                if (Entry != null && Entry.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                {
                    //this is tricky with Binance, because there is no live running trade - only account balances
                    //so if there is a base asset balance, the trade is still running. Otherwise, close it entirely.
                    Entry.TradeExecutionStatus = TradeExecutionStatus.OK;
                    SetUpdateRequest(Entry);

                    if (IsTradeClosed(Entry.Position))
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
                            }
                        }
                        //Stop.TradeExecutionStatus = TradeExecutionStatus.OK;
                        //SetUpdateRequest(Stop);
                    }
                }
                
            });
        }
        #endregion

        #region Utility methods  

        protected override bool IsTradeClosed(TradePosition position)
        {
            #region Get lot size (this is incorrect, it returns 0.00001 when the min.lot size is 0.0001) but keep for reference
            //var exchangeInfo = GetExchangeInfo();
            //if (exchangeInfo != null)
            //{
            //    var __baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
            //    if (__baseAsset != null)
            //    {
            //        var __symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
            //        if (__symbol != null)
            //        {
            //            var lotSize = __symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
            //            if (lotSize != null)
            //            {
            //                var accountData = GetAccountInfo();
            //                var _minValue = lotSize.MinQty * 2; //it should always use the lot size notional value, same as MinQty, to calculate trade quantity so would only leave < MinQty but double this to be safe
            //                var _baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault();
            //                var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
            //                return _baseAssetBalanceLocked < _minValue && _baseAssetBalanceFree < _minValue; //i.e there is no BTC
            //            }
            //        }
            //    }
            //}
            #endregion


            var __baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
            if (__baseAsset != null)
            {
                var accountData = GetAccountInfo();
                var _minValue = GetMinLotSize(); 
                var _baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault();
                var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
                return _baseAssetBalanceLocked < _minValue && _baseAssetBalanceFree < _minValue; //i.e there is no BTC
            }
                        
            return false;
        }

        #region Get minimum lot size and price filter

        #region Unused - get lot size (returns 0.00001 when the minimum is actually 0.0001) and price filter from exchange info
        //var exchangeInfo = GetExchangeInfo();
        //if (exchangeInfo != null)
        //{
        //    var __symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
        //    if (__symbol != null)
        //    {
        //        var lotSize = __symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
        //        if (lotSize != null)
        //        {
        //            _minValueBase = lotSize.MinQty;
        //        }
        //        var priceFilter = __symbol.Filters.Where(x => x.FilterType == "PRICE_FILTER").FirstOrDefault();
        //        if (priceFilter != null)
        //        {
        //            _minValueQuote = priceFilter.TickSize;
        //        }
        //    }
        //}
        #endregion

        const double minLotSize = 0.0001;
        protected double GetMinLotSize()
        {
            return minLotSize;
        }
        protected double GetMinPriceFilter()
        {
            return minLotSize * GetLastPrice();
        }
        protected double GetRestrictedBalanceBase(double balance)
        {
            return RESTRICT_TRADE_VALUE ? GetMinLotSize() : balance;
        }
        protected double GetRestrictedBalanceQuote(double balance)
        {
            return RESTRICT_TRADE_VALUE ? GetMinPriceFilter() : balance;
        }

        #endregion

        //Get the correct precision for a base asset trade and return the amended amount, this is for the base asset
        //Note, the symbol will be BTCUSDT, all the symbols are trading pairs
        //NOTE, at time of trade execution, all trades are in base asset, both sell and buy (e.g. BTC)
        protected override double GetCorrectPrecisionForBaseAssetTrade(double coinBalanceToTrade)
        {
            if (coinBalanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
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
                    var ret = (int)(coinBalanceToTrade / stepSize) * stepSize;
                    var p = (int)Math.Log10(stepSize);
                    ret = Math.Round(ret, -p);
                    return ret;
                }
            }
            return -1;
        }

        //as above, but this is for the quote asset (USDT)
        //use this to set stops at the correct price, although this may not be necessary
        protected double GetCorrectPrecisionForQuoteAssetTrade(double fiatBalanceToTrade)
        {
            if (fiatBalanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
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
                        var ret = (int)(fiatBalanceToTrade / stepSize) * stepSize;
                        var p = (int)Math.Log10(stepSize);
                        ret = Math.Round(ret, -p);
                        return ret;
                    }

                }
                //just in case, just round it down
                return Math.Round(fiatBalanceToTrade, 2);
            }
            return -1;
        }

        void WriteDeliniatedMessage(string msg) => LogTrades.WriteLine($"\n**************************************\n{msg}\n**************************************");

        #endregion
    } 
}