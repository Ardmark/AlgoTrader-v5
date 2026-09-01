using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Windows.Threading;

namespace AlgoTrader.Shared.ExchangeConnection
{
    public partial class ExchangeDataProcessorBinance : ExchangeDataProcessor
    {

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

        #endregion

        #region Timestamp conversion
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

        #region Properties

        [JsonExtensionData] //or this won't be serialized correctly
        public List<string> AdditionalData { get; set; }

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
                }
            }

            return ret;
        }

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

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogConnection.WriteLineTimestamp($"Binance capture output exception at {DateTime.UtcNow:G}: " + ex.ToString());
            }
        }
        #endregion

    }
}
