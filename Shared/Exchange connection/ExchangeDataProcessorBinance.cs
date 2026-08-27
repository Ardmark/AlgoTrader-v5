using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Timers;
using System.Windows.Threading;
using static AlgoTrader.Shared.BinanceAPI;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace AlgoTrader.Shared.ExchangeConnection
{
    public partial class ExchangeDataProcessorBinance : ExchangeDataProcessor
    {
        /*
         * Stops and take profits:
         * To run on the exchange, the initial order is placed, then subsequent orders are first cancelled then added again on the exchange.
         * This requires a couple of flags (pendingLiveEntryStop, pendingLiveEntryTP) and the logic to respond to the close order acknowlegements.
         * This has been tested on the exchange simulation and it works. 
         * This also assumes that the exchange stop will execute before the system one, then the system will execute its stop and send a close order
         *  which will be rejected. The system will then synch the pending close against the exchange in the OnQueryOrderDataReceived() event,
         *  which has not been tested at this time.
         *  6/4/21.
         * */
        #region Binance data classes (JSON deserializes into these directly)


        /*can just use dynamic object and access these properties by name, or a class with specific property definitions */
        internal class BinanceCaptureData
        {
            //there are 3 properties: eventTime; symbol; eventType (of which, 'kline' is only of interest - anything else, this will not be set but only kline is being sent anyway from the connector)
            //here we are adding an EventTimestamp to automatically convert the node format to a DateTime object
            public string Symbol { get; set; }
            public DateTime EventTimestamp { get; set; }
            public Kline Kline { get; set; } //contains startTime,     endTime,     symbol,     interval,     firstTradeId,     lastTradeId,   open,     close,     high,     low,     volume,     trades,     final,     quoteVolume,     volumeActive,     quoteVolumeActive,     ignored,
            public string Message { get; set; }
            public string Error { get; set; }
            public string ExchangeInfo { get; set; }
            public string Account { get; set; }
            public string QuotePrice { get; set; }
            public string QueryOrder { get; set; }
            public string AllOrders { get; set; }
            public string NewOrder { get; set; }
            public string CancelOrder { get; set; }
            public string OpenOrders { get; set; }
            public string MyTrades { get; set; }
            public string NewOCO { get; set; }

            private long eventTime;
            public long EventTime
            {
                get
                {
                    return eventTime;
                }
                set
                {
                    eventTime = value;
                    EventTimestamp = TimeStampFromBinance(value);
                }
            }
        }

        internal class Kline
        {
            public DateTime EndTimestamp { get; set; }
            public double Close { get; set; }
            public double Open { get; set; }
            public double High { get; set; }
            public double Low { get; set; }
            public bool Final { get; set; }

            //only interested in the time, close value and whether this is the end of the minute (Final)
            private long endTime;
            public long EndTime
            {
                get
                {
                    return endTime;
                }
                set
                {
                    endTime = value;
                    //note, value here is always at current minute and 59 seconds, so the seconds cannot be used
                    EndTimestamp = TimeStampFromBinance(value);
                }
            }
        }

        const long TICKS_TO_1970 = 621355968000000000;
        public static DateTime TimeStampFromBinance(long ms)
        {
            //var dt = new DateTime(1970, 1, 1);
            //var ticks = dt.Ticks;
            //node returns dates in milliseconds since jan 1st 1970
            //DateTime uses ticks since an earlier date, and there are 10000 ticks per ms
            return new DateTime(ms * 10000 + TICKS_TO_1970);
        }

        public static long TimeStampToBinance(DateTime timeStamp)
        {
            return (timeStamp.Ticks - TICKS_TO_1970) / 10000;

        }
        #endregion

        #region Countdown timer
        DispatcherTimer countdownTimer;
        DateTime lastCountdownTime;
        bool countDownStarted = false;
        #endregion

        #region Fields

        [JsonExtensionData] //or this won't be serialized correctly
        public List<string> AdditionalData { get; set; }

        readonly List<KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>> callBacks = new List<KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>>();

        #endregion

        #region Init
        public ExchangeDataProcessorBinance() : base(ExchangeId.BINANCE)
        {
            connectorPath = string.Format(@"""{0}""", System.IO.Path.Combine(projectDirectory, "Binance connectors", "csharpconnector", "connector.js"));    //note the escaped " to allow spaces

            countdownTimer = new DispatcherTimer(new TimeSpan(0, 0, 1), DispatcherPriority.Normal, (Object sender, EventArgs e) =>
            {
                var countdownSeconds = DateTime.UtcNow.Subtract(lastCountdownTime).TotalSeconds;
                lastCountdownTime = DateTime.UtcNow;
                MainObj.SetCurrentSeconds(countdownSeconds); //not causing exception
            }, Dispatcher.CurrentDispatcher);
            countdownTimer.Stop();
            lastCountdownTime = DateTime.UtcNow;

            symbolNamesToUpdate = SymbolNames.GetSymbolNames().ToList();
        }

        protected override bool OpenConnection()
        {
            var ret = base.OpenConnection();
            if (ret == true)
            {
                //this just pumps a message out to the connector 
                if (process.StandardInput != null)
                {
                    //set up node connector with all the symbol names to retrieve data for
                    AdditionalData = new List<string>();
                    foreach (var symbolName in SymbolNames.GetSymbolNames())
                    {
                        //symbols which don't have a data feed are bracketed, but brackets in the symbol name break the connection process with Binance,
                        // so must exclude those from the request
                        if (!symbolName.Contains("("))
                        {
                            //need a name-value pair, so can just use the symbol name for both
                            AdditionalData.Add(symbolName);
                        }
                    }

                    //todo - need to get all symbols, etc.
                    //SendCommandToExchange("SetSymbols", AdditionalData);


                    //var s1 = JsonConvert.SerializeObject(new
                    //{
                    //    Operation = "SetSymbols",
                    //    Data = AdditionalData
                    //});
                    //
                    //process.StandardInput.WriteLineAsync(s1);

                    //var s2 = JsonConvert.SerializeObject(new
                    //{
                    //    Operation = "ExchangeInfo"
                    //});
                    //process.StandardInput.WriteLineAsync(s2);

                    //cannot use Console.WriteLine, as that just writes to the debug window
                    //Console.SetOut(myStreamWriter); - could now use Console.WriteLine instead
                }
            }

            return ret;
        }

        #endregion


        #region Send data via node connector (unused)
        /*
        internal override void AddRequest(ExchangeRequest exchangeRequest, EventHandler<ExchangeEventArgs> eventHandler, params (dynamic, dynamic)[] _data)
        {
            Dictionary<string, string> data = null;
            if (_data != null && _data.Count() > 0)
            {
                data = new Dictionary<string, string>();
                foreach (var v in _data)
                {
                    //map the api request string name to the actual string required by the Binance API
                    string p;
                    ApiParamNames name = (ApiParamNames)v.Item1;
                    switch (name)
                    {
                        case ApiParamNames.SYMBOL:
                            p = "symbol";
                            break;
                        case ApiParamNames.TRADE_ID_ORIGINAL:
                            p = "origClientOrderId";
                            break;
                        case ApiParamNames.TRADE_ID_NEW:
                            p = "newClientOrderId";
                            break;
                        case ApiParamNames.EXCHANGE_ORDER_ID:
                            p = "orderId";
                            break;
                        case ApiParamNames.SIDE:
                            p = "side";
                            break;
                        case ApiParamNames.ORDER_TYPE:
                            p = "type";
                            break;
                        case ApiParamNames.QUOTE_ORDER_QTY:
                            p = "quoteOrderQty";
                            break;
                        case ApiParamNames.QUANTITY:
                            p = "quantity";
                            break;
                        case ApiParamNames.ICEBERG_QUANTITY:
                            p = "icebergQty";
                            break;
                        case ApiParamNames.PRICE:
                            p = "price";
                            break;
                        case ApiParamNames.STOP_PRICE:
                            p = "stopPrice";
                            break;
                        case ApiParamNames.RESPONSE_TYPE:
                            p = "newOrderRespType";
                            break;
                        case ApiParamNames.TIME_IN_FORCE:
                            p = "timeInForce";
                            break;
                        case ApiParamNames.TIMEOUT_MS:
                            p = "recvWindow";
                            break;
                        case ApiParamNames.BUY:
                            p = "BUY";
                            break;
                        case ApiParamNames.SELL:
                            p = "SELL";
                            break;
                        case ApiParamNames.LIMIT:
                            p = "LIMIT";
                            break;
                        case ApiParamNames.MARKET:
                            p = "MARKET";
                            break;
                        case ApiParamNames.STOP_LOSS:
                            p = "STOP_LOSS";
                            break;
                        case ApiParamNames.STOP_LOSS_LIMIT:
                            p = "STOP_LOSS_LIMIT";
                            break;
                        case ApiParamNames.TAKE_PROFIT_LIMIT:
                            p = "TAKE_PROFIT_LIMIT";
                            break;
                        case ApiParamNames.ACK:
                            p = "ACK";
                            break;
                        case ApiParamNames.RESULT:
                            p = "RESULT";
                            break;
                        case ApiParamNames.FULL:
                            p = "FULL";
                            break;

                        case ApiParamNames.ABOVE_TYPE:
                            p = "aboveType";
                            break;
                        case ApiParamNames.ABOVE_PRICE:
                            p = "abovePrice";
                            break;
                        case ApiParamNames.ABOVE_STOP_PRICE:
                            p = "aboveStopPrice";
                            break;
                        case ApiParamNames.ABOVE_TIMEINFORCE:
                            p = "aboveTimeInForce";
                            break;
                        case ApiParamNames.BELOW_TYPE:
                            p = "belowType";
                            break;
                        case ApiParamNames.BELOW_PRICE:
                            p = "belowPrice";
                            break;
                        case ApiParamNames.BELOW_STOP_PRICE:
                            p = "belowStopPrice";
                            break;
                        case ApiParamNames.BELOW_TIMEINFORCE:
                            p = "belowTimeInForce";
                            break;
                        case ApiParamNames.ORIG_CLIENT_ORDER_ID:
                            p = "origClientOrderId";
                            break;


                        default:
                            Logging.LogTrades.WriteLine($"could not map {name} to api parameter string");
                            return;


                    }
                    //data.Add(new KeyValuePair<string, string>(v.Item1 as string, v.Item2 as string));
                    if (v.Item2 != null)
                    {
                        data.Add(p, v.Item2.ToString());
                    }
                    else
                    {
                        Logging.LogTrades.WriteLine($"missing data for ExchangeCommand {exchangeRequest}: {p} was not found");
                    }
                }
            }

            switch (exchangeRequest)
            {
                case ExchangeRequest.EXCHANGE_INFO:
                    //this is symbol-agnostic so the data must be sent back to the TradeEngine which requested it via an event handler
                    //there would in principal be only one, although 2 could try to enter more or less simultaneously in principal
                    if (!callBacks.Where(x => x.Key == exchangeRequest).Any())
                    {
                        callBacks.Add(new KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>(exchangeRequest, eventHandler));
                    }
                    SendCommand("ExchangeInfo");
                    break;

                case ExchangeRequest.ACCOUNT:
                    //ditto
                    if (!callBacks.Where(x => x.Key == exchangeRequest).Any())
                    {
                        callBacks.Add(new KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>(exchangeRequest, eventHandler));
                    }
                    SendCommand("Account");
                    break;

                case ExchangeRequest.QUOTE_PRICE:
                    if (!callBacks.Where(x => x.Key == exchangeRequest).Any())
                    {
                        callBacks.Add(new KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>(exchangeRequest, eventHandler));
                    }
                    SendCommand("QuotePrice", data);
                    break;

                case ExchangeRequest.ALL_ORDERS:
                    //uses symbol name
                    SendCommand("AllOrders", data);

                    //Required parameters:
                    //symbol

                    break;

                case ExchangeRequest.QUERY_ORDER:
                    //
                    // Required parameters:
                    // symbol
                    // 
                    // Optional parameters:
                    // orderId (long)
                    // origClientOrderId (string)
                    // 
                    SendCommand("QueryOrder", data);
                    break;

                case ExchangeRequest.NEW_ORDER:

                    //close - this is a new order
                    //
                    // Required parameters:
                    // symbol
                    // side
                    // type
                    // timestamp
                    // quoteOrderQty (for market orders, can send quantity instead)
                    // quantity  (for stop loss)
                    // stopPrice (for stop loss)
                    // 
                    // Optional parameters:
                    // newClientOrderId (string) (must be unique amongst open orders. If already in use by an open order, new order will be rejected. If not supplied, will be generated)
                    // 
                    SendCommand("NewOrder", data);
                    break;

                case ExchangeRequest.NEW_OCO:
                    SendCommand("NewOCO", data);
                    break;


                case ExchangeRequest.OPEN_ORDERS:
                    //this should use a symbol name or is weight 40, so will not by symbol-agnostic
                    SendCommand("OpenOrders", data);
                    break;

                case ExchangeRequest.MY_TRADES:
                    //uses symbol name
                    SendCommand("MyTrades", data);
                    break;

                case ExchangeRequest.CANCEL_ORDER:
                    SendCommand("CancelOrder", data);
                    break;
            }
        }

        void SendCommand(string command, dynamic data = null)
        {
            if (!TradeEngine.USE_SIMULATED_EXCHANGE)
            {
                SendCommandToExchange(command, data);
            }
            else
            {
                //bypass the exchange for the actual order, no need to place it for live testing, just to get the price, quantity etc.
                SendCommandSimulated(command, data);
            }
        }

        //send command to the connector via StandardInput


        //send command to the connector via StandardInput
        public override void SendCommandToExchange(string command, dynamic data = null)
        {

            string s;
            if (data != null)
            {
                s = JsonConvert.SerializeObject(new
                {
                    Operation = command,
                    Data = data //will be serialized correctly but needs to be passed in as a public property with the [JsonExtensionData] attribute
                });
            }
            else
            {
                s = JsonConvert.SerializeObject(new
                {
                    Operation = command
                });
            }
            try
            {
                process.StandardInput.WriteLine(s);
            }
#pragma warning disable CS0168
            catch (Exception e)
            {

            }
        }

        */
        #endregion

        #region Receive data fron node connector (Kline only)
        List<DateTime> errors = [];
        protected override void CaptureOutput(object sender, DataReceivedEventArgs e)
        {
            void HandleError()
            {
                const int MAX_ERRORS_PER_MINUTE = 3;
                if (errors.Count >= MAX_ERRORS_PER_MINUTE - 1) //2 errors so this one will make 3 in the list
                {
                    LogConnection.WriteLine($"{errors.Count + 1} errors in one minute, restarting connection...");
                    errors.Clear();
                    OpenConnection();
                }
                else
                {
                    errors.Add(DateTime.UtcNow);
                }
            }

            if (!MainObj.initComplete)
            {
                lastProcessTimestamp = DateTime.UtcNow; //so no reconnection
                return;
            }

            if (e == null || e.Data == null) //seems to happen after breaking for some time
            {
                return;
            }

            try
            {

                var captureData = JsonConvert.DeserializeObject<BinanceCaptureData>(e.Data);
                //if (captureData.Message != null) Debug.WriteLine(captureData.Message);

                #region Error
                if (errors.Any())
                {
                    var prevMinute = DateTime.UtcNow.AddMinutes(-1);
                    errors.RemoveAll((DateTime dt) => { return dt < prevMinute; });
                }
                if (captureData.Error != null)
                {
                    LogConnection.WriteLine($"Binance capture data error at {DateTime.UtcNow:G}: {captureData.Message}");

                    HandleError();

                    return;
                }
                #endregion

                //From websocket (user data stream)
                #region Candle data
                if (captureData.Kline != null)
                {
                    //Process data here 

                    lastProcessTimestamp = DateTime.UtcNow;

                    lock (thisLock) //could use SemaphoreSlim class instead
                    {
                        //this is way more simple than Bitmex, where the data is streamed in blocks with repeated entries each time, so that needs a lot of sorting to get the latest data
                        //here, we just need to save data when Final == true, otherwise just pass the close value back to the app
                        try
                        {
                            var currentSymbolDataItem = GetCurrentSymbolDataItem(captureData.Symbol);

                            //need a new object or the db will hold a reference and that data will be changed next update
                            var item = new _SymbolDataItem()
                            {
                                Close = captureData.Kline.Close,
                                Open = captureData.Kline.Open,
                                High = captureData.Kline.High,
                                Low = captureData.Kline.Low,
                                Timestamp = captureData.Kline.EndTimestamp,
                                //SymbolName = captureData.Symbol,
                                //ExchangeId = exchangeId
                            };

                            var isFinal = captureData.Kline.Final;
                            currentSymbolDataItem.Update(captureData.Symbol, item, isFinal);
                            MainObj.ItemUpdated(currentSymbolDataItem, captureData.Symbol, isFinal);
                            if (!isFinal) //maybe 10 per minute of these
                            {
                                if (!countDownStarted)
                                {
                                    //countdownSeconds = captureData.Kline.EndTimestamp.Second;
                                    countDownStarted = true;
                                    countdownTimer.Start();
                                }
                            }
                            else
                            {
                                var itemsToSave = GetItemsToSave(item, captureData.Symbol);
                                if (itemsToSave.Count > 0)
                                {
                                    if (MainObj.WRITE_TO_DB)
                                    {
                                        bool block = itemsToSave.Count > 1;
                                        if (block)
                                        {
                                            LogConnection.WriteLine($"Adding block of {itemsToSave.Count} data items for {captureData.Symbol}...");
                                        }

                                        //db.Configuration.AutoDetectChangesEnabled = false; //speeds up entity framework but not used here

                                        //INSANELY SLOW USING EF


                                        //cannot mix EF with SQL, won't write out sometimes
                                        //also, no need to set identity to ON that just throws an error with SQL query

                                        //using (DataContext db = new DataContext())
                                        //{
                                        //db.Database.ExecuteSqlCommand("SET IDENTITY_INSERT[dbo].[SymbolDataItems] ON");

                                        //foreach (var p in itemsToSave)
                                        //{
                                        //    lastProcessTimestamp = DateTime.UtcNow; //stops reconnection during long saves, probably not an issue with this but EF methods just took hours
                                        //    p.ExecuteInsertCommand(db);
                                        //}

                                        //db.Database.ExecuteSqlCommand("SET IDENTITY_INSERT[dbo].[SymbolDataItems] OFF");
                                        var value = System.Configuration.ConfigurationManager.ConnectionStrings["DataContext"];
                                        using (SqlConnection conn = new SqlConnection(value.ToString()))
                                        {
                                            conn.Open();

                                            foreach (var p in itemsToSave)
                                            {
                                                lastProcessTimestamp = DateTime.UtcNow; //stops reconnection during long saves, probably not an issue with this but EF methods just took hours
                                                p.ExecuteInsertCommand(captureData.Symbol, conn);

                                            }
                                        }
                                    }
                                    //var s = string.Empty;
                                    //if interruption is v. short, may not do a block interpolate, so need to remove symbol from update list anyway
                                    if (symbolNamesToUpdate.Count > 0 && symbolNamesToUpdate.Contains(captureData.Symbol))
                                    {
                                        symbolNamesToUpdate.Remove(captureData.Symbol);

                                        if (symbolNamesToUpdate.Count > 0)
                                        {
                                            string s = string.Join(", ", symbolNamesToUpdate);
                                            LogConnection.WriteLine($"Symbols remaining to connect: {s}");
                                        }
                                        else
                                        {
                                            LogConnection.WriteLine("All symbols connected.");
                                        }
                                    }
                                }
                            }

                            lastCaptureTimestamp = captureData.Kline.EndTimestamp;
                        }
                        catch (Exception ex)
                        {
                            //can get a 400 error if timestamps get out of synch, so easiest thing is just to reconnect
                            LogConnection.WriteLine($"CaptureData.Kline exception at {DateTime.UtcNow:G}: {ex}");
                            HandleError();
                        }
                    }
                    return;
                }
                #endregion

                #region Node connector REST data (unused)
                //from REST
                /*
                #region Message
                if (captureData.Message != null)
                {
                    LogConnection.WriteLine($"Message: {captureData.Message}");

                    return;
                }
                #endregion
                */

                //for all trade data, data is projected onto a new object (defined in Trade Engine), to reduce complexity of data presented to that class,
                // and to make all Trade Engine data exchange-agnostic
                /*
                if (captureData.Kline == null)
                {
                    #region Exchange info
                    if (captureData.ExchangeInfo != null)
                    {
                        try
                        {
                            var exchangeInfoData = JsonConvert.DeserializeObject<ExchangeInfoData>(captureData.ExchangeInfo);

                            //note, no trading pairs have STOP_LOSS or TAKE_PROFIT order types available, only the _LIMIT types
                            //var tradingPairs = exchangeInfoData.Symbols.Where(x =>
                            //    x.Status == "TRADING" &&
                            //    x.OrderTypes.Contains("MARKET") &&
                            //    x.OrderTypes.Contains("STOP_LOSS_LIMIT") &&
                            //    x.OrderTypes.Contains("TAKE_PROFIT_LIMIT")
                            //    )
                            //    .Select(x => (SymbolName: x.Symbol, SpotTradingAllowed: x.IsSpotTradingAllowed, MarginTradingAllowed: x.IsMarginTradingAllowed, OcoAllowed: x.OcoAllowed)).ToList();
                            //
                            //TradeEngine.ExchangeInfoDataReceived(new ExchangeInfoDataFiltered
                            //{
                            //    //there are only 3 items sent anyway, but will do full query in case any others get added
                            //    RequestWeightPerMin = exchangeInfoData.RateLimits.Where(x => x.RateLimitType == "REQUEST_WEIGHT" && x.Interval == "MINUTE" && x.IntervalNum == 1).Select(x => x.Limit).FirstOrDefault(),
                            //    MaxOrdersPerDay = exchangeInfoData.RateLimits.Where(x => x.RateLimitType == "ORDERS" && x.Interval == "DAY" && x.IntervalNum == 1).Select(x => x.Limit).FirstOrDefault(),
                            //    MaxOrdersPer10Secs = exchangeInfoData.RateLimits.Where(x => x.RateLimitType == "ORDERS" && x.Interval == "SECOND" && x.IntervalNum == 10).Select(x => x.Limit).FirstOrDefault(),
                            //
                            //    //also there is a max. orders, but only interested in algo orders here
                            //    MaxOpenAlgoOrders = exchangeInfoData.ExchangeFilters.Where(x => x.FilterType == "EXCHANGE_MAX_ALGO_ORDERS").Select(x => x.MaxNumAlgoOrders).FirstOrDefault(),
                            //    TradingPairs = tradingPairs
                            //
                            //});

                            InvokeAccountDataRequestCallbacks(
                                ExchangeRequest.EXCHANGE_INFO,
                                new ExchangeEventArgs
                                {
                                    Result = exchangeInfoData
                                }
                            );
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region Account info
                    if (captureData.Account != null)
                    {
                        try
                        {
                            var accountData = JsonConvert.DeserializeObject<AccountData>(captureData.Account);

                            //symbol agnostic, so get callback for the correct trade
                            InvokeAccountDataRequestCallbacks(
                                ExchangeRequest.ACCOUNT,
                                new ExchangeEventArgs
                                {
                                    Result = new AccountDataFiltered
                                    {
                                        Balances = accountData.Balances
                                    }
                                }
                            );
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region Quote price info
                    if (captureData.QuotePrice != null)
                    {
                        try
                        {
                            var quotePriceData = JsonConvert.DeserializeObject<QuoteData>(captureData.QuotePrice);

                            //symbol agnostic, so get callback for the correct trade
                            InvokeAccountDataRequestCallbacks(
                                ExchangeRequest.QUOTE_PRICE,
                                new ExchangeEventArgs
                                {
                                    Result = quotePriceData
                                }
                            );
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region New order info
                    if (captureData.NewOrder != null)
                    {
                        try
                        {
                            var newOrderData = JsonConvert.DeserializeObject<NewOrderData>(captureData.NewOrder);
                            newOrderData.CalculateAverageFillPrice();
                            (MainObj.GetTradeEngineForSymbol(newOrderData.Symbol) as TradeEngineBinance).OnDataReceivedForTrade_synchronised(newOrderData);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region New OCO info
                    if (captureData.NewOCO != null)
                    {
                        try
                        {
                            var newOCOData = JsonConvert.DeserializeObject<NewOCOData>(captureData.NewOCO);
                            (MainObj.GetTradeEngineForSymbol(newOCOData.Symbol) as TradeEngineBinance).OnDataReceivedForTrade_synchronised(newOCOData);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region Cancel OCO info (only an OCO will be cancelled, as we are not placing limit orders on the exchange
                    if (captureData.CancelOrder != null)
                    {
                        try
                        {
                            var cancelOrderData = JsonConvert.DeserializeObject<NewOCOData>(captureData.CancelOrder);
                            var symbol = cancelOrderData.OrderReports[0].Symbol; //since both orders will have the same symbol
                            (MainObj.GetTradeEngineForSymbol(symbol) as TradeEngineBinance).OnDataReceivedForTrade_synchronised(cancelOrderData);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                    #region Query order info
                    if (captureData.QueryOrder != null)
                    {
                        try
                        {
                            var orderData = JsonConvert.DeserializeObject<OrderData>(captureData.NewOrder);
                            (MainObj.GetTradeEngineForSymbol(captureData.Symbol) as TradeEngineBinance).OnDataReceivedForTrade_synchronised(orderData);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                    #endregion

                }
                */
                #endregion

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogConnection.WriteLineTimestamp($"Binance capture output exception at {DateTime.UtcNow:G}: " + ex.ToString());
            }
        }
        #endregion

        #region Invoked callbacks

        //invoke all callbacks which match the key for the exchange request, and remove those from the callbacks list
        //this is for symbol-agnostic data (account info) which must go just to the trade engine which is waiting for it
        //so there will only be one key used anyway
        private void InvokeAccountDataRequestCallbacks(ExchangeRequest exchangeRequest, ExchangeEventArgs eventArgs)
        {
            try
            {
                var _callBacks = (new KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>>[callBacks.Count]);
                callBacks.CopyTo(_callBacks); //in simulation, this will be called again immediately thus modifying the collection and throwing exception
                callBacks.RemoveAll((KeyValuePair<ExchangeRequest, EventHandler<ExchangeEventArgs>> kvp) =>
                {
                    return kvp.Key == exchangeRequest;
                });

                var items = _callBacks.Where(x => x.Key == exchangeRequest);
                foreach (var item in items)
                {
                    item.Value?.Invoke(this, eventArgs);
                }

            }
            catch { }

        }

        #endregion



    }
}
