using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;


namespace AlgoTrader.Shared
{
    public partial class TradeEngine : DisplayPanel
    {
        #region Constants

        public static ExchangeId EXCHANGE;


        // public static double MAX_LOSS_PC = 0;

        #endregion

        #region Fields

        //because the stop and take profit is a double exchange request, need to block this once it starts
        // - can't read from db to get exchange intent, as the system sets that first on a trade, then queries for entry
        // - and EF will return the object just changed, rather than pull the original data from the db if queried for that
        //NOTE - these are unused, this check must be done when running the exchange sequence, rather than in the UI loop, as the trade
        // may not be added or removed from the active order
        public _Trade pendingLiveEntryStop = null;
        public _Trade pendingLiveEntryTP = null;

        public const double TIMEOUT_SECONDS = 15; //timeout for exchange data

        #endregion

        #region Script names
        //script names - rather than enums - because these are stored in the db as strings so are easier to use when debugging
        internal const string QUERY_ORDER_SCRIPT = "Query Order"; //to update a trade with live server data
        internal const string NEW_ORDER_SCRIPT = "New Order";
        internal const string CLOSE_ORDER_SCRIPT = "Close Order";
        internal const string CANCEL_ORDER_SCRIPT = "Cancel Order"; //to cancel a stop, tp or floor (or OCO)
        internal const string CLOSED_ON_EXCHANGE = "Closed on exchange";
        internal const string EXCHANGE_NOT_SYNCHED = "Exchange not synched"; //this is just used if the exchange returns no open trades, it is just for drawing

        //binance - specific
        internal const string SET_STOP_SCRIPT = "Set Stop";
        internal const string CANCEL_STOP_SCRIPT = "Cancel Stop";
        internal const string UPDATE_LIVE_STATE_SCRIPT = "Update live state";
        #endregion

        //The entry point for all trades
        //This method is only called from a single point, which is within a synchronised block.
        //So it is vital that this does NOT start another synchronised section.
        public virtual void StartEntryOrClose_Synchronised() { }


        protected virtual void KillEntryOrCloseOrder(bool interrupted = false)
        {
            //kill order only applies to new entry being abandoned prior to submitting order request to exchange, or if it is rejected by exchange
            // - so only the Entry will actually have the script name, etc., but this saves a test for NULL on that if calling EndScript()
            foreach (var trade in ActiveTrades)
            {
                trade.ItemStatus = TradeItemStatus.CLOSED;
                trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                if (!interrupted)
                {
                    trade.ExchangeMethodScript = null;
                    trade.ItemStatus = TradeItemStatus.CLOSED;
                    trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                }
                SetUpdateRequest(trade);
            }
            if (Entry != null)
            {
                Entry.ExchangeMethodScript = null;
                Entry.ItemStatus = TradeItemStatus.CLOSED;
                Entry.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            if (Order != null)
            {
                Order.ExchangeMethodScript = null;
                Order.ItemStatus = TradeItemStatus.CLOSED;
                Order.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            if (Stop != null)
            {
                Stop.ExchangeMethodScript = null;
                Stop.ItemStatus = TradeItemStatus.CLOSED;
                Stop.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            //if (Floor != null)
            //{
            //    Floor.ExchangeMethodScript = null;
            //    Floor.ExchangeMethodIndex = 0;
            //    Floor.ItemStatus = TradeItemStatus.CLOSED;
            //    Floor.GlobalStatus = TradeGlobalStatus.STOPPED;
            //}
            //if (Ceiling != null)
            //{
            //    Ceiling.ExchangeMethodScript = null;
            //    Ceiling.ExchangeMethodIndex = 0;
            //    Ceiling.ItemStatus = TradeItemStatus.CLOSED;
            //    Ceiling.GlobalStatus = TradeGlobalStatus.STOPPED;
            //}
            //DequeueOrder_synchronised();
        }

        //ditto
        protected virtual void EndEntryOrCloseScript()
        {
            //immediate market orders placed through UI will not have set ActiveTrades (Bitmex) yet, so clear these first
            if (Order != null)
            {
                Order.ExchangeMethodScript = null;
                SetUpdateRequest(Order);
            }
            if (Entry != null)
            {
                Entry.ExchangeMethodScript = null;
                SetUpdateRequest(Entry);
            }
            foreach (var trade in ActiveTrades)
            {
                trade.ExchangeMethodScript = null;
                SetUpdateRequest(trade);
            }

            //DequeueOrder_synchronised();
        }

        protected bool isManualSynch = false;
        protected double previousStopPrice = 0;
        public virtual void SetStopOnExchange_Synchronised()
        {
            ResetLiveUpdateTimer();
            Stop.Timestamp = DateTime.UtcNow;
            SetUpdateRequest(Stop); //to prevent timeout from triggering
        }
        protected virtual bool RemoveStopFromExchange_Synchronised() { return false; }

        //get account balances - this is for display only, it is not part of trade placement
        internal virtual void ShowAccountBalances() { }

        #region Update live state from exchange 

        System.Timers.Timer timer = null;
        public const double CHECK_EXCHANGE_INTERVAL_SECONDS = 15;
        public void InitLiveUpdateTimer()
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

        public void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            UpdateLiveState();
        }

        public void ResetLiveUpdateTimer()
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

        protected virtual void UpdateLiveState() { }


    }
}