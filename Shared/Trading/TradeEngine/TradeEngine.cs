using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.MovingAverages;
using OpenExchangeRates;
using System.Data.Entity;
using System.Windows.Controls;
using System.Windows.Media;
using static AlgoTrader.Shared.BinanceAPI;
using Brushes = System.Windows.Media.Brushes;

namespace AlgoTrader.Shared
{
    public abstract partial class TradeEngine : DisplayPanel
    {
        #region Trade engine parameters

         //start hour
        //need to offset for bst
        //so when we want to start at 9 AM for example, the candle timestamp will be 8 AM so that is what we need
        //but - this will also be displayed, so need to add the time offset onto the display data as well when drawing time bar
        private static int startHour = 8;
        public static int START_HOUR
        {
            get
            {
                //line up with BST, by offsetting into the data by 1 hour when necessary
                return startHour - BSTOffset;
            }
        }

        public static int BSTOffset
        {
            get
            {
                return (int)TimeZoneInfo.Local.GetAdjustmentRules()[0].DaylightDelta.TotalHours;
            }
        }
        public static string LIVE_KEY { get; set; }
        public static string LIVE_SECRET { get; set; }
        public static string TESTNET_KEY { get; set; }
        public static string TESTNET_SECRET { get; set; }
        public static bool USE_TESTNET { get; set; } = true;
        public static double FEE { get; set; }
        public static bool USE_FIXED_LEVERAGE { get; set; } = true;
        public static double MIN_ALLOWED_LEVERAGE_SHORT { get; set; } = 1;
        public static double MIN_ALLOWED_LEVERAGE_LONG { get; set; } = 1.333;
        public static double MAX_ALLOWED_LEVERAGE { get; set; } = 10;

        //stops
        public static double STOPLINE_MIN_VALUE { get; set; } = 0.2; //Do not set to zero. Lines set closer than this to order will trigger warning
        public static Dictionary<double, double> STOP_MIN_VALUE { get; set; }
        public static Dictionary<double, double> STOP_MAX_VALUE { get; set; }
        public static double DEFAULT_LEVERAGE { get; set; }
        public static Dictionary<double, double> MIN_LEVERAGE { get; set; }
        public static Dictionary<double, double> MAX_LEVERAGE { get; set; }
        public double GetValueForTimeframe(Dictionary<double,double> values)
        {
            if (values.TryGetValue(TimeFrame, out double value))
            {
                return value;
            }
            LogTrades.WriteLine("Could not get default value for Timeframe, using 0.5%");
            return 1; //just to be safe
        }
        public static int NUM_BARS_FOR_FLOOR { get; set; } = 0; // when this many bars clear (higher lows) will set floor
        public static double MOVE_STOPS_AMT { get; set; } = 0.1;
        public static double MAX_STOP_LOSS { get; internal set; } = 1;

        //take profit
        public static double INITIAL_TAKE_PROFIT_PERCENT_MARKET { get; set; } = 0; //initial take profit percent (set to zero to block setting TP initially) - set to any % to place an initial take profit  
 
        //break even
        public static double BREAK_EVEN_PERCENT { get; set; } = 10;

        //spike
        public static int SPIKE_TRIGGER_PERCENT { get; set; } = 5;   //set to zero to disable spike. 
        public static int SPIKE_TRIGGER_FRAMES { get; set; } = 15;
        public double SPIKE_PERCENT { get; set; } // will split price from spike start to current price by this amount, and enter floor at that level

        //trading
        public static bool USE_SIMULATED_EXCHANGE { get; set; } = false; //This is now unused, it bypasses exchange for price and quantity, and does not set stops or orders on exchange
        public static double BREAK_EVEN_THRESHOLD { get; set; } = 0.5;  //percent above entry regarded as break-even, will show warning if attempt to set b/e below this
        public static int MAX_TRADES_PER_24_HOURS { get; set; } = 10;
        public static int MAX_CONCURRENT_TRADES { get; set; } = 1;
        public static int MINIMUM_TRADE_TIME { get; set; } = 60;
        public static double RESTRICTED_TRADE_VALUE { get; set; } = 0;
        public static double MAX_ENTRY_DIFF_PERCENT { get; set; } = 0; //in Order.cs so must be shared: maximum allowable difference for an order or flip to happen; more than this, the trade won't enter
        public static double ENTRY_LINE_PRICE_GAP_PC { get; set; } = 0; //this percent will be used to offset line for trade entry
        public static double STOP_LINE_PRICE_GAP_PC { get; set; } = 0; //this percent will be used to offset line for trade

        public static bool AUTO_EXTEND_LINES { get; set; } = true;

        #endregion


        public enum Side
        {
            ENTER,
            CLOSE
        }

        #region Fields
        System.Timers.Timer timer = null;
        protected bool isManualSynch = false;
        public const double TIMEOUT_SECONDS = 15; //timeout for exchange data
        public const double CHECK_EXCHANGE_INTERVAL_SECONDS = 15;
        public static ExchangeId EXCHANGE;
        public static double Leverage = 1;
        public static bool showBalancesDialog = false; //Binance can't really set the balance on a specific trade as it is obtained asynchronously, this is just to only show the dialog when manually requested

        public List<_SymbolDataItem> results = [];
        List<_SymbolDataItem> framesetResults = [];
        public _SymbolDataItem currentDataItem;
        public int currentFrame;
        public DateTime firstTimestamp;
        public bool dataReady = false;
        public string currentTradeSetReference = null;
        public bool hasSpiked; //easier than saving as a persistant property
        protected bool connectionInitialised = false;
        bool hasFailedToClose = false;

        //Order object fields
        public Order_PriceBreak priceBreakOrderIndicator;
        public Order_PriceBreakMACD macdPriceBreakOrderIndicator;
        public Order_PriceBreakHMA hmaPriceBreakOrderIndicator;
        public HMA hma_100, ema_63, ema_27, ema_20, ema_10, ema_5; //unused but will throw compiler errors from other classes if not defined
        public const int emaOffset = 10;
        
        //lock is just a keyword which implements Monitor.Enter,
        //and the locked object is just to lock entry to the method, it is not locked in itself
        //intellisense does not like locking the list
        public readonly object synchLock = new object(); //lock for all exchange-related processing
        public readonly object listUpdateLock = new object(); //lock for list update, so UI cannot access list whilst it is being changed (avoids exception)        
        
        //Trade fields
        public _Trade _order, _orderOrStub;
        private _Trade _entry, _entryOrStub, _stop, _ceiling, _floor, _breakEven, _takeProfit, _close;
        private List<_Trade> _activeTrades, _allTrades, _waitingOrders;
        private List<_ChartLine> _activeLines, _allLinesForSymbol;
        private bool dbUpdateRequired = false;
        public bool tradeStatusChanged = false;

        //saved data context
        public DataContext DataContext { get; set; }

        //UI drawing lists
        internal List<_Trade> UI_Trades = [];
        internal List<_ChartLine> UI_Lines = []; //just the lines, no trades included, for drawing (disposed data context)
        #endregion

        #region Properties
        //gets all waiting orders for this symbol
        public List<_Trade> WaitingOrders
        {
            get
            {
                return _waitingOrders != null
                    ? _waitingOrders
                    : _waitingOrders = AllTrades.Where(x =>
                            x.Action == TradeAction.ORDER &&
                            x.GlobalStatus == TradeGlobalStatus.ACTIVE &&
                            x.ItemStatus == TradeItemStatus.OPEN &&
                            x.TradeExecutionStatus == TradeExecutionStatus.OK).ToList();
            }
            set
            {
                _waitingOrders = value;
            }
        }

        //get all trades associated with a running Order
        public List<_Trade> ActiveTrades
        {
            get
            {
                return _activeTrades != null
                    ? _activeTrades
                    : _activeTrades = Order != null
                        ? AllTrades.Where(x =>
                            x.TradeReference == Order.TradeReference
                            ).ToList()
                    : new List<_Trade>();
            }
            set
            {
                _activeTrades = value;
            }
        }

        //get all lines associated with a running Order
        public List<_ChartLine> ActiveLines
        {
            get
            {

                return _activeLines != null
                    ? _activeLines
                    : _activeLines = Order != null
                        ? DataContext.Lines.Include(x => x.Trade).Where(x =>
                            x.TradeReference == Order.TradeReference
                            ).ToList()
                        : new List<_ChartLine>();

            }
            set
            {
                _activeLines = value;
            }
        }

        //get a list of all trades, for this symbol
        public List<_Trade> AllTrades
        {
            get
            {
                return _allTrades != null
                ? _allTrades
                : _allTrades = DataContext.Trades.Where(x =>
                                    x.SymbolName == symbolName
                                    ).ToList();
            }
            set
            {
                _allTrades = value;
            }
        }

        //get a list of all lines with trades included, for this symbol
        public List<_ChartLine> AllLinesForSymbol
        {
            get
            {
                return _allLinesForSymbol != null
                ? _allLinesForSymbol
                : _allLinesForSymbol = DataContext.Lines.Include(x => x.Trade).Where(x =>
                                            x.SymbolName == symbolName
                                            ).ToList();

            }
            set
            {
                _allLinesForSymbol = value;
            }
        }

        //the running Order and other trades (for Order, item status will be Closed and global status Active)
        public _Trade Order
        {
            get
            {
                return _order != null
                    ? _order
                    : _order = AllTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.ORDER &&
                                    x.GlobalStatus == TradeGlobalStatus.ACTIVE &&
                                    x.ItemStatus == TradeItemStatus.CLOSED
                                    );
            }
            set
            {
                _order = value;
            }
        }
       
        public _Trade Entry
        {
            get
            {
                return _entry != null
                    ? _entry
                    : _entry = ActiveTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.ENTRY &&
                                    x.ItemStatus == TradeItemStatus.OPEN);
            }
            set
            {
                _entry = value;
            }
        }

        public _Trade Stop
        {
            get
            {
                return _stop != null
                    ? _stop
                    : _stop = ActiveTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.STOP &&
                                    x.ItemStatus == TradeItemStatus.OPEN);
            }
            set
            {
                _stop = value;
            }
        }
 
        public _Trade TakeProfit
        {
            get
            {
                return _takeProfit != null
                    ? _takeProfit
                    : _takeProfit = ActiveTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.TAKE_PROFIT &&
                                    x.ItemStatus == TradeItemStatus.OPEN);
            }
            set
            {
                _takeProfit = value;
            }
        }
  
        public _Trade Close
        {
            get
            {
                return _close != null
                    ? _close
                    : _close = ActiveTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.CLOSE &&
                                    x.ItemStatus == TradeItemStatus.OPEN);
            }
            set
            {
                _close = value;
            }
        }


        public _Trade GetOppositeOrder(_Trade trade)
        {
            var oppositeSide = GetOppositePosition(trade);

            var trades = AllTrades.Where(x =>
                                            x.Action == TradeAction.ORDER &&
                                            x.Position == oppositeSide &&
                                            x.ItemStatus == TradeItemStatus.OPEN &&
            
                                            x.GlobalStatus == TradeGlobalStatus.ACTIVE).ToList();
            if (trades.Count == 0)
            {
                return null;
            }
            if (trades.Count == 1)
            {
                return trades[0];
            }
            else
            {
                //look for the trade with the closest price to the parameter trade
                //probably best to do this, just in case there are more than one
                //This is only used to calculate leverage at entry point. The flip is done with the actual opposite trade which triggers the flip.
                _Trade ret = null;
                var priceDiff = double.MaxValue;
                foreach (var _trade in trades)
                {
                    var _priceDiff = Math.Abs(_trade.Price - trade.Price);
                    if (_priceDiff < priceDiff)
                    {
                        priceDiff = _priceDiff;
                        ret = _trade;
                    }
                }
                return ret;
            }

        }
        #endregion

        #region Get Trades and Lines for UI event use

        internal List<_Trade> GetTradesForTradeReference(string reference = null)
        {
            var _reference = reference ?? currentTradeSetReference; //reference == null ? currentTradeSetReference : reference;
            return AllTrades.Where(x => x.TradeReference == _reference).ToList();
        }

        internal List<_ChartLine> GetLinesForTradeReference(string reference = null)
        {
            var _reference = reference ?? currentTradeSetReference;
            return AllLinesForSymbol.Where(x => x.TradeReference == _reference).ToList();
        }

        #endregion

        #region Get Trades and Lines for drawing

        public void GetUIListData(DataContext dataContext)
        {
            if (dataContext != null)
            {
                UI_Lines = [.. dataContext.Lines.AsNoTracking().Include(x => x.Trade).Where(x => x.SymbolName == symbolName)]; //same as .ToList()
                UI_Trades = [.. dataContext.Trades.AsNoTracking().Where(x => x.SymbolName == symbolName)];
            }
        }
        public void GetUILists(DataContext db, bool shallow = false, Action callback = null)
        {
            if (shallow)
            {
                callback?.Invoke();
                return; //do nothing, the lists will have had one or more members directly updated
            }
            //get UI lists for drawing. These need now to be updated since there have been some changes somewhere
            lock (listUpdateLock) //locking here in case UI thread tries to read the data for drawing
            {
                if (callback != null)
                {
                    //this will be just to get a shallow copy of some of the trades from the existing lists, by just locking the update method here
                    callback();
                    return;
                }
                else
                {
                    //refresh the lists - these lists are not tracked
                    GetUIListData(db);

                }
            }
        }
        public void UpdateUIList(_Trade trade)
        {
            if (trade == null) return;
            var t = tradeEngine.UI_Trades.FirstOrDefault(x => x.Id == trade.Id);
            if (t != null)
            {
                trade.CopyTo(t);
            }
        }
        public void UpdateUIList(_ChartLine line)
        {
            if (line == null) return;
            var l = tradeEngine.UI_Lines.FirstOrDefault(x => x.Id == line.Id);
            if (l != null)
            {
                line.CopyTo(l);
            }
        }
        #endregion

        #region Context synchronisation
        public void InitForDataContext(Action callback)
        {
            lock (synchLock)
            {
                DataContext = new DataContext();
                //Debug.WriteLine($"InitForDataContext starting {Thread.CurrentThread.ManagedThreadId}");
                try
                {
                    callback?.Invoke();
                }
                finally
                {
                    if (dbUpdateRequired)
                    {
                        try
                        {
                            SaveChangesForDataContext();
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                    ResetForDataContext();
                    dbUpdateRequired = false;
                    DataContext.Dispose();
                    DataContext = null; //must not hold reference to using parameter
                }

            }


            // Debug.WriteLine($"InitForDataContext ending {Thread.CurrentThread.ManagedThreadId}");
        }
        public void SaveChangesForDataContext()
        {
            lock (synchLock)
            {
                if (dbUpdateRequired)
                {
                    try
                    {
                        DataContext.SaveChanges();
                        GetUIListData(DataContext);
                        if (tradeStatusChanged)
                        {
                            tradeStatusChanged = false;
                            MainObj.TradeStatusChanged(this, true);
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }
        public void ResetForDataContext()
        {
            //must clear everything, or next time the lists will still be attached to the previous (nulled) DataContext so won't propagate
            //have had some inexplainable behaviour, where ActiveLines is still a list at start of method, but of zero length,
            //this is with BTCUSDT in a QTB trade, possibly because the properties were referenced in a different process?
            //seems that you need to null out the underlying fields??
            _activeLines = _allLinesForSymbol = null;
            _activeTrades = _allTrades = _waitingOrders = null;
            _entry = _entryOrStub = _stop = _ceiling = _floor = _takeProfit = _close = _order = _orderOrStub = _breakEven = null;
        }

        //set the update request status. This is only called from within a valid DataContext loop (InitForDataContext)
        public void SetUpdateRequest(_Trade trade, TradeUpdateRequest updateRequest = TradeUpdateRequest.UPDATE)
        {
            try
            {
                //UpdateRequest.ADD is not used, since it is only the Id which determines if the object should be added to the DB, and it will not duplicate if added twice anyway
                if (trade.Id == 0) //should never be non-zero anyway
                {
                    DataContext.Trades.Add(trade); //cannot duplicate in database if added twice, it will only get added once
                }
                else if (updateRequest == TradeUpdateRequest.DELETE)
                {
                    DataContext.Trades.Remove(trade);
                }
                dbUpdateRequired = true;
            }
            catch { }
        }

        //ditto for lines.
        public void SetUpdateRequest(_ChartLine line, TradeUpdateRequest updateRequest = TradeUpdateRequest.UPDATE)
        {
            //UpdateRequest.ADD is not uses, since it is only the Id which determines if the object should be added to the DB, and it will not duplicate if added twice anyway
            if (line.Id == 0)//should never be non-zero anyway
            {
                DataContext.Lines.Add(line); //cannot duplicate in database if added twice, it will only get added once
            }
            else if (updateRequest == TradeUpdateRequest.DELETE)
            {
                DataContext.Lines.Remove(line);
            }

            dbUpdateRequired = true;
        }

        //ditto but returns the Id, needed to add to windows GUI objects to enable Trade to be found from line, etc.
        public int SetUpdateRequestAdd_UI(_ChartLine line)
        {
            if (line.Id == 0)
            {
                //the only way to get the Id is to add to the db and SaveChanges()
                //cannot manually add the Id as it is overwritten on SaveChanges with the next incremental value
                UI_Lines.Add(line);
                DataContext.Lines.Add(line); //this does set the Id, no need to re-assign object to return value
                DataContext.SaveChanges();
            }
            else
            {
                dbUpdateRequired = true;
            }
            return line.Id;
        }
        public int SetUpdateRequestAdd_UI(_Trade trade)
        {
            if (trade.Id == 0)
            {
                //the only way to get the Id is to add to the db and SaveChanges()
                //cannot manually add the Id as it is overwritten on SaveChanges with the next incremental value
                UI_Trades.Add(trade);
                DataContext.Trades.Add(trade); //this does set the Id, no need to re-assign object to return value
                DataContext.SaveChanges();
            }
            else
            {
                dbUpdateRequired = true;
            }
            return trade.Id;
        }
        #endregion

        #region Init

        public TradeEngine(string symbolName, int timeInterval) : base(symbolName, timeInterval, null, null)
        {
            // movingAverages.Add(hma_100 = new HMA(CandlePosition.CLOSE, Chart, 100));
        }

        public override void Init(int _timeInterval) //time interval does not change so ignore
        {

            base.Init(_timeInterval);

            tradeEngine = this; //in case the Results property is invoked by mistake, which will return Results

            #region Get active trade
            //get any entered trade. By checking for entry first, then open order, it is possible to run multiple orders on a single symbol,
            // to allow a trade to stop out then be re-entered higher on break.
            // however this adds complexity so has not been implemented yet

            //must still be reading this symbol name
            if (!SymbolNames.GetSymbolNames().Contains(symbolName))
            {
                return;
            }

            //get any pending order - ignore Constants.ALLOW_MULTIPLE_SYMBOL_TRADES, pick up whatever is entered
            try
            {
                using (DataContext dbInit = new DataContext())
                {
                    GetUILists(dbInit);
                }
            }
            catch
            {
            }

            if (USE_SIMULATED_EXCHANGE)
            {
                //Binance_StartSimulation();
            }
            #endregion
        }

        public override void InitIndicators(int timeFrame)
        {
            base.InitIndicators(timeFrame);
        }

        public override void InitTimeFrame(int timeFrame)
        {
            base.InitTimeFrame(timeFrame);
        }

        #endregion

        #region Add Item

        public override void ItemAdded(_SymbolDataItem item, bool canComputeTradeEntry, bool liveItem)//, bool redrawOnly) //canComputeTradeEntry means outside of interruption periods
        {
            base.ItemAdded(item, canComputeTradeEntry, liveItem);
            try
            {
                results.Add(item);

                if (!liveItem)
                {
                    return;
                }

                //so as to not enter a trade during an interpolation period, i.e. after a connection failure, interpolation must be completed before testing for trade entry

                //MainObj.AddItem() calls ItemUpdated(), so no need to do it here

                //must have at least one update for data to be ready, so any interpolation will have been performed by now and current data item will be up to date
                dataReady = true;

                using DataContext db = new();
                GetUILists(db); //just to synch with db, in case something has been removed or not saved correctly
            }
            catch
            {

            }
        }

        public override void ItemUpdated(_SymbolDataItem item, bool itemAdded = false)
        {

            //wait until confirmed connection before starting to send requests to exchange
            if (!USE_SIMULATED_EXCHANGE) //because it will already have been done
            {
                if (!connectionInitialised)
                {
                    connectionInitialised = true;
                    //Binance_InitEvents();
                }
            }

            currentDataItem = item;

            //synchronise the trades with any UI changes
            RunOrders_synchronised(results.Count - 1, itemAdded);

            
        }

        #endregion

        #region Exchange-specific overridden methods
        public virtual void Test() { }
        public abstract void ShowAccountBalances();
        protected abstract bool SendStopToExchange_Synchronised(bool isUpdating);
        protected abstract bool RemoveStopFromExchange_Synchronised();
        protected abstract bool CancelOpenOrders();
        protected abstract void UpdateLiveState();
        protected abstract double GetLastPrice();
        protected abstract double GetBalanceToEnterTrade(double freeBalance, double lastQuoteValue, TradePosition position);
        protected abstract bool SendOrder(TradePosition position, double orderQuantity, string orderId, Side side, out double averagePrice, out double executedQuantity);
        protected abstract double GetCorrectPrecisionForBaseAssetTrade(double balanceToTrade);
        protected abstract (double freeBalance, double totalEquity) GetTotalEquity();
        protected abstract double GetFreeBaseAssetBalance();
        protected abstract bool InitForEntryOrClose();
        protected abstract bool IsTradeClosed();
        #endregion

        #region Run orders

        //return param is only for backtesting
        public bool RunOrders_synchronised(int currentFrame, bool itemAdded)
        {
            //when called directly from ItemUpdated, this will run live trade
            //when called for backtesting, will exit the test session when this returns false

            bool redrawRequired = false;
            bool ret = false; //this is for returning from backtest loop, so default is to continue backtesting when exiting from tp, stop etc.
            this.currentFrame = currentFrame;

            double timeoutSeconds = USE_SIMULATED_EXCHANGE ? 1000 : TIMEOUT_SECONDS;

            InitForDataContext(() =>
            {
                var v = Order;
                if (Order != null) //need to include stubs in Orders to do the exchange trade, but don't execute them here!
                {
                    var posText = Order.Position == TradePosition.LONG ? "Long" : "Short";
                    var currentTradeIndicator = GetCurrentTradeIndicator(Order);

                    #region Timeout for close orders
                    //order may have closed or not, on the exchange
                    // - need to check if it has already closed, or deal with the rejected order if the balance is zero (has already been closed)
                    if (Close != null)
                    {
                        if (CheckForTimeout_Synchronised(Close))
                        {
                            LogTrades.WriteLine(string.Format("Close order for {0} timed out at {1}", Close.SymbolName, DateTime.UtcNow));

                            MainObj.exchangeConnector.RestartConnection();
                            redrawRequired = true;

                            //with Binance, the stops sometimes do not get re-added after removal, during the update loop
                            //if the stops are not present but are still on the system here, the system will attempt to remove the stops before closing the trade.
                            // - this will fail, with an error response so the process will not continue.
                            //To fix this, if the Close timeouts, it can be assumed this was the problem, so attempt another close without removing the stops first.
                            if (hasFailedToClose == false)
                            {
                                LogTrades.WriteLine("Attempting to close order without removing stops first...");
                                hasFailedToClose = true;
                                CloseTradeOnExchange(null);
                            }
                            else
                            {
                                LogTrades.WriteLine("Second timeout, so closing trade locally. May not have closed on exchange!!!");

                                //The problem here is that if the close order is now removed, it will just loop back around and do this indefinitely.
                                //So the only solution is to fully close the order here, with a message and mark as unsynched.
                                hasFailedToClose = false;
                                Entry.GlobalStatus = Order.GlobalStatus = Close.GlobalStatus = TradeGlobalStatus.STOPPED;
                                Entry.ItemStatus = Order.ItemStatus = Close.ItemStatus = TradeItemStatus.CLOSED;
                                Entry.TradeExecutionStatus = Order.TradeExecutionStatus = Close.TradeExecutionStatus = TradeExecutionStatus.EXCHANGE_NOT_SYNCHED;
                                SetUpdateRequest(Entry);
                                SetUpdateRequest(Order);
                                SetUpdateRequest(Close);
                            }

                            return;
                        }
                        //no further processing whilst close is pending
                        return;
                    }
                    #endregion

                    #region Entry
                    if (Entry != null)
                    {
                        if (Entry.TradeExecutionStatus == TradeExecutionStatus.OK)
                        {
                            if (currentTradeIndicator.ExecuteEntry_synchronised(itemAdded))
                            {
                                redrawRequired = true;
                            }

                            //entry should never terminate trade (in backtesting) so cannot return false

                            //RunTrade_exchangeSpecific();
                        }

                        /*
                         * Not checking Entry for timeout at all. The Entry timestamp must be updated to current time each loop, and each time it times out, otherwise it will continue
                         *  to execute the CheckForTimeout script continuously. Only solution is to have a second timestamp for the last entry execution time, and test against that
                         *  
                         * So if any Entry script fails to execute, it just will show as not synched. Should not be a problem, with stops on the exchange
                         * */
                        else
                        {
                            //don't want to update the Entry timestamp if not synched, which it has to do or it will check for timeout each loop
                            // - so there is a separate field for this, easier than adding another one to the Trade object
                            if (Entry.TradeExecutionStatus != TradeExecutionStatus.EXCHANGE_NOT_SYNCHED && Entry.TradeExecutionStatus != TradeExecutionStatus.UPDATE_LIVE_STATE)
                            {

                                if (CheckForTimeout_Synchronised(Entry))
                                {
                                    //an interrupted entry won't be closed, since there is no way to tell whether this has entered on the exchange (at time of writing)
                                    //leave the script name in place. 

                                    Entry.TradeExecutionStatus = TradeExecutionStatus.EXCHANGE_NOT_SYNCHED;
                                    SetUpdateRequest(Entry);
                                    //won't kill the order, just mark it as not synched. It may still be running and will then get re-synched automatically anyway.
                                    redrawRequired = true;
                                    tradeStatusChanged = true;
                                    return;

                                }
                            }
                        }
                    }
                    #endregion

                    #region Take Profit

                    //just a simplified version of the stop logic, no need for a separate order object to do this
                    if (TakeProfit != null)// && TakeProfit.TradeExecutionStatus == null)
                    {
                        var line = ActiveLines.GetLineForTrade(TakeProfit);
                        if (line != null) //just in casse
                        {
                            (TakeProfit.Price, _) = line.GetPriceAtTime(currentDataItem.Timestamp, true);

                            var low = tradeEngine.GetLow(TakeProfit);
                            var high = tradeEngine.GetHigh(TakeProfit);

                            //check if tp price is crossed now price is set from line


                            //this will execute first, so if the trigger percent is zero, the ceiling will execute immediately
                            if (Order.Position == TradePosition.LONG
                                                        ? high >= TakeProfit.Price
                                                        : high <= TakeProfit.Price)
                            {
                                //not using the trigger, just an immediate exit instead
                                LogTrades.WriteLine($"Exiting {posText} {Entry.SymbolName} on take profit cross at {DateTime.UtcNow}");
                                CloseTradeOnExchange();
                                redrawRequired = true;
                                return;

                            }

                            //move stop to current time, if earlier than this
                            ExtendLineToCurrentTime_synchronised(TakeProfit);

                        }
                    }


                    #endregion

                    #region Stop(s)

                    //stop can be a dead stop (TradeAction == STOP, TradeType == NO_FLIP_ORDER), a flip stop (TradeAction == WAITING_STOP, TradeType == FLIP_ORDER) or a dead stop for a flip stop (TradeAction == WAITING_STOP, TradeType == STOP_WAITING)
                    //but only the single Stop object gets its price updated, so get any flip stops and update those
                    if (Stop != null)
                    {
                        if (Stop.TradeExecutionStatus == TradeExecutionStatus.OK || Stop.TradeExecutionStatus == TradeExecutionStatus.EXCHANGE_NOT_SYNCHED)
                        {
                            //indicator may terminate trade on other stop conditions, e.g. ema crossover
                            (bool stopExecuted, bool updateStop) = currentTradeIndicator.ExecuteStop_synchronised(itemAdded);

                            if (stopExecuted)
                            {
                                LogTrades.WriteLine($"Exiting {posText} {Entry.SymbolName} on stop cross at {DateTime.UtcNow}");
                                CloseTradeOnExchange();
                                redrawRequired = true;
                                return;//terminate in backtesting, will have already closed trade
                            }

                            if (itemAdded) //No idea why, but this was commented?? This is the only place a live update happens.
                            {
                                SetStopOnExchange_Synchronised();
                            }
                        }

                        //move stop to current time, if earlier than this
                        ExtendLineToCurrentTime_synchronised(Stop);
                    }

                    #endregion

                    #region Timeout and Update Time for stops

                    foreach (var trade in AllTrades.Where(x => x.GlobalStatus == TradeGlobalStatus.ACTIVE)) //will move waiting trades, leave stopped ones
                    {
                        switch (trade.Action)
                        {
                            //case TradeAction.FLOOR:
                            case TradeAction.STOP:
                            //case TradeAction.CEILING:
                            case TradeAction.TAKE_PROFIT:
                                //case TradeAction.BREAK_EVEN:
                                //to reduce db load, only save stop, tp, floor timestamps on the minute
                                if (itemAdded)
                                {
                                    trade.Timestamp = currentDataItem.Timestamp;
                                    SetUpdateRequest(trade);
                                }
                                if (trade.TradeExecutionStatus != TradeExecutionStatus.OK)
                                {
                                    if (CheckForTimeout_Synchronised(trade))
                                    {
                                        redrawRequired = true;
                                        trade.TradeExecutionStatus = TradeExecutionStatus.OK;
                                        MainObj.exchangeConnector.RestartConnection();
                                    }
                                }
                                break;
                        }
                    }
                    #endregion

                }
                #region Test for trade entry

                foreach (var order in WaitingOrders)
                {
                    var currentTradeIndicator = GetCurrentTradeIndicator(order);

                    //when a trade is changed, added or deleted, mark it with a TradeUpdateRequest and add to the trades list
                    (bool hasEntered, bool tradeKilled) = ExecuteTradeEntryIfPossible(order, currentTradeIndicator);
                    redrawRequired = hasEntered || tradeKilled;
                    if (tradeKilled)
                    {
                        //order has failed to enter, so don't want to keep retrying especially with market orders as this will just show endless dialogs
                        order.ItemStatus = TradeItemStatus.CLOSED;
                        order.GlobalStatus = TradeGlobalStatus.STOPPED;
                        order.TradeExecutionStatus = TradeExecutionStatus.OK;
                        SetUpdateRequest(order);
                        redrawRequired = true;
                    }
                    if (hasEntered)
                    {
                        Order = order;
                        hasFailedToClose = false;
                        break;
                    }
                }

                #endregion

                ret = true;
            });

            //cannot redraw until db write is complete
            if (redrawRequired)
            {
                MainObj.DrawCurrentDispatcher();
            }

            //for backtesting, quit when no more trades to test and nothing running
            //live does not need to return anything
            return ret;

        }

        #endregion

        #region Enter trade

        protected (bool hasEntered, bool tradeKilled) ExecuteTradeEntryIfPossible(_Trade trade, Order currentTradeIndicator)
        {
            bool canEnter =
                trade.Type == TradeType.MARKET_ORDER ||
                currentTradeIndicator == null ||
                currentTradeIndicator.CanEnterTrade_synchronised(trade);

            if (canEnter)
            {
                //determine if max number of concurrent or recent trades has been exceded
                OrderFrequency reason = OrderFrequency.OK_TO_ENTER_LIVE;

                //limit max. number of trades per 24 hour period
                var earliest = DateTime.UtcNow.AddDays(-1);
                var totTrades = DataContext.Trades.Where(x =>
                    x.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE &&
                    x.Action == TradeAction.ENTRY &&
                    DateTime.Compare(x.Timestamp, earliest) > 0).Count();

                if (totTrades >= MAX_TRADES_PER_24_HOURS)
                {
                    reason = OrderFrequency.MAX_TRADES_PER_24_HOURS_EXCEDED;
                }
                else
                {
                    if (MINIMUM_TRADE_TIME > 0)
                    {
                        var lastEntry = AllTrades.OrderByDescending(x => x.Timestamp).FirstOrDefault(x => x.Action == TradeAction.ENTRY);
                        if (lastEntry != null)
                        {
                            var timeDiff = DateTime.UtcNow.Subtract(lastEntry.Timestamp).TotalMinutes;
                            if (timeDiff < MINIMUM_TRADE_TIME)
                            {
                                reason = OrderFrequency.PREV_ENTRY_TOO_CLOSE;
                            }
                        }
                    }
                }

                switch (reason)
                {
                    case OrderFrequency.MAX_TRADES_PER_24_HOURS_EXCEDED:
                        LogTrades.WriteLine($" {trade.SymbolName} at {DateTime.Now} - Trade not entered as max. number of entries in 24 hours ({TradeEngine.MAX_TRADES_PER_24_HOURS}) exceded");
                        //AlertDialog.Show($"Could not place order for {trade.SymbolName} as there have already been {TradeEngine.MAX_TRADES_PER_24_HOURS} in 24 hours");
                        return (false, true);

                    case OrderFrequency.PREV_ENTRY_TOO_CLOSE:
                        LogTrades.WriteLine($" {trade.SymbolName} at {DateTime.Now} - Trade not entered as too near to previous trade's close order");
                        return (false, true);

                    case OrderFrequency.OK_TO_ENTER_LIVE:
                        LogTrades.WriteSpacer();

                        var price = trade.Price; //trade may be an active order or a market order entry  currentDataItem.Close;
                        //issue with price of 0, this should not happen here, or matter if trade is live, but this is just to check
                        if (price == 0)
                        {
                            LogTrades.WriteLine(string.Format("{0} not entered at {1} as price was zero.", trade.SymbolName, DateTime.UtcNow));
                            break;
                        }

                        //enter trade on system here, but not active yet
                        if (Entry != null)
                        {
                            //this could be a flip order
                            if (Entry.Position == TradePosition.LONG && trade.Position == TradePosition.SHORT ||
                                Entry.Position == TradePosition.SHORT && trade.Position == TradePosition.LONG)
                            {
                                CloseTradeOnExchange(Entry); //this will start the flip
                            }
                            break;
                        }
                        else
                        {
                            Order = trade;
                            EnterTradeOnExchange();
                        }
                        if (Entry != null)
                        {
                            return (true, Entry.GlobalStatus != TradeGlobalStatus.ACTIVE);
                        }

                        break;
                }
            }

            return (false, false);
        }

        public virtual async void EnterTradeOnExchange()
        {
            #region Test for OK to start entry
            
            if (!USE_TESTNET && !dataReady) return;

            //if this has just restarted, will try to enter any trades immediately but the price won't be available from the websocket yet.
            //so use the REST api instead
            var lastQuoteValue = GetLastPrice();
            if (lastQuoteValue == 0)
            {
                LogTrades.WriteLine("Could not get symbol price");
                KillEntryOrCloseOrder(true);
                return;
            }
            var diff = Math.Abs(MainObj.GetPriceDiff(Order.Price, lastQuoteValue, Order.Position) * 100);

            LogTrades.WriteLine($"Price diff. between entry and current price is {diff}%");

            //determine if this is within an acceptable range
            if (diff > (USE_TESTNET ? 1000 : MAX_ENTRY_DIFF_PERCENT))
            {
                LogTrades.WriteLine("Did not enter trade as price difference between Order and current price was too great");
                return;
            }

            if (!InitForEntryOrClose())
            {
                KillEntryOrCloseOrder(true);
                return;
            }

            //make sure there are no orphaned stops 
            if (!CancelOpenOrders())
            {
                LogTrades.WriteLine("Failed to cancel open orders (OCO), exiting new trade");
                KillEntryOrCloseOrder(true);
                return;
            }

            #endregion

            ResetLiveUpdateTimer();

            LogTrades.WriteLine($"Starting new {Order.Position} order:");

            #region Get balance to trade
            LogTrades.WriteLine("Getting equity...");
            (var freeBalance, var totalEquity) = GetTotalEquity(); //totalEquity is in $, only used for a record in the Entry object
            if (freeBalance <= 0)
            {
                LogTrades.WriteLine($"Could not get free balance for {symbolName}, terminating Entry");
                KillEntryOrCloseOrder(true);
                return;
            }
            freeBalance = USE_TESTNET ? Math.Min(freeBalance, 50) : freeBalance;
            #endregion

            #region Generate Entry
            Entry = Order.Clone(); //item status will be open
            Entry.ItemId = _Trade.GetNewTradeId(); //set the client-assigned Id (could wait for the binance-assigned one instead) but this way, can query status even if no confirmation yet received
            Entry.Action = TradeAction.ENTRY;
            Entry.Price = lastQuoteValue;
            Entry.TradeExchangeStatus = TradeExchangeStatus.ENTERED_OFF_EXCHANGE; //only set live when it actually returns from exchange
            Entry.Timestamp = GetTimeStamp();
            Entry.Id = 0; //so it will get added
            Entry.Balance = freeBalance;
            Entry.Equity = totalEquity;
            Entry.TradeReference = Order.TradeReference;//shared with all trades in this group
            SetUpdateRequest(Entry);
            #endregion

            #region Calculate stop and take profit prices

            //need the closest of the current stop or opposing trade. If neither found, use the minimum stop value
            double stopPercent = GetValueForTimeframe(STOP_MIN_VALUE);
            double stopPrice = 0;

            //get the existing line break stop if it exists
            var stop = AllTrades.FirstOrDefault(x => x.TradeReference == Order.TradeReference && x.Action == TradeAction.STOP);
            if (stop != null)
            {
                Stop = stop;
            }
            var takeProfit = AllTrades.FirstOrDefault(x => x.TradeReference == Order.TradeReference && x.Action == TradeAction.TAKE_PROFIT);
            if (takeProfit != null)
            {
                TakeProfit = takeProfit;
            }

            //even if this comes from a flip, the stop will still be found as the data context has been refreshed for the new order
            if (Stop != null)
            {
                stopPrice = Stop.Price;

                var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == Stop.Id);
                if (stopLine != null)
                {
                    (stopPrice, _) = stopLine.GetPriceAtTime(DateTime.UtcNow, true);
                    stopPercent = MainObj.GetPriceDiffPercent(currentDataItem, stopPrice, Order.Position) * 100;
                }
            }
            else //if there is no stop, take the opposing order as one instead
            {
                var _oppOrder = GetOppositeOrder(Order);
                if (_oppOrder != null)
                {
                    stopPercent = _oppOrder.Price;
                    var orderLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == _oppOrder.Id);
                    if (orderLine != null)
                    {
                        LogTrades.WriteLine("Taking opposite order as stop");
                        (var oppOrderPrice, _) = orderLine.GetPriceAtTime(DateTime.UtcNow, true);
                        stopPercent = MainObj.GetPriceDiffPercent(currentDataItem, oppOrderPrice, Order.Position) * 100;
                    }
                }
            }

            #endregion

            #region Calculate Leverage

            //if a stop line is present, the leverage will be calculated from that, base on the range of stop values and leverages
            // for the timeframe. If no stop line is present, the leverage will be whatever is currently set in the UI
            Leverage = Stop != null ? CalculateLeverageFromPercent(stopPercent, Entry.Position) : Leverage;

            //save the calculated leverage, even if we are not using it
            Order.Leverage = Entry.Leverage = Leverage;

            #endregion

            #region Place order

            Order.TradeExchangeStatus = Entry.TradeExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;

            var tradeAmount = GetBalanceToEnterTrade(freeBalance, lastQuoteValue, Order.Position);
            if (tradeAmount == 0)
            {
                LogTrades.WriteLine($"Failed to get trade amount, terminating Entry");
                KillEntryOrCloseOrder(true);
                return;
            }

            LogTrades.WriteLine($"Placing buy order of {tradeAmount}, adjusted for step size, at {DateTime.Now}...");

            bool accepted = SendOrder(Entry.Position, tradeAmount, Entry.ItemId, Side.ENTER, out double averagePrice, out double executedQuantity);
            if (!accepted)
            {
                LogTrades.WriteLine("Order not accepted, terminating Entry");
                KillEntryOrCloseOrder(true);
                return;
            }

            LogTrades.WriteLine($"{Entry.Position} order FILLED for {Entry.SymbolName} at {DateTime.UtcNow}");

            //at this point, trade is entered live

            hasSpiked = false;

            var currentTradeIndicator = GetCurrentTradeIndicator(Order);

            //get the EF tracked trade - if this is a live entry, will be using a different data context to the one the parameter is running on

            if (Entry == null)
            {
                //if not trading live, will bypass exchange (if not using simulator) and come straight here, so no entry yet
                Entry = Order.Clone();
                Entry.Action = TradeAction.ENTRY;
                Entry.Timestamp = GetTimeStamp();
            }

            Entry.Price = Order.Price = averagePrice;
            Entry.Quantity = Order.Quantity = executedQuantity; //set both the entry and order quantity, as either may be used when closing, etc.
            Entry.TradeExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
            Entry.TradeExecutionStatus = TradeExecutionStatus.OK;

            Order.ItemStatus = TradeItemStatus.CLOSED; //close the order, this is now an active trade
            Order.TradeExecutionStatus = TradeExecutionStatus.OK;
            Order.TradeExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;

            #endregion

            #region Set stop

            //Stop has already been pulled from AllTrades
            if (Stop != null)
            {
                LogTrades.WriteLine("Using existing stop.");
                Stop.Price = Stop.Result = stopPrice; //this is the line price at this time, if a line is present, otherwise the stop price is used
            }
            else
            {
                LogTrades.WriteLine($"Setting stop at {stopPercent}%");

                Stop = Entry.Clone();
                Stop.Price = Stop.Result = MainObj.GetStopPriceFromPercent(Entry.Price, stopPercent, Order.Position); 
                Stop.Action = TradeAction.STOP;
                Stop.ItemId = null; //need a new ID for this as it is a new order.
                AddLineToTrade_synchronised(Stop, DateTime.UtcNow.AddMinutes(-1));
            }

            Stop.Quantity = Entry.Quantity;
            Stop.TradeExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;

            LogTrades.WriteLine("Sending stop to exchange");
            SetStopOnExchange_Synchronised();

            SetUpdateRequest(Stop);
            SetUpdateRequest(Order);
            SetUpdateRequest(Entry);

            #endregion

            #region Get exchange rate
            //cannot have any synched objects after this as synchronisation will exit before this method completes
            LogTrades.WriteLine("Getting exchange conversion rate...");
            var gbp = await GetConversionRate(Entry.ItemId);
            LogTrades.WriteLine($"Conversion rate received of {gbp.ToString("F2")}, Entry complete!");
            #endregion

            //No more access to Entry, Close etc. after call to async GetConversionRate()!!!
            MainObj.DrawCurrentDispatcher();

            tradeStatusChanged = true;
        }

        #endregion

        #region Stop
        
        // will create a new TakeProfit if one is not already set
        //this method is also called externally by Order, TradeEngine UI methods
        public void SetStopOnExchange_Synchronised()
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
            lastPrice = GetLastPrice();

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
            if (TakeProfit != null &&
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
                if (Stop.Price != Stop.Result || TakeProfit.Price != TakeProfit.Result)
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

        public void SynchStopsWithExchange_synchronised()
        {
            InitForDataContext(() =>
            {
                if (Entry != null)
                {
                    if (Stop != null)
                    {
                        isManualSynch = true;
                        SendStopToExchange_Synchronised(true);

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

        #endregion

        #region Close trade

        public virtual async void CloseTradeOnExchange(_Trade triggeringTrade = null)
        {
            #region Test for OK to start close

            if (!USE_TESTNET && !dataReady) return;

            if (Entry == null)
            {
                LogTrades.WriteLine($"No Entry trade present for {symbolName}, cannot close trade, exiting OnExchangeDataReceived");
                return;
            }

            if (!InitForEntryOrClose())
            {
                KillEntryOrCloseOrder(true);
                return;
            }
            #endregion

            ResetLiveUpdateTimer();

            LogTrades.WriteLine("Cancelling open orders.");

            CancelOpenOrders();

            #region Get balance to trade
            //if the base asset balance is very small, assume the trade has exited on the exchange, so go no further

            var coinBalance = GetFreeBaseAssetBalance();
            #endregion

            LogTrades.WriteLine($"Closing order for {symbolName}...");

            double closePrice = currentDataItem.Close; //default value
            var _baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);

            if (IsTradeClosed())
            {
                LogTrades.WriteLine($"No {_baseAsset} to sell, trade has already closed on exchange.");
            }
            else
            {
                //could coinbalance be very small after trade closed? The lot size may result in a small remainder.
                //so will go ahead anyway with trade close and ignore any exceptions
                LogTrades.WriteLine($"Using received balance of {coinBalance} {_baseAsset}");

                #region Generate close order
                if (Close == null)
                {
                    Close = Order.Clone();
                    Close.ItemId = _Trade.GetNewTradeId(); //set the client-assigned Id (could wait for the binance-assigned one instead) but this way, can query status even if no confirmation yet received
                    Close.Action = TradeAction.CLOSE;
                    Close.Timestamp = GetTimeStamp();
                }
                Close.Quantity = GetCorrectPrecisionForBaseAssetTrade(coinBalance);
                #endregion

                #region Place order     
                
                LogTrades.WriteLine($"Placing sell order of {Close.Quantity}, adjusted for step size, at {DateTime.Now}...");

                bool accepted = SendOrder(Close.Position, Close.Quantity, Close.ItemId, Side.CLOSE, out closePrice, out double executedQuantity);
                //if (!accepted)
                //{
                //    KillEntryOrCloseOrder(true);
                //    return;
                //}

                if (accepted)
                {
                    LogTrades.WriteLine($"{Entry.Position} Close order filled for {Entry.SymbolName} at {DateTime.UtcNow}");
                }

                #endregion
            }

            CloseTradeOnSystem(closePrice);

            var entryItemId = Entry.ItemId; //save this since close on system will set Entry = null
            var closeItemId = Close.ItemId; //need this later for conversion rate

            #region Set flip from opposing trade
            if (triggeringTrade != null)
            {
                var triggeringTradeId = triggeringTrade.Id;

                SaveChangesForDataContext();
                ResetForDataContext();

                //ActiveTrades is no loger valid, and this is used to get Order, Stop etc., so refresh the Order

                Order = AllTrades.FirstOrDefault(t => t.Id == triggeringTradeId);
                LogTrades.WriteLine($"Flipping order if possible...");
                EnterTradeOnExchange(); //and that's all there is to it.
            }
            #endregion

            #region Get exchange rate
            //this will cause synchronisation to exit BEFORE this awaitable method completes,
            // so CANNOT have any synched objects accessed after this (like Close, etc.)
            LogTrades.WriteLine("Getting exchange conversion rate...");
            var gbp = await GetConversionRate(closeItemId);
            LogTrades.WriteLine($"Conversion rate received of {gbp.ToString("F2")}, trade close complete!");
            #endregion

            MainObj.DrawCurrentDispatcher();

            tradeStatusChanged = true;
        }

        protected void CloseTradeOnSystem(double price)
        {
            //close a trade, this is a close request with no further intent
            //trade could be an order for a manual close, or a close if system is synching with exchange
            if (Order == null)
            {
                //LogTrades.WriteLine(string.Format(" ++ WARNING ++: Could not close trade for {0} on system as active order was null", symbolName));
                return;
            }

            //if the trade is open, add a closed trade
            //but it could be a running market order, so must still 'close' it      

            //try to get an existing close from the system with this tradeId - if there is one, just update it
            //the _trade parameter may be the order, a stop, or an actual close depending on how this is called
            //- if it is a Close, this would be when synching with exchange. 
            //Otherwise, this will be a manual close or a test trade (closed on stop or tp) and will need to add close trade
            if (Close == null)
            {
                Close = Order.Clone();
                Close.Action = TradeAction.CLOSE;
                Close.Timestamp = GetTimeStamp();
                Close.ItemId = _Trade.GetNewTradeId(); //set the client-assigned Id (could wait for the binance-assigned one instead) but this way, can query status even if no confirmation yet received
            }

            Close.Price = (double)price;
            Close.ItemStatus = TradeItemStatus.CLOSED;
            Close.GlobalStatus = TradeGlobalStatus.STOPPED;
            (_, Close.Balance) = GetTotalEquity();

            if (Entry.Price != 0) //if so Result will be infinity and db.SaveChanges() will throw exception
            {
                //so there is at least an entry trade here, so can close the trade and set the close price
                //if aborted, this is just a backtest which has hit an interruption, or a line trade which has not entered
                double price_pc = -MainObj.GetPriceDiff(Entry.Price, Close.Price, Close.Position) * 100;

                Close.Result = Entry.Result = AdjustResultForFees(price_pc); //set result on order for correct color display

                LogTrades.WriteLine(string.Format("Result: {0} {1:F2}%", Close.SymbolName, Close.Result));
            }         

            //change type to STOPPED for all the trades
            foreach (_Trade t in ActiveTrades)
            {
                t.GlobalStatus = TradeGlobalStatus.STOPPED;
                t.ItemStatus = TradeItemStatus.CLOSED;
                t.TradeExecutionStatus = TradeExecutionStatus.OK;
                SetUpdateRequest(t); //update everything (except close if that has just been created, it won't be in the list)
            }

            var line = ActiveLines.GetLineForTrade(Order);
            if (line != null)
            {
                line.TruncateLineToTime();
                line.Type = Entry != null ? LineType.CLOSED_ENTERED : LineType.CLOSED_UNENTERED;
                //detach from any line, so line can then be reused if it is desirable to leave the trade in place
                DetachTradeFromLine_UI_synchronised(Order);
            }

            SetUpdateRequest(Entry);
            SetUpdateRequest(Close);

            Order = null;

            Leverage = DEFAULT_LEVERAGE; //always set this as a default since it is an integer and 1 will be no use for a long anyway.
            MainObj.SetLeverageMenuState((int)Leverage); //NOTE, calling Application.Current.Dispatcher.Invoke() within this thread causes a lock condition    
        }

        protected void KillEntryOrCloseOrder(bool entry)
        {
            //kill order only applies to new entry being abandoned prior to submitting order request to exchange, or if it is rejected by exchange
            // - so only the Entry will actually have the script name, etc., but this saves a test for NULL on that if calling EndScript()
            foreach (var trade in ActiveTrades)
            {
                trade.ItemStatus = TradeItemStatus.CLOSED;
                trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                trade.TradeExecutionStatus = TradeExecutionStatus.OK;
                trade.ItemStatus = TradeItemStatus.CLOSED;
                trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                trade.TradeExchangeStatus = entry ? TradeExchangeStatus.NEVER_ENTERED : TradeExchangeStatus.NO_WARNING;
                SetUpdateRequest(trade);
            }
            if (Entry != null)
            {
                Entry.TradeExecutionStatus = TradeExecutionStatus.OK;
                Entry.ItemStatus = TradeItemStatus.CLOSED;
                Entry.GlobalStatus = TradeGlobalStatus.STOPPED;
                Entry.TradeExchangeStatus = entry ? TradeExchangeStatus.NEVER_ENTERED : TradeExchangeStatus.NO_WARNING;
            }
            if (Order != null)
            {
                Order.TradeExecutionStatus = TradeExecutionStatus.OK;
                Order.TradeExchangeStatus = entry ? TradeExchangeStatus.NEVER_ENTERED : TradeExchangeStatus.NO_WARNING;
                Order.ItemStatus = TradeItemStatus.CLOSED;
                Order.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            if (Stop != null)
            {
                Stop.TradeExecutionStatus = TradeExecutionStatus.OK;
                Stop.ItemStatus = TradeItemStatus.CLOSED;
                Stop.GlobalStatus = TradeGlobalStatus.STOPPED;
                Stop.TradeExchangeStatus = entry ? TradeExchangeStatus.NEVER_ENTERED : TradeExchangeStatus.NO_WARNING;
            }
        }

        #endregion

        #region Update live state from exchange 

        internal void InitLiveUpdateTimer()
        {
            //return; //because live state update uses InitForDataContext and this does not always work in an asynch loop
            if (timer == null)
            {
                try
                {
                    timer = new System.Timers.Timer(CHECK_EXCHANGE_INTERVAL_SECONDS * 1000)
                    {
                        Enabled = true
                    };
                    timer.Elapsed += new System.Timers.ElapsedEventHandler(Timer_Elapsed);

                }
                catch { }
            }
            else
            {
                if (timer.Enabled == false)
                {
                    timer.Start(); //won't hurt in case it stopped for some reason
                }
            }
        }

        protected void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            UpdateLiveState();
        }

        protected void ResetLiveUpdateTimer()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
            InitLiveUpdateTimer();
        }

        #endregion

        #region Leverage 

        //this uses a maximum stop loss to calculate the leverage, so that the maximum loss is always the same regardless of the stop position
        // but it clamps it to the existing leverage value so you don't go above that, only below
        // the existing leverage value therefore can be set before the order enters, then it will
        // scale down from that if the stop is too far away
        public double CalculateLeverageFromPercent(double stopPC, TradePosition position)
        {
            var minStop = GetValueForTimeframe(STOP_MIN_VALUE);
            var maxStop = GetValueForTimeframe(STOP_MAX_VALUE);
            var minLev = GetValueForTimeframe(MIN_LEVERAGE);
            var maxLev = GetValueForTimeframe(MAX_LEVERAGE);
            stopPC = Math.Abs(stopPC); //to be safe
            stopPC += STOP_LINE_PRICE_GAP_PC; //if a gap is set, add this in

            var stopDiff = maxStop - minStop;
            var levDiff = maxLev - minLev; 

            //could do this all on one line but this is easier to follow
            if (levDiff == 0)
            {
                return maxLev; //no range, so just return the max (or min) value
            }
            var stopScale = stopDiff / levDiff; //how much the stop changes per unit of leverage change
            var levReduction = (stopPC - minStop) / stopScale; //how much the leverage should be reduced from the max. value
            var leverage = maxLev - levReduction; //the leverage to use for this stop value

            return Math.Clamp(leverage, position == TradePosition.LONG ? MIN_ALLOWED_LEVERAGE_LONG : MIN_ALLOWED_LEVERAGE_SHORT, MAX_ALLOWED_LEVERAGE); 

            //This algorithm attempts to set the leverage so the maximum allowed loss is always the same.
            //For small initial stops, the leverage may be outside of the max. allowed leverage, so that will clamp the leverage and reduce the
            // effective maximum loss (i.e. to hit the max. allowed loss, a high leverage may be needed)
            //For large initial stops, the leverage may be too small, so it will be increased to the minimum - however, this will 
            // INCREASE the maximum potential loss.

            //With long positions, 1X leverage will merely track the rising value of the underlying asset, so no profit can be taken.
            //With short positions, 1X is fine as the sell will purchase the underlying asset at a higher level of equity than it was sold for
            //e.g. Long - market rises by 10%, XBT sold for $100, XBT bought for $110, but BTC value has gone up by 10% anyway
            //Short - market falls by 10%, XBT sold for $100, XBT bought for $110, so more XBT purchased than was originally sold
            //var leverage = MAX_STOP_LOSS / stopPC; //this is the default leverage here

            //if leverage is outside of min and max range, clamp it
            //leverage = Math.Max(Math.Min(leverage, MAX_LEVERAGE), minLeverage);

            //clamp max loss to a limit by limiting leverage, but keep leverage to the minimum value
            //don't want every loss (initially set) to be the same large default, it is very bothersome
            //but also don't want a trade to accidentally set a huge initial loss of, say, 3 * 8 = 24% + spread + fees
            //e.g. DEFAULT_LEVERAGE = 3, MAX_STOP_LOSS = 9
            //stop = 1, leverageScale = 1 * 3 / 9 = 0.333, leverage = 3 / 0.333 = 9
            //stop = 2, leverageScale = 2 * 3 / 9 = 0.667, leverage = 3 / 0.667 = 4.5
            //stop = 3, leverageScale = 3 * 3 / 9 = 1, leverage = 3 / 1 = 3
            //stop = 6, leverageScale = 6 * 3 / 9 = 2, leverage = 3 / 2 = 1.5
            //stop = 9, leverageScale = 9 * 3 / 9 = 3, leverage = 3 / 3 = 1            
            
            //var minLeverage = position == TradePosition.LONG ? MIN_LEVERAGE_LONG : MIN_LEVERAGE_SHORT;

            //var leverageScale = stopPC * Leverage / MAX_STOP_LOSS;
            //var leverage = Math.Min(Leverage / leverageScale, Leverage);


            #region Auto-calculate leverage (unused)
            /*
    * Leverage is scaled depending on the min and max stop levels, and the min and max allowable leverage
    * The max stop level is not necessarily the maximum actually used, but the max. which would generally be used. However, this value does not
    *  really affect the final result by very much when it is greater than 3%
    * Calculation:
    * Let stop range from Smin to Smax, and leverage from Lmin to Lmax
    * So the scale factor for the leverage is what the stop (S) can be divided into to get the leverage (L)
    * e.g. if the stop is 0.5% and the max leverage is 5, the scale (C) is 2.5, i.e. 2.5 / 0.5 = 5
    * So the scale factor is S * L
    * Scale will range from Cmin to Cmax: Cmin = Smin * Lmax, Cmax = Smax * Lmin
    *                                 * 
    * For a given stop value of X:
    * Let P be the ratio of the value of X to the range of S, so if Smin = 1, Smax = 2 and X = 1.5, P = 0.5 i.e it is half way between Smin and Smax
    * P = (X - Smin) / (Smax - Smin)
    * Calculate a value of C for this:
    * C = (Cmax - Cmin) * P + Cmin
    * Therefore the leverage L = C / X
    * 
    * Full formula: L = ((Cmax - Cmin) * (X - Smin) / (Smax - Smin) + Cmin) / X
    * 
    * */

            /* Examples:
             * For a min. stop of 0.7:
             * Stop     Leverage
             * 0.7      5
             * 1        3.8
             * 2        2.4
             * 3        2.0
             * 4        1.77
             * 5        1.6
             * 6        1.54
             * 
             * For a min. stop of 1.0
             * 1        5
             * 2        2.75
             * 3        2
             * 4        1.63
             * 5        1.4
             * 6        1.25

            /*
             * NOTE - if the stop percent is less than LEVERAGE_STOP_MIN_VALUE, the calculated leverage will be greater than MAX_LEVERAGE so it will be clamped
             * Also, the stop will then be reset to 
             * */
            /*
            double leverage = 2;
            var X = stopPC;
            var Smin = LEVERAGE_STOP_MIN_VALUE_1;
            var Smax = LEVERAGE_STOP_MAX_VALUE_1;
            var Lmin = MIN_LEVERAGE_1;
            var Lmax = MAX_LEVERAGE_1;
            var Cmin = Smin * Lmax;
            var Cmax = Smax * Lmin;
            if (Smax - Smin > 0)
            {
                var P = (X - Smin) / (Smax - Smin);
                var C = (Cmax - Cmin) * P + Cmin;
                leverage = C / X;
            }
            else
            {
                LogTrades.WriteLine($"Max and Min stop values are the same, cannot set leverage from stop. Using current Leverage of {Leverage}");
            }

            leverage = SetMinLeverage(leverage, position);

            LogTrades.WriteLine($"Setting leverage of {leverage}");

            try
            {
                //note, if leverage is < 0 this will fail with an exception, so leverage is always 1 or more.
                MainObj.SetLeverageMenuState((int)leverage); //NOTE, calling Application.Current.Dispatcher.Invoke() within this thread causes a lock condition
            }
            catch { }

            return leverage;
            */
            #endregion

        }

        //public static double CalculateStopFromLeverage(double leverage, TradePosition position)
        //{
        //    leverage = position == TradePosition.LONG
        //        ? Math.Max(leverage, MIN_LEVERAGE_LONG)
        //        : Math.Max(leverage, MIN_LEVERAGE_SHORT);

        //    var stop = MAX_STOP_LOSS / leverage; //this obviously cannot be greater than the target stop loss
        //    stop = Math.Max(stop, STOP_MIN_VALUE); //use this minimum stop value as a lower limit
        //    return stop;
        //}

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

        public double AdjustResultForFees(double result) { return result - FEE; }

        public _Trade GetTradeForTradeReference(TradeAction action, string reference = null)
        {
            var _reference = reference ?? currentTradeSetReference;
            return AllTrades.FirstOrDefault(x =>
                        x.Action == action &&
                        x.TradeReference == _reference);
        }

        public void ExtendLineToCurrentTime_synchronised(_Trade trade)
        {
            var line = ActiveLines.GetLineForTrade(trade);
            if (line != null)
            {
                if (line.EndTimeStamp < DateTime.UtcNow)
                {
                    (var price, _) = line.GetPriceAtTime(DateTime.UtcNow, true);
                    line.EndTimeStamp = DateTime.UtcNow;
                    line.EndPrice = price;
                    SetUpdateRequest(line);
                }
            }
        }

        public void AddLineToTrade_synchronised(_Trade trade, DateTime timeStamp)
        { 
            var line = new _ChartLine
            {
                StartTimeStamp = timeStamp,
                EndTimeStamp = MainObj.GetNextInterval_UI(timeStamp, currentDataItem),
                StartPrice = trade.Price,
                EndPrice = trade.Price,
                SymbolName = symbolName,
                Trade = trade,
                TradeReference = trade.TradeReference,
                Type = LineType.BREAK,
                TradeId = trade.Id
            };
            currentTradeSetReference = trade.TradeReference;
            SetUpdateRequest(line);
        }  

        protected void AdjustStopPriceToMinimum_Synchronised(_Trade entry, _Trade _floorStop, double minVal)
        {
            if (minVal > 0)
            {
                double stopPrice = _floorStop.Price;
                var line = ActiveLines.FirstOrDefault(x => x.Trade == _floorStop);
                if (line != null)
                {
                    (stopPrice, _) = line.GetPriceAtTime(_floorStop.Timestamp, true);
                }

                //ensure floor is not too close to entry price
                var high = GetHigh(entry);
                var v = MainObj.GetPriceDiff(high, stopPrice, _floorStop.Position) * 100; //the position may be different from Entry position, if this is a flip stop waiting
                if (v < minVal)
                {
                    LogTrades.WriteLine($"Stop too close to order price. Stop price was adjusted");

                    var prevPrice = stopPrice;

                    _floorStop.Price = MainObj.GetStopPriceFromPercent(entry.Price, minVal, _floorStop.Position);

                    _floorStop.Timestamp = GetTimeStamp();
                    SetUpdateRequest(_floorStop);

                    if (line != null)
                    {
                        var priceDiff = _floorStop.Price - prevPrice;

                        line.StartPrice += priceDiff;
                        line.EndPrice += priceDiff;
                        SetUpdateRequest(line);
                    }
                }
            }
        }

        private bool CheckForTimeout_Synchronised(_Trade trade)
        {
            bool ret = DateTime.UtcNow.Subtract(trade.Timestamp).TotalSeconds > (USE_SIMULATED_EXCHANGE ? 1000 : TIMEOUT_SECONDS);
            if (ret)
            {
                trade.Timestamp = DateTime.UtcNow; //or it will restart every loop until the issue is resolved
                SetUpdateRequest(trade);
            }
            return ret;
        }

        public DateTime GetTimeStamp()
        {
            return DateTime.UtcNow; //because currentdataitem is always at 59 secs so cannot be used for timeouts, etc.
        }

        protected Order GetCurrentTradeIndicator(_Trade trade)
        {
            if (trade == null)
            {
                return null;
            }
            switch (trade.Type)
            {
                case TradeType.HMA_BREAK_ORDER:
                    return hmaPriceBreakOrderIndicator;
                case TradeType.LINE_BREAK_ORDER:
                    return priceBreakOrderIndicator;
                //case TradeType.MACD_LINE_BREAK_ORDER:
                //    return macdOrderIndicator;
                // case TradeType.AUTO_ORDER:
                //     return autoOrderIndicator;
                default:
                    return priceBreakOrderIndicator;
            }
        }

        public double GetHigh(_Trade trade)
        {
            return !TradeEngine.USE_SIMULATED_EXCHANGE
                ? currentDataItem.Close
                : trade.Position == TradePosition.LONG ? currentDataItem.High : currentDataItem.Low;
        }

        public double GetLow(_Trade trade)
        {
            return !TradeEngine.USE_SIMULATED_EXCHANGE
                ? currentDataItem.Close
                : trade.Position == TradePosition.LONG ? currentDataItem.Low : currentDataItem.High;
        }

        public TradePosition GetOppositePosition(_Trade trade)
        {
            return GetOppositePosition(trade.Position);
        }
        
        public TradePosition GetOppositePosition(TradePosition position)
        {
            return position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
        }

        public void UpdateCurrentTradeSetReference(DataContext db)
        {
            var referenceActive = db.Trades.Any(x => x.TradeReference == currentTradeSetReference && x.GlobalStatus == TradeGlobalStatus.ACTIVE);
            if (!referenceActive)
            {
                var trades = db.Trades.Where(x => x.SymbolName == symbolName &&
                                                            x.Action == TradeAction.ORDER &&
                                                            x.GlobalStatus == TradeGlobalStatus.ACTIVE).ToList();
                //look for any running order, this will be the default one
                var order = trades.FirstOrDefault(x => x.ItemStatus == TradeItemStatus.CLOSED);
                if (order == null)
                {
                    //no running order, so look for any waiting order
                    order = trades.FirstOrDefault(x => x.ItemStatus == TradeItemStatus.OPEN);
                }
                currentTradeSetReference = order == null ? null : order.TradeReference;
            }
        }

        #endregion

        #region Draw

        public override void Draw(bool shallow)
        {
            //return;
            int startFrame = Chart.ScrollBars;
            int numFrames = Chart.ScaleBars;

            InitDraw();

            canvas = Chart.canvas;

            //not bothering to draw all of these
            //List<double> items_ema100 = hma_100?.GetResults(startFrame, numFrames, TimeFrame);

            #region set the scaling to exactly fill the panel

            Low = Chart.Low;
            High = Chart.High;

            SetScaling();

            #endregion

            double _yOffset = double.MaxValue;

#pragma warning disable CS8321 //disable warning for unused method
            void DrawEMAColored(List<double> items, int lineWidth, DrawingContext dc)
            {
                if (items == null || items.Count == 0)
                {
                    return;
                }

                //need the actual start frame in the visible timeframe, which is available from GetFrameCount()
                int frame = startFrame * MainObj.TimeFrame;

                for (int i = 0; i < items.Count - 1; i++) // emaLarge.GetResult(startFrame, numFrames))
                {
                    double startX = i;
                    double endX = i + 1;
                    double startY = items[i];
                    double endY = items[i + 1];

                    //get a suitable color
                    bool _isAbove60 = MovingAverage.IsAbove(ema_5, ema_10, frame, TimeFrame);

                    //SolidColorBrush color = _isAbove60 && _isRising60 ? colorUp : colorDown;
                    SolidColorBrush color = _isAbove60 ? Brushes.Blue : Brushes.Red;

                    //this method basically replicates PanelBase.DrawLineOverlayChart, which is in visible time scale.
                    frame += TimeFrame;

                    DrawLineSegment(startX, endX, ref startY, ref endY, lineWidth, color, dc);

                    //need to offset the drawing image down by the amount of emtpy space at the top 
                    //would also need to do this for left edge, probably, but this starts drawing from x == 0
                    _yOffset = _yOffset > startY ? startY : _yOffset;
                    _yOffset = _yOffset > endY ? endY : _yOffset;
                }

            }

#pragma warning disable CS8321
            void DrawEMA(List<double> items, int lineWidth, SolidColorBrush color, DrawingContext dc)
            {
                if (items == null || items.Count == 0)
                {
                    return;
                }

                //need the actual start frame in the visible timeframe, which is available from GetFrameCount()
                int frame = startFrame * TimeFrame;

                for (int i = 0; i < items.Count - 1; i++) // emaLarge.GetResult(startFrame, numFrames))
                {
                    double startX = i;
                    double endX = i + 1;
                    double startY = items[i];
                    double endY = items[i + 1];

                    frame += TimeFrame;

                    DrawLineSegment(startX, endX, ref startY, ref endY, lineWidth, color, dc);

                    //need to offset the drawing image down by the amount of emtpy space at the top 
                    //would also need to do this for left edge, probably, but this starts drawing from x == 0
                    //there does not seem to be any way to get the clipping region, this is just null in drawingImage.Source
                    _yOffset = _yOffset > startY ? startY : _yOffset;
                    _yOffset = _yOffset > endY ? endY : _yOffset;
                }
            }

            try
            {
                //todo - move to constructor
                var drawingImage = new System.Windows.Controls.Image();
                DrawingGroup drawingGroup = new DrawingGroup();
                using (DrawingContext dc = drawingGroup.Open())
                {
                    //DrawEMA(items_ema100, 2, Brushes.Thistle, dc);

                    drawingImage.Source = new DrawingImage(drawingGroup);
                    drawingImage.IsHitTestVisible = false; //without this, this will block mouse capture from the underlying chart
                    //canvas.Background = MainObj.invertChart ? Brushes.AntiqueWhite : Brushes.White;
                    //canvas.Background.Freeze();
                    Canvas.SetTop(drawingImage, !MainObj.invertChart ? _yOffset + YOffset : _yOffset - YOffset);
                    canvas.Children.Add(drawingImage);

                }
                //DrawEMAColored(items_ema5, 1);
            }
            catch { }
            //DrawEMA(items_ema200, 2, Brushes.LightSkyBlue);
        }

        #endregion
    }
}