using AlgoTrader.Shared.Enums;

namespace AlgoTrader.Shared
{
    public partial class TradeEngine : DisplayPanel
    {
        #region Constants

        public static ExchangeId EXCHANGE;
        public const double TIMEOUT_SECONDS = 15; //timeout for exchange data
        public const double CHECK_EXCHANGE_INTERVAL_SECONDS = 15;

        #endregion

        #region Fields

        System.Timers.Timer timer = null;
        protected bool isManualSynch = false;

        #endregion

        #region Abstract methods
        protected abstract void StartEntry_Synchronised();
        protected abstract void StartClose_Synchronised(_Trade triggeringTrade);
        internal abstract void ShowAccountBalances();
        protected abstract void UpdateLiveState();
        #endregion

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
                    trade.TradeExecutionStatus = TradeExecutionStatus.OK;
                    trade.ItemStatus = TradeItemStatus.CLOSED;
                    trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                }
                SetUpdateRequest(trade);
            }
            if (Entry != null)
            {
                Entry.TradeExecutionStatus = TradeExecutionStatus.OK;
                Entry.ItemStatus = TradeItemStatus.CLOSED;
                Entry.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            if (Order != null)
            {
                Order.TradeExecutionStatus = TradeExecutionStatus.OK;
                Order.ItemStatus = TradeItemStatus.CLOSED;
                Order.GlobalStatus = TradeGlobalStatus.STOPPED;
            }
            if (Stop != null)
            {
                Stop.TradeExecutionStatus = TradeExecutionStatus.OK;
                Stop.ItemStatus = TradeItemStatus.CLOSED;
                Stop.GlobalStatus = TradeGlobalStatus.STOPPED;
            }

            //DequeueOrder_synchronised();
        }

        protected virtual void EndEntryOrCloseScript()
        {
            //immediate market orders placed through UI will not have set ActiveTrades (Bitmex) yet, so clear these first
            if (Order != null)
            {
                Order.TradeExecutionStatus = TradeExecutionStatus.OK;
                SetUpdateRequest(Order);
            }
            if (Entry != null)
            {
                Entry.TradeExecutionStatus = TradeExecutionStatus.OK;
                SetUpdateRequest(Entry);
            }
            foreach (var trade in ActiveTrades)
            {
                trade.TradeExecutionStatus = TradeExecutionStatus.OK;
                SetUpdateRequest(trade);
            }
        }

        internal virtual void SetStopOnExchange_Synchronised()
        {
            ResetLiveUpdateTimer();
            Stop.Timestamp = DateTime.UtcNow;
            SetUpdateRequest(Stop); //to prevent timeout from triggering
        }
        
        protected virtual bool RemoveStopFromExchange_Synchronised() { return false; }

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
    }
}