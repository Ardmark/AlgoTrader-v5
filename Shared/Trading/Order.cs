using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.TradeIndicators;
using AlgoTrader.Shared.Windows;


namespace AlgoTrader.Shared
{
    /*
     * Base class for all Order system objects (market and auto)
     * */
    public abstract partial class Order : DisplayPanel
    {
        #region Fields
        protected bool isInLongTrade = false;
        protected bool isInShortTrade = false;

        #endregion

        #region Properties
        //public Trade ActiveOrder
        //{
        //    get
        //    {
        //        return tradeEngine.activeOrder;
        //    }
        //    set
        //    {
        //        tradeEngine.activeOrder = value;
        //    }
        //}
        #endregion

        #region Init
        public Order(string symbolName, int timeInterval, TradeEngine tradeEngine, Chart chart) : base(symbolName, timeInterval, tradeEngine, chart)
        {
        }

        public override void Init(int timeFrame = 0)
        {
            base.Init(timeFrame);
        }

        public override void InitIndicators(int timeFrame)
        {
            if (!timeIntervalFixed)
            {
                TimeFrame = timeFrame;
            }
        }

        public override void InitTimeFrame(int timeFrame)
        {
            if (!timeIntervalFixed)
            {
                TimeFrame = timeFrame;
            }
            //same as display panel method but these trade indicators are different, and interfaces mess things up
            /*
                        foreach (var indicator in indicators)
                        {
                            indicator.SetData();
                        }
                        foreach (var indicator in indicators)
                        {
                            foreach (var item in tradeEngine.results)
                            {
                                indicator.ItemAdded(item);
                            }
                        }
                        */
        }

        #endregion

        #region Add / update item
        public override void ItemAdded(_SymbolDataItem item, bool canComputeTradeEntry, bool liveItem)
        {
            if (liveItem)
            {
                ItemUpdated(item, true);
            }
        }

        #endregion

        #region Set order (UI)

        internal virtual _Trade SetOrder_UI_synchronised(int timeFrame, TradeType tradeType, TradePosition tradePosition, string symbolName, bool isBacktest, double price, int frameNum = -1, int lineId = -1)
        {

            _Trade ret = null;

            //get the frame to place the market order. If this position is outside of the chart bounds, set it on the last candle
            //only get frame if not already supplied, as for line break order. Otherwise, will be regular break order so use last frame position (unless backtest)
            if (frameNum == -1)
            {
                frameNum = Chart.GetFrameNumFromLastMousePos();
            }

            double orderPrice = -1;

            switch (tradeType)
            {
                case TradeType.PRICE_BREAK_ORDER:
                case TradeType.LINE_BREAK_ORDER:

                    orderPrice = price == 0 ? Chart.GetTradePriceFromLastMouseY() : price;
                    if (!isBacktest)
                    {
                        //if (tradePosition == TradePosition.LONG && orderPrice <= tradeEngine.currentDataItem.Close)
                        //{
                        //    AlertDialog.Show("Cannot set Long break order below current price", "Not possible");
                        //    return null;
                        //}
                        //else if (tradePosition == TradePosition.SHORT && orderPrice >= tradeEngine.currentDataItem.Close)
                        //{
                        //    AlertDialog.Show("Cannot set Short break order above current price", "Not possible");
                        //    return null;
                        //}
                    }
                    break;

                case TradeType.HMA_BREAK_ORDER:

                    var hmaIndicator = MainObj.GetPanelForSymbol(chart.symbolName, typeof(HMA_Indicator));
                    if (hmaIndicator != null)
                    {
                        orderPrice = !isBacktest ? hmaIndicator.GetLastPrice(TimeFrame) : hmaIndicator.GetLastPrice(TimeFrame, frameNum);
                        if (orderPrice == -1)
                        {
                            AlertDialog.Show("No price returned from HMA indicator", "Not possible");
                            return null;
                        }
                        if (orderPrice <= tradeEngine.currentDataItem.Close)
                        {
                            AlertDialog.Show("Cannot set HMA break order below current price", "Not possible");
                            return null;
                        }
                    }
                    break;
            }

            if (orderPrice == -1)
            {
                return null;
            }

            try
            {
                DateTime timeStamp = isBacktest || frameNum != -1 ? Chart.GetTimestampFromFrameNum(frameNum) : DateTime.UtcNow;

                ret = new _Trade
                {
                    GlobalStatus = isBacktest ? TradeGlobalStatus.STOPPED : TradeGlobalStatus.ACTIVE,
                    ItemStatus = isBacktest ? TradeItemStatus.CLOSED : TradeItemStatus.OPEN,
                    Timestamp = timeStamp,
                    TradeReference = _Trade.GetNewTradeId(),
                    Type = tradeType,
                    Position = tradePosition,
                    Action = TradeAction.ORDER,
                    Price = orderPrice,
                    ActionValue = 0,
                    TimeFrame = TimeFrame,
                    SymbolName = symbolName,
                    TradeExchangeStatus = TradeExchangeStatus.ENTERED_OFF_EXCHANGE
                };

            }
#pragma warning disable CS0168
            catch (Exception e)
            {
                return null;
            }

            return ret;
        }

        #endregion

        #region Enter trade
        public virtual bool CanEnterTrade_synchronised(_Trade order) { return false; }

        //anything which needs to be done outside of the stop or take profit handlers
        internal virtual bool ExecuteEntry_synchronised(bool itemAdded)
        {
            //may not be a stop auto-added, could be a floor instead, so test for spike here
            return TestForMoveToBE_synchronised(itemAdded);
        }

        #endregion

        #region Utility methods
        protected (double, double) GetPrevCurrPrices(_Trade order)
        {
            var item = tradeEngine.currentDataItem;

            //the Close of the current data item will come directly from the exchange, but there may have been a higher high between this and the prior sent data item
            //so must check the high (and low)
            double currItemPrice = tradeEngine.currentDataItem.Close;// GetMaxValue(order.Position);
            var prevItemPrice = tradeEngine.Results.Last().GetMinValue(order.Position);

            return (prevItemPrice, currItemPrice);
        }

        public static double GetBreakEvenPrice(_Trade trade, double? percent = null)
        {
            //calculate the break-even price, as a small percentage beyond the trade entry
            double beVal = 1 + (percent == null ? TradeEngine.BREAK_EVEN_THRESHOLD / 100 : (double)percent);
            return trade.Position == TradePosition.LONG
                ? trade.Price * beVal
                : trade.Price / beVal; //needs to be smaller with smaller timeframes
        }

        protected bool PriceAboveThreshold(TradePosition position, double price, double thresholdPrice)
        {
            return position == TradePosition.LONG
                ? price > thresholdPrice
                : price < thresholdPrice;
        }

        #endregion

        #region Stops

        public static void DoTrail(_Trade stop)
        {
            //trail max. value is High if backtesting, otherwise market close
            var limitPrice = MainObj.visibleTradeEngine.GetHigh(stop);

            //move the stop price which will still be the same ActionValue percentage below the current close
            //the action value can be changed manually at any point, to move the trade from weak to strong
            double newPrice = stop.Position == TradePosition.LONG
                ? limitPrice * (1 - stop.ActionValue)
                : limitPrice * (1 + stop.ActionValue);

            //update the trail price and set time as current -
            stop.Price = newPrice;
        }

        internal virtual (bool, bool) ExecuteStop_synchronised(bool itemAdded)
        {
            _Trade activeOrder = tradeEngine.Order;
            _Trade entry = tradeEngine.Entry;
            _Trade stop = tradeEngine.Stop;
            List<_ChartLine> lines = tradeEngine.ActiveLines;
            _SymbolDataItem currentDataItem = tradeEngine.currentDataItem;
            int currentFrame = tradeEngine.currentFrame;

            double stopPrice;
            _ChartLine line;

            if (currentFrame > Results.Count)
            {
                return (false, false);
            }
            if (stop.TradeExecutionStatus != TradeExecutionStatus.OK && stop.TradeExecutionStatus != TradeExecutionStatus.EXCHANGE_NOT_SYNCHED)
            {
                return (false, false); //do nothing with it at this point
            }

            stop.Timestamp = currentDataItem.Timestamp;

            var low = tradeEngine.GetLow(stop);
            var high = tradeEngine.GetHigh(stop);

            //var breakEvenPrice = GetBreakEvenPrice(entry);

            /*
             * NOTE - all these stops (except the trails) will NOT execute except on timeframe close, including price adjustments on-screen
             * */
            var oldPrice = stop.Price;
            switch (stop.Stop)
            {
                #region STOP_ON_LINE_BREAK

                case TradeStop.STOP_ON_LINE_BREAK:

                    stopPrice = 0;
                    line = lines.GetLineForTrade(stop);
                    if (line != null)
                    {
                        (stopPrice, _) = line.GetPriceAtTime(currentDataItem.Timestamp, true);
                    }
                    else
                    {
                        stopPrice = stop.Price;
                    }


                    var gap = TradeEngine.STOP_LINE_PRICE_GAP_PC / 100;
                    stopPrice = stop.Position == TradePosition.LONG
                        ? stopPrice / (1 + gap)
                        : stopPrice * (1 + gap);

                    stop.Price = stopPrice;
                    tradeEngine.SetUpdateRequest(stop);

                    //check if stop price is crossed now price is set from line
                    // - in a falling wedge, stop may be initially above entry until after first execution, so must do this after stop moved
                    if (entry.Position == TradePosition.LONG
                                    ? low <= stopPrice
                                    : low >= stopPrice)
                    {
                        return (true, true);
                    }


                    break;
                #endregion

                #region TAKE_PROFIT_MA_CROSS
                case TradeStop.TAKE_PROFIT_MA_CROSS:

                    //get the moving average
                    var MA_Indicator = MainObj.displayPanelsForSymbol[symbolName].FirstOrDefault(x => x.GetType() == typeof(SMA_Indicator));
                    if (MA_Indicator != null)
                    {
                        //only one moving average
                        var movingAverage = MA_Indicator.movingAverages[0];
                        var results = movingAverage.GetResultsFromDictionary(stop.TimeFrame);
                        var lastResult = results[results.Count - 1];
                        stop.Price = lastResult;
                        tradeEngine.SetUpdateRequest(stop);


                        if (entry.Position == TradePosition.LONG
                           ? low <= lastResult
                           : low >= lastResult)
                        {
                            //only trigger stop if the stop price is not the wrong side of the trade, since this is a take profit only
                            bool ret = ((entry.Position == TradePosition.LONG)
                                        ? lastResult > entry.Price
                                        : lastResult < entry.Price);
                            if (ret)
                            {
                                LogTrades.WriteLine("Exiting on MA take profit");
                            }
                            return (ret) ? (true, true) : (false, true);
                        }
                    }
                    //return (true, true);
                    break;
                #endregion              

                #region STOP_TRAIL

                case TradeStop.STOP_TRAIL:

                    void TrailStop()
                    {
                        //if (TradeEngine.STOP_TRAIL > 0)
                        //{

                            //adjust the stop price - the action value (percentage) can be set below the initial stop price, so only adjust price if the difference
                            // between current price and stop price is greater than action value percentage
                            double diff = MainObj.GetPriceDiffPercent(currentDataItem, stop.Price, stop.Position);
                            if (diff > stop.ActionValue)
                            {
                                DoTrail(stop);
                                tradeEngine.SetUpdateRequest(stop);
                            }
                       // }
                    }

                    switch (stop.Stop)
                    {
                        case TradeStop.STOP_TRAIL:
                            //trail the stop (always do this, never switch to another stop)
                            TrailStop();
                            break;
                    }

                    //check if stop price is crossed
                    if (entry.Position == TradePosition.LONG
                                    ? low <= stop.Price
                                    : low >= stop.Price)
                    {
                        return (true, true);
                    }

                    return (false, true);

                    #endregion
            }

            return (false, oldPrice != stop.Price);
        }


        #endregion




        internal bool TestForMoveToBE_synchronised(bool itemAdded)
        {
            return false; //this is not unused as trailing stops are too risky. Spike is also disabled here may need to re-enable that later

            #region Init
            _Trade SetFloorStop_synchronised(DateTime timeStamp, double price, TradeAction action)
            {
                int currentFrame = tradeEngine.currentFrame;
                var trade = tradeEngine.Order.Clone();
                trade.Action = action;
                trade.Timestamp = timeStamp;
                trade.Price = price;
                trade.ActionValue = 0;
                trade.ItemId = null; //or it will be the order ItemId
                trade.TradeExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;
                trade.Quantity = 0; //this will be calculated in the next step
                tradeEngine.SetUpdateRequest(trade);
                tradeEngine.SetStopOnExchange_Synchronised();
                return trade;
            }

            if (tradeEngine.Entry == null)
            {
                return false;
            }

            var entry = tradeEngine.Entry;
            var activeOrder = tradeEngine.Order;
            var redrawRequired = false;
            var stop = tradeEngine.Stop;
            _Trade breakEven = null;// tradeEngine.BreakEven;
            var takeProfit = tradeEngine.TakeProfit;
            var currentFrame = tradeEngine.currentFrame;
            var currTimestamp = tradeEngine.currentDataItem.Timestamp;
            var currClose = tradeEngine.currentDataItem.Close;

            #endregion

            //////////////////////////////////////////////////////////////////////////////////
            ///CHANGES MADE TO SPIKE AND TRAILING STOP, DO NOT RE-ENABLE WITHOUT TESTING FIRST
            //////////////////////////////////////////////////////////////////////////////////
            ///
            #region Trailing stop (unimplemented)

            //if any stop or floor is below the break even position, do nothing.
            //otherwise, set a trailing stop if that is below the lowest stop or floor
            //So the stop will trail down but won't override a lower stop (i.e. it won't be added), but when it is the lowest stop it will be added.
            //But if there is a stop in a profit position, it won't be added, so replacing a trailing stop with any other stop will remove it.


            //if (TradeEngine.AUTO_SET_TRAILING_STOP) //use this as the trail value for now
            //{

            //    //if there is a stop or floor in profit, do nothing
            //    var breakEvenPrice = GetBreakEvenPrice(entry);
            //    bool canSet = true;
            //    if (stop != null)
            //    {
            //        if (stop.Position == TradePosition.LONG ? stop.Price > breakEvenPrice : stop.Price < breakEvenPrice)
            //        {
            //            canSet = false;
            //        }
            //    }
            //    else if (floor != null)
            //    {
            //        if (floor.Position == TradePosition.LONG ? floor.Price > breakEvenPrice : floor.Price < breakEvenPrice)
            //        {
            //            canSet = false;
            //        }
            //    }

            //    if (canSet)
            //    {
            //        var pc = TradeEngine.STOP_TRAIL / 100;
            //        var currPrice = tradeEngine.currentDataItem.Close;
            //        var trailValue = entry.Position == TradePosition.LONG ? currPrice * (1 - pc) : currPrice * (1 + pc);


            //        //so there is either:
            //        //no existing stop or floor
            //        //any stop or floor is not better than the trail value
            //        //any stop or floor is not in profit

            //        canSet = false;
            //        if (stop == null)
            //        {
            //            stop = SetFloorStop_synchronised(DateTime.UtcNow, trailValue, TradeAction.STOP);
            //            stop.Quantity = -entry.Quantity;
            //            canSet = true;
            //        }
            //        else
            //        {
            //            if (stop.Type != TradeType.FLIP_ORDER)
            //            {
            //                //a flip order, which will often be placed, cannot be used as a trailing stop as it is set at a specific price level
            //                //If a trailing stop is required, then the flip would be changed to a floor or line break stop, and the trail would then execute separately.

            //                //detach stop from any line
            //                var line = tradeEngine.ActiveLines.GetLineForTrade(stop);
            //                if (line != null)
            //                {
            //                    tradeEngine.RemoveTradeFromLine_UI_synchronised(stop);
            //                    tradeEngine.SetUpdateRequest(line);
            //                }
            //            }
            //            else
            //            {
            //                canSet = true;
            //            }
            //        }
            //        if (canSet)
            //        {
            //            stop.Stop = TradeStop.STOP_TRAIL;
            //            stop.ActionValue = TradeEngine.STOP_TRAIL / 100;
            //            tradeEngine.SetUpdateRequest(stop);
            //        }
            //    }
            #endregion

            #region Auto-set MA stop (unused but keep)
            //if (stop == null)
            //{
            //    stop = SetFloorStop_synchronised(currTimestamp, takeProfitPrice, TradeAction.STOP);
            //    stop.Stop = TradeStop.TAKE_PROFIT_MA_CROSS;
            //}
            //else
            //{
            //    //if stop set below b/e, move it - but don't move if above!!
            //    if (activeOrder.Position == TradePosition.LONG ? stop.Price < breakEvenPrice : stop.Price > breakEvenPrice)
            //    {
            //        var chartLine = tradeEngine.ActiveLines.GetLineForTrade(stop);
            //        if (chartLine != null)
            //        {
            //            chartLine.TradeId = null;
            //            chartLine.TradeReference = null;

            //            chartLine.Type = LineType.NONE;
            //            tradeEngine.SetUpdateRequest(chartLine);
            //        }
            //        stop.Price = takeProfitPrice;
            //        stop.Stop = TradeStop.TAKE_PROFIT_MA_CROSS;
            //        tradeEngine.SetUpdateRequest(stop);
            //    }
            //} 
            #endregion

            #region Move to break even when some bars are clear

            if (TradeEngine.NUM_BARS_FOR_FLOOR > 0)
            {
                if (itemAdded) //only on bar close
                {
                    if (entry.Stop != TradeStop.FLOOR_SET_TO_PREV_BAR)
                    {
                        if (TradeEngine.results.Count % entry.TimeFrame == 0)
                        {

                            //only do this test once. If the trend moves away, set a floor.
                            entry.Stop = TradeStop.FLOOR_SET_TO_PREV_BAR;
                            tradeEngine.SetUpdateRequest(entry);
                            var numBarsBack = entry.TimeFrame * TradeEngine.NUM_BARS_FOR_FLOOR;
                            var startBar = currentFrame - numBarsBack;
                            var _pos = entry.Position;
                            var low = _pos == TradePosition.LONG ? double.MaxValue : double.MinValue;

                            if (tradeEngine.results.Count >= currentFrame)
                            {
                                for (int i = startBar; i < currentFrame; i++)
                                {
                                    var resultLow = _pos == TradePosition.LONG ? tradeEngine.results[i].Low : tradeEngine.results[i].High;

                                    if (_pos == TradePosition.LONG && resultLow < low || _pos == TradePosition.SHORT && resultLow > low)
                                    {
                                        low = resultLow;
                                    }
                                }

                                //don't set floor if it is too close, must have a minimum of 0.5%
                                var minFloor = 0.005; //1/2 %
                                var actualFloor = MainObj.GetPriceDiff(tradeEngine.currentDataItem.Close, low, _pos);
                                if (actualFloor < 0)
                                {
                                    LogTrades.WriteLine("2 bar floor not placed as floor wrong side of price");
                                }
                                else
                                {
                                    if (actualFloor < minFloor)
                                    {
                                        LogTrades.WriteLine($"Setting {TradeEngine.NUM_BARS_FOR_FLOOR}-bar floor at minimum value of {minFloor * 100}%");
                                        actualFloor = minFloor;
                                    }
                                    else
                                    {
                                        LogTrades.WriteLine($"Setting {TradeEngine.NUM_BARS_FOR_FLOOR}-bar floor at {actualFloor}%");
                                    }
                                    //just invert the method to get the percent
                                    var price = _pos == TradePosition.LONG ? (1 - actualFloor) * tradeEngine.currentDataItem.Close : tradeEngine.currentDataItem.Close / (1 - actualFloor);

                                    //set floor at price
                                    var startTimestamp = tradeEngine.Results[startBar].Timestamp;
                                    var endTimestamp = startTimestamp.AddMinutes(tradeEngine.TimeFrame * 4);

                                    var line = new _ChartLine
                                    {
                                        SymbolName = symbolName,
                                        TradeReference = entry.TradeReference,
                                        StartTimeStamp = startTimestamp,
                                        EndTimeStamp = endTimestamp,
                                        StartPrice = price,
                                        EndPrice = price,
                                        Type = LineType.MARKER
                                    };

                                    line.InitLine(false);
                                    tradeEngine.SetUpdateRequest(line);

                                    //TODO - uncomment this to set floor, it is tested

                                    //if (floor == null)
                                    //{
                                    //    SetFloorStop_synchronised(currTimestamp, price, TradeAction.FLOOR);
                                    //}
                                    //else
                                    //{
                                    //    if (_pos == TradePosition.LONG && floor.Price < price || (_pos == TradePosition.SHORT && floor.Price > price))
                                    //    {
                                    //        floor.Price = price;
                                    //        tradeEngine.SetUpdateRequest(floor);
                                    //    }
                                    //}
                                    redrawRequired = true;
                                }
                            }
                        }
                    }
                    #region old break even code (unused)
                    //var minBreakEven = entry.Price * 1.005;
                    //if (minBreakEven < low)
                    //{
                    //    if (floor == null)
                    //    {
                    //        SetFloorStop_synchronised(currTimestamp, minBreakEven, TradeAction.FLOOR);
                    //        LogTrades.WriteLine($"Setting dead break even floor for {entry.SymbolName}");
                    //    }
                    //    else
                    //    {
                    //        if (floor.Price < low && floor.Price < minBreakEven)
                    //        {
                    //            floor.Price = minBreakEven;
                    //            tradeEngine.SetUpdateRequest(floor);//);
                    //            LogTrades.WriteLine($"Moving to dead break even floor for {entry.SymbolName}");
                    //        }
                    //    }
                    //}
                    #endregion
                }

            }
            #endregion

            #region Move to break even when profit reaches certain level

            /*
            if (TradeEngine.BREAK_EVEN_PERCENT > 0)
            {
                var entryPrice = entry.Price;
                var pos = entry.Position;
                var high = pos == TradePosition.LONG
                   ? entryPrice * (1 + TradeEngine.BREAK_EVEN_PERCENT / 100)
                   : entryPrice / (1 + TradeEngine.BREAK_EVEN_PERCENT / 100);

                if ((pos == TradePosition.LONG && tradeEngine.currentDataItem.Close > high) || (pos == TradePosition.SHORT && tradeEngine.currentDataItem.Close < high))
                {

                        if ((pos == TradePosition.LONG && floor.Price < entryPrice) || (pos == TradePosition.SHORT && floor.Price > entryPrice))
                        {
                            floor.Price = entryPrice;
                            tradeEngine.SetUpdateRequest(floor);
                        }
                    
                }
            }
            */
            #endregion

            #region Spike
            /*

            if (TradeEngine.SPIKE_TRIGGER_PERCENT > 0)
            {
                //if trade has risen by an arbitrary amount within an arbitrary time, set a floor half way within this time period
                //e.g. if trade rises 10% from price x in 1 hour, set floor at half way from x to current price

                //get elapsed time since entry
                var elapsedTime = currTimestamp.Subtract(entry.Timestamp).TotalMinutes;

                //if (elapsedTime > tradeEngine.SPIKE_TRIGGER_FRAMES)
                //{

                int numFrames = elapsedTime < TradeEngine.SPIKE_TRIGGER_FRAMES ? (int)elapsedTime : TradeEngine.SPIKE_TRIGGER_FRAMES;
                if (numFrames > 0 && currentFrame - numFrames > 0)
                {
                    var prevPrice = Results[currentFrame - numFrames];
                    var pcDiff = -MainObj.GetPriceDiff(prevPrice.Close, currClose, entry.Position) * 100;

                    if (pcDiff >= TradeEngine.SPIKE_TRIGGER_PERCENT)
                    {
                        if (!MainObj.IsBacktesting && !tradeEngine.hasSpiked)
                        {
                            MainObj.PlaySound(MainObj.SPIKE_SOUND);
                            LogTrades.WriteLine($"Spike alert for {symbolName} at {DateTime.UtcNow}");
                            tradeEngine.hasSpiked = true; //just the once, this is set to false when a trade enters (only one point of entry on system)
                        }

                        //will only set a new stop if in profit, not climbing out of a loss, as that would still exit in loss (has happened)
                        if (entry.Position == TradePosition.LONG && currClose > entry.Price || entry.Position == TradePosition.SHORT && currClose < entry.Price)
                        {
                            if (!MainObj.IsBacktesting)
                            {
                                var posText = entry.Position == TradePosition.LONG ? "Long" : "Short";
                                LogTrades.WriteLineOrder($"Spike triggered for {posText} {symbolName} at {DateTime.UtcNow}");
                            }
                            var stopPrice = activeOrder.Position == TradePosition.LONG
                                ? (currClose + prevPrice.Close) * (tradeEngine.SPIKE_PERCENT / 100)
                                : (currClose + prevPrice.Close) / (tradeEngine.SPIKE_PERCENT / 100);

                            //if no floor, add one at b/e
                            if (floor == null)
                            {
                                SetFloorStop_synchronised(currTimestamp, stopPrice, TradeAction.FLOOR);
                            }
                            else
                            {
                                //if floor set below b/e, move it - but don't move if above!!
                                if (activeOrder.Position == TradePosition.LONG
                                    ? floor.Price < stopPrice
                                    : floor.Price > stopPrice)
                                {
                                    floor.Price = stopPrice;
                                    floor.Timestamp = tradeEngine.GetTimeStamp();
                                    tradeEngine.SetUpdateRequest(floor);
                                }
                            }

                            //remove any take profit and break even, these may conflict with a spike
                            if (takeProfit != null)
                            {
                                tradeEngine.SetUpdateRequest(takeProfit, TradeUpdateRequest.DELETE);
                            }
                            if (breakEven != null)
                            {
                                tradeEngine.SetUpdateRequest(breakEven, TradeUpdateRequest.DELETE);
                            }
                            redrawRequired = true;
                        }
                    }
                }
                // }
            } 
            
             */
            #endregion

            return redrawRequired;
        }
    }
}

