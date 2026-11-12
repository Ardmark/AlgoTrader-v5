using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;
using System;
using System.Diagnostics;
using System.Windows.Interop;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace AlgoTrader.Shared
{
    /*
     * All methods only called from mainViewModel, in response to UI input
     * */
    public partial class TradeEngine : DisplayPanel
    {
        #region MainViewModel-invoked methods

        //there is an unused method in MainObj to kill all orders on all trade engines simultaneously,
        // this will just remove the orders on the current trade engine

        internal void KillOrders_UI_Synchronised()
        {
            CancelTrade_UI_synchronised();
            MainObj.TradeStatusChanged(this);
        }

        internal void MoveTrades_UI_Synchronised(bool up)
        {
            InitForDataContext(() =>
            {
                void MoveTrade(_Trade trade, bool _up, double _pc)
                {
                    if (trade != null)
                    {
                        double __pc = _pc;

                        var line = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == trade.Id);
                        /*
                        We want to move any unentered order line by the percentage relative to whatever angle the line is at, not just vertically
                        But this is very difficult to do if you want to move the x and y coordinates, since they use different coordinate systems - 
                         e.g. y could be in the dollar price range but x could be in ticks
                        So these need to be converted to the same coordinate system, i.e. in screen coordinates - but these must be as if (0,0)) is at zero dollars and zero time, which they are obviously not
                        Too difficult to bother with. So will just move vertically.
                        However, if a trade is entered, then stops and floors (which will always move vertically) will move at different rates, 
                         depending on stop line angles, which is very confusing. So will only move vertically by percentage for entered trades.
                        With an asc or desc order line, there will typically be no floor anyway, as that is useless, only stop lines matching the order line angle.
                        So putting all of this together: unenetered orders - move according to line angle, but entered orders move vertically, and floors move vertically regardless of order line angle.
                        */
                        if (line != null && trade.Action == TradeAction.ORDER && trade.ItemStatus == TradeItemStatus.OPEN)
                        {
                            (double X1, double Y1) = chart.GetDrawPosition1(line.StartTimeStamp, line.StartPrice);
                            (double X2, double Y2) = chart.GetDrawPosition1(line.EndTimeStamp, line.EndPrice);

                            var opp = X2 - X1;

                            var adj = Y2 - Y1;
                            var hyp = Math.Sqrt(opp * opp + adj * adj);
                            var sinTheta = opp / hyp;
                            var cosTheta = adj / hyp;
                            if (sinTheta < 0.1) //don't want the price move too much or could go way off screen
                            {
                                sinTheta = 0.1;
                            }
                            __pc = (__pc - 1) / sinTheta + 1;
                        }

                        __pc = trade.Position == TradePosition.LONG ? __pc : 1 / __pc;

                        if (line != null)
                        {
                            line.StartPrice = _up ? line.StartPrice * __pc : line.StartPrice / __pc;
                            line.EndPrice = _up ? line.EndPrice * __pc : line.EndPrice / __pc;
                            SetUpdateRequest(line);
                        }

                        //stop will eventually track line but want immediate update:
                        trade.Price = _up ? trade.Price * __pc : trade.Price / __pc;
                    }
                }

                var pc = MOVE_STOPS_AMT / 100 + 1;

                if (Entry != null && Entry.ItemStatus == TradeItemStatus.OPEN)
                {
                    if (Stop != null && Stop.Stop == TradeStop.STOP_ON_LINE_BREAK)
                    {
                        MoveTrade(Stop, up, pc);
                        MoveTrade(TakeProfit, !up, pc);
                        SetStopOnExchange_Synchronised();
                    }
                }

                //move any stopped trades
                var activeTrades = AllTrades.Where(x => x.GlobalStatus == TradeGlobalStatus.STOPPED);

                foreach (var trade in activeTrades)
                {
                    var close = AllTrades.FirstOrDefault(x => x.TradeReference == trade.TradeReference && x.Action == TradeAction.CLOSE);
                    if (close == null)
                    {
                        //if there is no close, then the trade was stopped before it entered, so can move the stop and take profit levels
                        MoveTrade(trade, !up, pc);
                    }
                }
            });
            MainObj.DrawImmediate();
        }

        //internal void RemoveStoppedTrades_UI_Synchronised(_Trade trade)
        //{
        //    InitForDataContext(() =>
        //    { 
        //        var tradeToDelete = AllTrades.FirstOrDefault(x => x.Id == trade.Id);
        //        if (tradeToDelete != null)
        //        {
        //            RemoveTradeFromSystem_UI_synchronised(tradeToDelete, false);
        //            SetUpdateRequest(tradeToDelete, TradeUpdateRequest.DELETE);
        //        }
        //    });
        //}

        internal void CleanupTrades_UI_Synchronised()
        {
            InitForDataContext(() =>
            {
                foreach (var trade in AllTrades.Where(x => x.Action == TradeAction.ORDER && x.GlobalStatus == TradeGlobalStatus.STOPPED))
                {
                    foreach (var _trade in AllTrades.Where(x => x.TradeReference == trade.TradeReference))
                    {
                        if (_trade.Action == TradeAction.ORDER)
                        {
                            var line = GetLinesForTradeReference(_trade.TradeReference).FirstOrDefault();
                            if (line != null)
                            {
                                SetUpdateRequest(line, TradeUpdateRequest.DELETE);
                            }
                        }
                        if (
                            _trade.TradeExchangeStatus == TradeExchangeStatus.NEVER_ENTERED ||
                            (_trade.Action != TradeAction.ORDER && _trade.Action != TradeAction.ENTRY && _trade.Action != TradeAction.CLOSE)
                            )
                        {
                            var line = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == _trade.Id);
                            if (line != null)
                            {
                                line.TradeId = null;
                                line.StopId = null;
                                SetUpdateRequest(line);
                            }
                            SetUpdateRequest(_trade, TradeUpdateRequest.DELETE);
                        }
                    }
                }
            });

            MainObj.DrawImmediate();
        }

        internal void CleanupLines_UI_Synchronised()
        {
            InitForDataContext(() =>
            {
                var linesWithoutTrades = DataContext.Lines.Where(x =>
                                               x.SymbolName == symbolName &&
                                               x.TradeId == null && x.StopId == null
                                               ).ToList();
                var linesWithTrades = DataContext.Lines.Where(x =>
                                               x.SymbolName == symbolName &&
                                               (x.TradeId != null || x.StopId != null)
                                               ).ToList();
                //foreach (var line in linesWithTrades)
                //{
                //    //this will leave stop lines, since the stops would also need to be removed first
                //    if (!DataContext.Trades.Any(x => x.Id == line.TradeId || x.Id == line.StopId))
                //    {
                //        linesWithoutTrades.Add(line);
                //    }
                //}
                foreach (var line in linesWithoutTrades)
                {
                    SetUpdateRequest(line, TradeUpdateRequest.DELETE);
                }
            });

            MainObj.DrawImmediate();
        }

        internal void SetStopAtPrice_UI_synchronised(DateTime timeStamp, double price, TradeStop tradeStop)
        {
            SetStop_UI(timeStamp, price, TradeAction.STOP, /*TradeType.NO_FLIP_ORDER, */tradeStop);
        }

        internal void SetTakeProfitAtPrice_UI_synchronised(DateTime timeStamp, double price)
        {
            SetStop_UI(timeStamp, price, TradeAction.TAKE_PROFIT/*, TradeType.NO_FLIP_ORDER*/);
        }

        internal void SetMarketOrderAtTime_UI_synchronised(DateTime timeStamp, double price, TradePosition position)
        {
            Task.Run(() =>
            {
                InitForDataContext(() =>
                {
                    if (Order != null)
                    {
                        AlertDialog.Show("Cannot set 2 active trades to run concurrently on same symbol", "Not possible");
                        return;
                    }

                    //must have at least 24 hours of data, or trade can be placed before first midnight boundary so will never be visible unless it closes the following day
                    if (results.Count < 24 * MainObj.INITIAL_TIMEFRAME)
                    {
                        AlertDialog.Show("Could not set market break order, needs 24 bars data", "Insufficient data");
                        return;
                    }

                    var order = new _Trade
                    {
                        Timestamp = timeStamp,
                        TradeReference = _Trade.GetNewTradeId(),
                        Type = TradeType.MARKET_ORDER,
                        Position = position,
                        Action = TradeAction.ORDER,
                        Price = price,
                        TimeFrame = Chart.TimeFrame,
                        SymbolName = symbolName,
                    };

                    SetUpdateRequest(order);

                    currentTradeSetReference = order.TradeReference;

                    var currentTradeIndicator = GetCurrentTradeIndicator(order);

                    //when a trade is changed, added or deleted, mark it with a TradeUpdateRequest and add to the trades list
                    (bool hasEntered, bool tradeKilled) = ExecuteTradeEntryIfPossible(order, null);
                    if (tradeKilled)
                    {
                        order.ItemStatus = TradeItemStatus.CLOSED;
                        order.GlobalStatus = TradeGlobalStatus.STOPPED;
                    }
                    if (hasEntered)
                    {
                        Order = order;
                    }
                });

                MainObj.TradeStatusChanged(this);
            });
        }

        //sets a break order on a new horizontal line at a specific price
        internal void SetLineBreakOrder_UI_synchronised(DateTime timeStamp, double price, TradePosition position, bool addClosed = false, bool isBacktest = false)
        {
            if (currentDataItem == null)
            {
                AlertDialog.Show("Waiting for connection", "Not possible");
                return;
            }

            InitForDataContext(() =>
            {
                var nextEarliest = MainObj.GetNextInterval_UI(DateTime.UtcNow.AddMinutes(-1), currentDataItem);
                var line = new _ChartLine
                {
                    StartTimeStamp = timeStamp,
                    EndTimeStamp = nextEarliest,
                    StartPrice = price,
                    EndPrice = price,
                    SymbolName = symbolName
                };

                SetUpdateRequest(line);

                _Trade trade = SetLineOrder_UI_synchronised(position, isBacktest, line, addClosed);
            });

            MainObj.TradeStatusChanged(this);
        }

        internal void EditTradeSet_UI(_Trade trade)
        {
            currentTradeSetReference = trade.TradeReference;
        }

        internal void CloseTrade_UI_synchronised(int tradeId = 0)
        {
            CancelTrade_UI_synchronised(tradeId);
            MainObj.TradeStatusChanged(this);
        }

        internal void MarkAsSynched_UI_synchronised(int tradeId = 0)
        {
            InitForDataContext(() =>
            {
                var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);
                if (trade != null)
                {
                    var trades = AllTrades.Where(x => x.TradeReference == trade.TradeReference);
                    foreach (var t in trades)
                    {
                        t.TradeExecutionStatus = TradeExecutionStatus.OK;
                        SetUpdateRequest(t);
                        tradeEngine.UpdateUIList(t);
                    }
                    MainObj.DrawCurrentDispatcher(true);
                }
            });
        }

        internal void RemoveTradeAndLine_UI_synchronised(int tradeId)
        {
            InitForDataContext(() =>
            {
                var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);
                if (trade == null)
                {
                    AlertDialog.Show("Could not find trade.");
                    return;
                }

                //this method is only called from the pop-up menu, so it will not be a forced delete
                //therefore block it on all live trades
                if ((trade.ItemStatus == TradeItemStatus.OPEN || trade.GlobalStatus == TradeGlobalStatus.ACTIVE) && trade.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                {
                    AlertDialog.Show("Cannot delete a live trade!");
                    return;
                }

                RemoveTradeFromSystem_UI_synchronised(trade, true);
                MainObj.TradeStatusChanged(this);

                trade = AllTrades.FirstOrDefault(x => x.Id != tradeId);
                if (trade != null)
                {
                    currentTradeSetReference = trade.TradeReference;
                    MainObj.DrawImmediate();
                }

            });

        }

        internal void RemoveTrade_UI_synchronised(int tradeId, bool allowForceRemove = false)
        {

            InitForDataContext(() =>
            {
                var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);

                if (trade == null)
                {
                    AlertDialog.Show("Could not find trade");
                    return;
                }

                if (!allowForceRemove && (trade.ItemStatus == TradeItemStatus.OPEN || trade.GlobalStatus == TradeGlobalStatus.ACTIVE) && trade.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                {
                    AlertDialog.Show("Cannot delete a live trade!");
                    return;
                }
                RemoveTradeFromSystem_UI_synchronised(trade, false, allowForceRemove);
                MainObj.TradeStatusChanged(this);

                trade = AllTrades.FirstOrDefault(x => x.Id != tradeId);
                if (trade != null)
                {
                    currentTradeSetReference = trade.TradeReference;
                    MainObj.DrawImmediate();
                }

            });

        }

        internal void DeleteLine_UI_synchronised(int chartLineId)
        {
            InitForDataContext(() =>
            {
                //need all the lines here
                try
                {
                    var chartLine = AllLinesForSymbol.FirstOrDefault(x => x.Id == chartLineId);

                    if (chartLine != null)
                    {
                        _Trade trade = chartLine.Trade;
                        if (trade != null && trade.GlobalStatus == TradeGlobalStatus.ACTIVE)
                        {
                            AlertDialog.Show("Cannot delete a line when active trade is attached");
                            return;
                        }
                        SetUpdateRequest(chartLine, TradeUpdateRequest.DELETE);
                    }

                }
                catch
                {

                }
            });

            MainObj.TradeStatusChanged(this);
        }

        internal void RestartTrade_UI_synchronised(int tradeId)
        {
            //restart a trade which has been closed but not yet entered, and extend any line to the next interval (+24 hours)
            InitForDataContext(() =>
            {
                //get the trade from the current data context
                var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);

                //Order will be null at this point, must use trade
                if (trade == null)
                {
                    AlertDialog.Show("Could not find trade");
                    return;
                }

                var entry = AllTrades.FirstOrDefault(x =>
                    x.Action == TradeAction.ENTRY &&
                    x.TradeReference == trade.TradeReference &&
                    x.GlobalStatus == TradeGlobalStatus.ACTIVE);
                if (entry != null)
                {
                    AlertDialog.Show("Cannot restart a trade which has already entered");
                    return;
                }
                var allTrades = AllTrades.Where(x => x.TradeReference == trade.TradeReference);
                foreach (var t in allTrades)
                {
                    t.ItemStatus = TradeItemStatus.OPEN;
                    t.GlobalStatus = TradeGlobalStatus.ACTIVE;
                    SetUpdateRequest(t);

                    if (t.Action == TradeAction.CLOSE)
                    {
                        SetUpdateRequest(t, TradeUpdateRequest.DELETE);
                    }
                }

                //extend line - not actually necessary as this will be done on first run anyway
                var line = AllLinesForSymbol.GetLineForTrade(trade);
                if (line != null)
                {
                    if (line.EndTimeStamp < DateTime.Now.AddDays(1))
                    {
                        line.Extend(DateTime.UtcNow.AddDays(2), TimeFrame);
                    }
                    SetUpdateRequest(line);
                }
                else
                {
                    line = new _ChartLine
                    {
                        StartTimeStamp = trade.Timestamp,
                        EndTimeStamp = trade.Timestamp.AddDays(1),
                        StartPrice = trade.Price,
                        EndPrice = trade.Price,
                        SymbolName = symbolName,
                        Trade = trade,
                        //TradeReference = trade.TradeReference,
                        Type = LineType.BREAK
                    };

                    SetUpdateRequest(line);

                }
            });

            MainObj.TradeStatusChanged(this);
        }

        //this sets both stops and take profits
        private void SetStop_UI(DateTime timeStamp, double price, TradeAction tradeAction, TradeStop tradeStop = TradeStop.STOP_ON_LINE_BREAK, _ChartLine chartLine = null)
        {
            //var isWaitingStop = tradeAction == TradeAction.WAITING_STOP;

            var order = UI_Trades.GetTradeForTradeReference(TradeAction.ORDER, currentTradeSetReference);

            if (order == null)
            {
                AlertDialog.Show("No trade matches current trade set reference", "Could not set trade");
                return;
            }

            if (order.GlobalStatus == TradeGlobalStatus.STOPPED)
            {
                AlertDialog.Show("Could not set trade", "Order is closed");
                return;
            }

            //ensure stop is below (or above) current price
            var entry = UI_Trades.GetTradeForTradeReference(TradeAction.ENTRY, currentTradeSetReference);

            var orderLine = UI_Lines.GetLineForTrade(order);

            void SetStop(TradeAction _tradeAction)
            {
                InitForDataContext(() =>
                {
                    if (chartLine != null)
                    {
                        //get the line for the current data context, the chartLine parameter will not track in EF
                        chartLine = AllLinesForSymbol.FirstOrDefault(x => x.Id == chartLine.Id);

                        if (chartLine.TradeId != null || chartLine.StopId != null)
                        {
                            return;
                        }
                    }
                    ////////////////////////
                    //Set the stop
                    //if this is called from an entered trade, there will be a stop. Otherwise, we need to set one

                    //if this is a trailing stop, need the action value
                    double actionValue = tradeStop == TradeStop.STOP_TRAIL ? price / 100 : 0; //because the price is the percent here

                    //this could be an existing line break stop
                    _Trade trade = GetTradeForTradeReference(_tradeAction);
                    if (trade != null)
                    {
                        //just update the existing stop
                        var priceDiff = price - trade.Price;
                        trade.Price = price;
                        var line = AllLinesForSymbol.FirstOrDefault(x => x.StopId == trade.Id);
                        if (line != null)
                        {
                            line.StartPrice += priceDiff;
                            line.EndPrice += priceDiff;
                            SetUpdateRequest(line);
                        }

                        SetUpdateRequest(trade);
                        return;
                    }

                    //new stop
                    //add a new stop / take profit, and line
                    double _price = price;
                    if (chartLine != null)
                    {
                        (_price, _) = chartLine.GetPriceAtTime(timeStamp, true);
                    }

                    trade = order.Clone();


                    //although precision is set when OCO is sent to exchange, it helps to keep the number of decimal places down anyway.
                    //_price = GetCorrectPrecisionForPrice(_price);
                    trade.Price = _price;
                    trade.Action = tradeAction;
                    trade.ItemId = null; //need a new ID for this as it is a new order.;
                    trade.ItemStatus = TradeItemStatus.OPEN;
                    trade.Stop = tradeStop;
                    trade.ActionValue = actionValue;
                    trade.Timestamp = timeStamp;
                    trade.TimeFrame = TimeFrame;

                    SetUpdateRequestAdd_UI(trade);

                    if (trade.Action == TradeAction.STOP || trade.Action == TradeAction.TAKE_PROFIT)
                    {
                        if (chartLine == null)
                        {
                            AddLineToTrade_synchronised(trade, order.Timestamp);
                        }
                        else
                        {
                            chartLine.StopId = trade.Id;
                            SetUpdateRequest(chartLine);
                        }
                    }

                    if (Stop == null)
                    {
                        //stop has been force-removed in error, so replace it
                        Stop = trade;
                        if (TakeProfit != null)
                        {
                            TakeProfit.Result = 0;
                        }
                    }

                    tradeEngine.UpdateUIList(trade);
                    tradeEngine.UpdateUIList(chartLine);
                });


                MainObj.DrawCurrentDispatcher(true);
                MainObj.TradeStatusChanged(this);
            }

            (bool isClose, bool isWrongSide) = CanSetStop(price, orderLine, chartLine, order.Position, tradeAction, entry != null);// CanSetTrade(chartLine, orderLine, price, tradeAction, entry, order.Position);

            bool canSetStop = true;
            if (isWrongSide)
            {
                AlertDialog.Show("Requested position is wrong side of Order");
                canSetStop = false;
            }
            else if (isClose)
            {
                canSetStop = false;
                //need to block setting on wrong side of current price entirely to be safe
                AlertDialog.ShowCallback($"Requested position is too close to Order and may trigger immediate exit.\nDo you wish to procede? ", $"Terminating trade position may not be valid", (_) =>
                    canSetStop = true
                );
            }

            if (canSetStop)
            {
                SetStop(tradeAction);
            }
        }

        internal void AddStopToLine_UI(int chartLineId)
        {
            var chartLine = UI_Lines.FirstOrDefault(x => x.Id == chartLineId);
            if (chartLine.Trade != null || chartLine.Stop != null)
            {
                AlertDialog.Show("Trade already set on this line");
                return;
            }

            var order = UI_Trades.GetTradeForTradeReference(TradeAction.ORDER, currentTradeSetReference);

            if (order == null)
            {
                AlertDialog.Show("No trade matches current trade set reference", "Could not set trade");
                return;
            }

            if (order.GlobalStatus == TradeGlobalStatus.STOPPED)
            {
                AlertDialog.Show("Could not set trade", "Order is closed");
                return;
            }

            var timeStamp = order.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE ? DateTime.Now : order.Timestamp;
            (var price, var isWithinLine) = chartLine.GetPriceAtTime(timeStamp, true);

            SetStop_UI(timeStamp, price, TradeAction.STOP, TradeStop.STOP_ON_LINE_BREAK, chartLine);
        }

        internal void AddTakeProfitToLine_UI(int chartLineId)
        {
            var chartLine = UI_Lines.FirstOrDefault(x => x.Id == chartLineId);
            if (chartLine.Trade != null || chartLine.Stop != null)
            {
                AlertDialog.Show("Trade already set on this line");
                return;
            }

            var order = UI_Trades.GetTradeForTradeReference(TradeAction.ORDER, currentTradeSetReference);

            if (order == null)
            {
                AlertDialog.Show("No trade matches current trade set reference", "Could not set trade");
                return;
            }
            if (order.GlobalStatus == TradeGlobalStatus.STOPPED)
            {
                AlertDialog.Show("Could not set trade", "Order is closed");
                return;
            }
            var timeStamp = order.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE ? DateTime.Now : order.Timestamp;

            (var price, var isWithinLine) = chartLine.GetPriceAtTime(timeStamp, true);
            SetStop_UI(order.Timestamp, price, TradeAction.TAKE_PROFIT, 0, chartLine);
        }

        internal void AddBreakToLine_UI_synchronised(int chartLineId, TradePosition position)
        {
            InitForDataContext(() =>
            {
                var chartLine = AllLinesForSymbol.FirstOrDefault(x => x.Id == chartLineId);

                if (chartLine.Trade != null)
                {
                    AlertDialog.Show("Trade already set on this line");
                    return;
                }

                SetLineOrder_UI_synchronised(position, false, chartLine);

            });

            MainObj.TradeStatusChanged(this);
        }

        public void ChangePrice_UI_Synchronised(_Trade _trade, double price)
        {
            AlertDialog.Show("Unimplemented, needs to be changed to allow reversed position for waiting stop");
            return;

            /*void ChangePrice()
            {
                InitForDataContext(() =>
                {
                    var trade = AllTrades.FirstOrDefault(x => x.Id == _trade.Id);
                    if (trade != null)
                    {
                        trade.Price = price;
                        tradeEngine.SetUpdateRequest(trade);

                        var chartLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == trade.Id);

                        if (chartLine != null)
                        {
                            //get the line for the current data context, the chartLine parameter will not track in EF
                            chartLine.StartPrice = chartLine.EndPrice = price;
                            tradeEngine.SetUpdateRequest(chartLine);
                        }
                        MainObj.DrawCurrentDispatcher();
                        MainObj.TradeStatusChanged(this);
                    }
                });
            }
            */
        }

        public void ChangeTimeframe_UI_Synchronised(_Trade _trade, int timeframe)
        {
            InitForDataContext(() =>
            {
                var trades = AllTrades.Where(x => x.TradeReference == _trade.TradeReference);
                foreach (var trade in trades)
                {
                    trade.TimeFrame = timeframe;
                    tradeEngine.SetUpdateRequest(trade);
                }

                MainObj.DrawCurrentDispatcher();
                MainObj.TradeStatusChanged(this);
            });
        }

        #endregion

        #region MainObj - called methods (on batches of trades)

        internal void CancelTrade_UI_synchronised(int tradeId = 0)
        {
            Task.Run(() =>
            {
                InitForDataContext(() =>
                {
                    List<_Trade> tradesToClose = null;

                    if (tradeId != 0)
                    {
                        var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);
                        if (trade != null)
                        {
                            tradesToClose =
                            [
                                //just the single order to close
                                GetTradeForTradeReference(TradeAction.ORDER, trade.TradeReference)
                            ];
                        }
                    }
                    else
                    {
                        //if no tradeId specified, close all open orders
                        tradesToClose = [.. AllTrades.Where(x => x.Action == TradeAction.ORDER)];
                    }

                    foreach (var order in tradesToClose)
                    {
                        if (order.GlobalStatus == TradeGlobalStatus.ACTIVE)
                        {
                            if (order.TradeExecutionStatus != TradeExecutionStatus.OK)
                            {
                                var s = USE_SIMULATED_EXCHANGE ? "Simulated exchange in use so trade must be force-removed (shift click) as it may not timeout" : " Please wait for timeout, then check Exchange and remove trade..";
                                AlertDialog.Show($"Order for {order.SymbolName} has a pending Exchange Method, so this may be an interrupted trade. {s}.");
                                return;
                            }

                            if (order.ItemStatus == TradeItemStatus.OPEN)
                            {
                                CloseUnenteredTrade_UI_synchronised(order.TradeReference);
                            }
                            else
                            {
                                //order has entered
                                _Trade oppositeOrder = USE_TESTNET ? GetOppositeOrder(order) : null;
                                CloseTradeOnExchange(oppositeOrder); //cannot do a flip from a manually closed trade unless testing


                                //no, even if it is local it still should close on the exchange
                                //if (order.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE) //don't bother to do this with backtests
                                //{
                                //    CloseTradeLive_synchronised(false);
                                //}
                                //else
                                //{
                                //    CloseTradeLive_synchronised(false);
                                //   CloseUnenteredTrade_UI_synchronised(order.TradeReference);
                                //}
                            }
                        }
                        else
                        {
                            #region Force-close interrupted trade
                            //this would happen if a trade has been interrupted, in which case everything is closed so there will be no active trades or order to close
                            //but the trade still needs to be closed on the system or it cannot be deleted
                            if (order.TradeExecutionStatus != TradeExecutionStatus.OK)
                            {
                                var trades = GetTradesForTradeReference(order.TradeReference);

                                foreach (var trade in trades)
                                {
                                    trade.TradeExecutionStatus = TradeExecutionStatus.OK;//or will continue to show as interrupted
                                    SetUpdateRequest(trade);

                                    if (trade.Action == TradeAction.CLOSE) //or will show at the bottom
                                    {
                                        var entry = GetTradeForTradeReference(TradeAction.ENTRY, order.TradeReference);
                                        if (entry != null)
                                        {
                                            trade.Price = entry.Price;
                                        }
                                    }
                                    if (trade.Action == TradeAction.ENTRY)
                                    {
                                        SetUpdateRequest(trade, TradeUpdateRequest.DELETE);
                                    }
                                }

                                AlertDialog.Show($"{order.SymbolName} had an Interrupted trade so exchange status is indeterminate. Check exchange!!!!");
                            }
                            #endregion
                        }
                    }
                });
            });
        }

        internal void StopWaitingTrades_UI_synchronised()
        {
            InitForDataContext(() =>
            {
                foreach (var order in WaitingOrders)
                {
                    CloseUnenteredTrade_UI_synchronised(order.TradeReference, false);
                }
            });
        }

        #endregion

        private bool RemoveTradeFromSystem_UI_synchronised(_Trade trade, bool removeLine, bool allowForceRemove = false)
        {
            var tradesToRemove = AllTrades.Where(x => x.TradeReference == trade.TradeReference);
            var tradeToRemove = tradesToRemove.FirstOrDefault(x => x.Id == trade.Id);

            if (tradeToRemove != null)
            {
                //can be called on any trade, not just the order
                switch (tradeToRemove.Action)
                {
                    //if this is to delete the whole trade set, must be closed already
                    case TradeAction.ORDER:
                    case TradeAction.ENTRY:
                    case TradeAction.CLOSE:

                        //if this is a locally entered trade, can remove it without closing on the exchange
                        foreach (var t in tradesToRemove)
                        {
                            DetachTradeFromLine_UI_synchronised(t, removeLine);
                            SetUpdateRequest(t, TradeUpdateRequest.DELETE);
                        }
                        break;


                    case TradeAction.STOP:
                    case TradeAction.TAKE_PROFIT:

                        DetachTradeFromLine_UI_synchronised(tradeToRemove, removeLine);
                        SetUpdateRequest(tradeToRemove, TradeUpdateRequest.DELETE);

                        break;

                }
            }

            return true;
        }

        internal _Trade SetLineOrder_UI_synchronised(TradePosition position, bool isBacktest, _ChartLine line, bool addClosed = false)
        {
            if (currentDataItem != null)
            {
                var frameNum = chart.GetFrameForTimestamp(line.StartTimeStamp);

                (var price, _) = line.GetPriceAtTime(currentDataItem.Timestamp, true);

                bool canSet = isBacktest == true || (position == TradePosition.LONG && price > currentDataItem.Close || position == TradePosition.SHORT && price < currentDataItem.Close);

                if (!canSet)
                {
                    var m = position == TradePosition.LONG ? "below" : "above";
                    AlertDialog.Show($"Warning ", $"Requested position is {m} current price, ensure break position is valid");
                }

                if (addClosed)
                {
                    isBacktest = true; //this will just set the order as STOPPED + CLOSED
                }
                if (isBacktest)
                {
                    addClosed = true;
                }
                var order = priceBreakOrderIndicator.SetOrder_UI_synchronised(TimeFrame, TradeType.LINE_BREAK_ORDER, position, symbolName, isBacktest, line.StartPrice, frameNum, line.Id);

                if (order != null)
                {
                    line.Trade = order;
                    //line.TradeReference = order.TradeReference;
                    line.Type = LineType.BREAK;
                    SetUpdateRequest(order);
                    SetUpdateRequest(line);
                    currentTradeSetReference = order.TradeReference;
                }

                return order;
            }
            else
            {
                AlertDialog.Show($"Waiting for connection... ", "Cannot set trade");
            }
            return null;
        }

        internal void CloseUnenteredTrade_UI_synchronised(string tradeReference, bool removeTrades = false)
        {
            var trades = GetTradesForTradeReference(tradeReference);

            foreach (var trade in trades)
            {
                trade.GlobalStatus = TradeGlobalStatus.STOPPED;
                trade.ItemStatus = TradeItemStatus.CLOSED;
                trade.TradeExchangeStatus = TradeExchangeStatus.NEVER_ENTERED; //keep for reference
                SetUpdateRequest(trade, removeTrades ? TradeUpdateRequest.DELETE : TradeUpdateRequest.UPDATE);
            }

            //detach from any line, so line can then be reused if it is desirable to leave the trade in place
            // - no, otherwise restarting will not have an attached line
            //RemoveTradeFromLine_UI_synchronised(trades.FirstOrDefault(x => x.Action == TradeAction.ORDER));

            //clear the stops as they are no longer relevant anyway
            // however, since these are unentered trades, leave them in in case trade is to be restarted later
            // - but stops will still be deleted when system closes a trade
            //var stops = trades.Where(x => x.Action != TradeAction.ENTRY && x.Action != TradeAction.CLOSE && x.Action != TradeAction.ORDER);
            //foreach (var stop in stops)
            //{
            //    RemoveTradeFromLine_UI_synchronised(stop); //or will throw exception on saving
            //    SetUpdateRequest(stop, TradeUpdateRequest.DELETE);
            //}

            var lines = GetLinesForTradeReference(tradeReference);
            foreach (var line in lines)
            {
                //line.TruncateLineToTime();
                line.Type = LineType.CLOSED_UNENTERED;
                SetUpdateRequest(line);
            }
        }

        internal void DetachTradeFromLine_UI_synchronised(_Trade trade, bool removeLine = false, bool activeTradesOnly = false)
        {
            if (trade != null)
            {
                //an optimization if only active trades considered
                var line = activeTradesOnly ? ActiveLines.GetLineForTrade(trade) : AllLinesForSymbol.GetLineForTrade(trade);
                if (line != null)
                {
                    line.Trade = null;
                    line.TradeId = null; //must do this, not just set the trade to null!
                    line.StopId = null;
                    //line.TradeReference = null;
                    line.Type = LineType.NONE;
                    SetUpdateRequest(line);
                    //remove any tiny lines completely

                    var timeSpan = line.EndTimeStamp.Subtract(line.StartTimeStamp).TotalMinutes;
                    var tinyLine = timeSpan < MainObj.visibleChart.TimeFrame * 2;
                    if (tinyLine || removeLine)
                    {
                        SetUpdateRequest(line, TradeUpdateRequest.DELETE);
                    }
                }
            }
        }

        #region Get results

        internal double lastCandleCountdown;
        //GetResults methods also called in Backtesting

        //this will return results based on current (display) timeframe, which will be set when the display switches to another symbol / timeframe
        public (List<_SymbolDataItem>, List<_SymbolDataItem>) GetResults_UI(int firstRequestedFrame, int numFrames, int timeFrame, bool align = true)
        {
            framesetResults.Clear();

            if (timeFrame == 0 || results.Count == 0)
            {
                return (null, null);
            }

            int startFrame = firstRequestedFrame;

            //line up with time interval (missed / extra frames will mess this up)
            if (align)
            {
                for (; firstRequestedFrame < results.Count; firstRequestedFrame++)
                {
                    var t = results[firstRequestedFrame].Timestamp;
                    if (t.Hour == START_HOUR)
                    {
                        break;
                    }
                }
            }

            //will have skipped some bars to get to the start bar, so when scrolling right for example, will be moving back down the data list
            // but will continue to skip bars until the timeframes line up, thus causing the scroll to move in big increments
            // - so need to display bars prior to current skip position
            int numSkipped = (firstRequestedFrame - startFrame) / timeFrame;
            firstRequestedFrame -= numSkipped * timeFrame;

            //switching could interrupt this process and there would not be enough results for the requested frame
            if (firstRequestedFrame > results.Count - 1)
            {
                firstRequestedFrame = results.Count - 1;
            }

            //since this is aggregating over a number of frames, the last frame must allow for aggregation
            int lastFrame = firstRequestedFrame + numFrames;
            if (lastFrame > results.Count)
            {
                lastFrame = results.Count;
            }

            var firstTime = results[firstRequestedFrame].Timestamp;

            timeFrameAggregator.Init(timeFrame);

            int lastAggregatedFrame = 0;

            while (firstRequestedFrame < lastFrame) //i is incremented in the loop below, lastFrame is always at least 1 less than results.Count
            {
                bool starting = true;
                while (true)
                {
                    var nextItem = results[firstRequestedFrame];
                    if (starting)
                    {
                        //due to some frames having a partial minute timestamp, and some getting duplicated due to interuptions,
                        //the intial timestamp can be a minute or so out.
                        //The very first item will be on a time boundary, so this will only affect the timestamps after interrupted items
                        // on the current screen. But it does show times incorrectly when hovering.
                        //Solution is just to reset the first timestamp
                        starting = false;
                        nextItem.Timestamp = firstTime;
                    }

                    _SymbolDataItem ret = timeFrameAggregator.AggregateSingleFrame(nextItem);
                    firstRequestedFrame++;
                    if (firstRequestedFrame == results.Count)
                    {
                        break;
                    }
                    if (ret != null)
                    {
                        framesetResults.Add(ret);
                        firstTime = firstTime.AddMinutes(timeFrame);
                        lastAggregatedFrame = firstRequestedFrame;
                        break;
                    }
                }
            }

            //has completed aggregation of all candles, so get the partial time left before next aggregation loop
            // - 1 minute can be ahead of current time, by 1 or 2 mins for some reason
            while (firstTime > DateTime.UtcNow)
            {
                firstTime = firstTime.AddMinutes(-1);
            }

            lastCandleCountdown = TimeFrame * 60 - DateTime.UtcNow.Subtract(firstTime).TotalSeconds;

            List<_SymbolDataItem> currentCandleItems = new List<_SymbolDataItem>();
            for (int i = lastAggregatedFrame; i < firstRequestedFrame; i++)
            {
                currentCandleItems.Add(results[i]);
            }

            return (framesetResults, currentCandleItems);
        }

        public (List<_SymbolDataItem>, List<_SymbolDataItem>) GetResultsHA_UI(int firstRequestedFrame, int numFrames, int timeFrame, bool align = true)
        {
            /*
                Close = 0.25 * (Open+High+Low+Close)           (The average price of the current bar)
                Open  = 0.5  * (Open of Prev. Bar+Close of Prev. Bar)          (The midpoint of the previous bar)
                High=Max[High, Open, Close]
                Low=Min[Low, Open, Close]
​	        */

            /*
             * So this does not tally with TradingView, as that uses the previous Heikin Ashi bar, and the current candle, for the calculation
             * Therefore, would need to set up a 1-min HA results set as well as the standard one, which kind of implies that instead of a single 1-min
             * data set with aggregation on-the-fly, a better approach would be multiple results sets.
             * Will leave for now.
             * */
            try
            {
                List<_SymbolDataItem> ret = new List<_SymbolDataItem>();
                (var items, var currentCandleItems) = GetResults_UI(firstRequestedFrame, numFrames, timeFrame, align);
                if (items != null && items.Count > 0)
                {
                    for (int i = 1; i < items.Count; i++)
                    {

                        var item = items[i];

                        //Weird - some items have a Open value of zero, this does not show up in the chart, but will wreck the HA display
                        // so backtrack and use the prior good item
                        if (item.Open == 0)
                        {
                            var v = i - 1;
                            while (items[v].Open == 0 && v > 0) v--;
                            item = items[v];
                        }

                        var prevItem = i == 1 ? items[i - 1] : ret[i - 2];
                        var haItem =
                                new _SymbolDataItem
                                {
                                    Close = 0.25 * (item.Open + item.Close + item.Low + item.High),
                                    Open = 0.5 * (prevItem.Open + prevItem.Close),
                                    High = Math.Max(Math.Max(item.High, item.Open), item.Close),
                                    Low = Math.Min(Math.Min(item.Low, item.Open), item.Close),
                                    Timestamp = item.Timestamp,
                                    //SymbolName = item.SymbolName,
                                    Id = item.Id,
                                    //ExchangeId = item.ExchangeId
                                };

                        ret.Add(haItem);
                    }
                    //ha list is one item shorter than the raw data, so must offset all data by one item for it to line up
                    var firstItem = ret[0];
                    ret.Insert(0, firstItem);
                }

                return (ret, currentCandleItems);
            }
            catch
            {
                return (null, null);
            }
        }

        //will use supplied price (i.e. setting a stop at an arbitrary price) or will take that from orderline instead if supplied
        internal (bool, bool) CanSetStop(double price, _ChartLine orderLine, _ChartLine stopLine, TradePosition position, TradeAction action, bool hasEntered)
        {
            if (orderLine == null || currentDataItem == null)
            {
                return (true, true);
            }
            (var stopPrice, _) = stopLine == null ? (price, true) : stopLine.GetPriceAtTime(currentDataItem.Timestamp, true);
            
            (var currentPrice, _) = hasEntered ? (currentDataItem.Close, true) : orderLine.GetPriceAtTime(currentDataItem.Timestamp, true);

            var v = MainObj.GetPriceDiff(stopPrice, currentPrice, position) * 100; //cannot be wrong side of price
            
            var isClose = Math.Abs(v) < STOPLINE_MIN_VALUE;
            
            var isWrongSide = action == TradeAction.STOP ? v >= 0 : v < 0;
            
            return (isClose, isWrongSide);
        }

        #endregion

        #region Unused
        /*
        internal void SetMarketOrderLocal_UI_synchronised(double price, TradePosition position)
        {
            if (currentDataItem == null)
            {
                AlertDialog.Show("Waiting for connection", "Not possible");
                return;
            }

            InitForDataContext(() =>
            {
                if (Order != null)
                {
                    AlertDialog.Show("Cannot set 2 active trades to run concurrently on same symbol", "Not possible");
                    return;
                }


                //must have at least 24 hours of data, or trade can be placed before first midnight boundary so will never be visible unless it closes the following day
                if (results.Count < 24 * 60)
                {
                    AlertDialog.Show("Could not set market break order, needs 24 hours data", "Insufficient data");
                    return;
                }

                Order = new _Trade
                {
                    Timestamp = DateTime.UtcNow,
                    TradeReference = _Trade.GetNewTradeId(),
                    Type = TradeType.MARKET_ORDER,
                    Position = position,
                    Action = TradeAction.ORDER,
                    Price = price,
                    TimeFrame = TimeFrame,
                    SymbolName = symbolName,
                    ExchangeId = exchangeId,
                    ItemStatus = TradeItemStatus.CLOSED,
                    Leverage = Leverage,
                };

                SetUpdateRequest(Order);

                currentTradeSetReference = Order.TradeReference;

                EnterTradeOnSystem_synchronised(price, 0, TradeExchangeStatus.ENTERED_OFF_EXCHANGE);

                Order.ActionValue = Entry.ActionValue = 0; //mark as using zero equity, to keep this off the profit / loss calculations
            });

            MainObj.TradeStatusChanged(this);
        }


        internal void SetLineBreakOrderClosed_UI_synchronised(DateTime timeStamp, double price, TradePosition position)
        {
            SetLineBreakOrder_UI_synchronised(timeStamp, price, position, true);
        }
        internal void SetHMABreakOrder_UI_synchronised(DateTime timeStamp, double price)
        {
            if (hmaPriceBreakOrderIndicator == null)
            {
                AlertDialog.Show("No HMA indicator!", "Not possible");
                return;
            }
            if (currentDataItem == null)
            {
                AlertDialog.Show("Waiting for connection", "Not possible");
                return;
            }

            InitForDataContext(() =>
            {
                //ensure break is above current price
                //var frame = chart.GetFrameForTimestamp(currentDataItem.Timestamp);
                var currentPrice = currentDataItem.Close;
                if (!MainObj.IsBacktesting)
                {
                    timeStamp = DateTime.UtcNow.AddMinutes(-1);
                }

                var frame = chart.GetFrameForTimestamp(timeStamp); //because a backtest trade can be at any point, although backtesting not implemented on this order type

                if (currentDataItem != null)
                {
                    var order = hmaPriceBreakOrderIndicator.SetOrder_UI_synchronised(TimeFrame, TradeType.HMA_BREAK_ORDER, TradePosition.LONG, symbolName, false, currentDataItem.Close, frame);

                    if (order != null)
                    {
                        currentTradeSetReference = order.TradeReference;
                        SetUpdateRequest(order);
                    }
                }
                else
                {
                    AlertDialog.Show($"Waiting for connection... ", "Cannot set trade");
                }
            });

            MainObj.TradeStatusChanged(this);
        }
       
        //sets an order which will enter on (hourly) macd histogram zero level cross up
        internal void SetMacdOrder_UI_synchronised(DateTime timeStamp)
        {
            if (macdPriceBreakOrderIndicator == null)
            {
                return;
            }
            if (currentDataItem == null)
            {
                AlertDialog.Show("Waiting for connection", "Not possible");
                return;
            }

            InitForDataContext(() =>
            {
                //ensure break is above current price
                //var frame = chart.GetFrameForTimestamp(currentDataItem.Timestamp);
                var currentPrice = currentDataItem.Close;
                if (!MainObj.IsBacktesting)
                {
                    timeStamp = DateTime.UtcNow.AddMinutes(-1);
                }

                var frame = chart.GetFrameForTimestamp(timeStamp); //because a backtest trade can be at any point, although backtesting not implemented on this order type

                if (currentDataItem != null)
                {
                    var order = macdPriceBreakOrderIndicator.SetOrder_UI_synchronised(TimeFrame, TradeType.HMA_BREAK_ORDER, TradePosition.LONG, symbolName, false, currentDataItem.Close, frame);

                    if (order != null)
                    {
                        currentTradeSetReference = order.TradeReference;
                        SetUpdateRequest(order);
                    }
                }
                else
                {
                    AlertDialog.Show($"Waiting for connection... ", "Cannot set trade");
                }
            });

            MainObj.TradeStatusChanged(this);
        }        internal void SetMovingAverageStop_UI_synchronised(TradeType type = TradeType.NO_FLIP_ORDER)
        {

            //get the moving average
            double lastResult = 0;
            var MA_Indicator = MainObj.displayPanelsForSymbol[symbolName].FirstOrDefault(x => x.GetType() == typeof(SMA_Indicator));
            if (MA_Indicator != null)
            {
                //only one moving average
                var movingAverage = MA_Indicator.movingAverages[0];
                var results = movingAverage.GetResultsFromDictionary(TimeFrame);
                lastResult = results[results.Count - 1];
            }

            _Trade entry = null;
            _Trade _stop = null;
            //no need for a synchronisation here
            using (DataContext db = new DataContext())
            {
                _entry = db.Trades.FirstOrDefault(x =>
                           x.SymbolName == symbolName &&
                           x.GlobalStatus == TradeGlobalStatus.ACTIVE &&
                           x.ItemStatus == TradeItemStatus.OPEN &&
                           x.Action == TradeAction.ENTRY
                           );
                if (_entry != null)
                {

                    _stop = db.Trades.FirstOrDefault(x =>
                               x.TradeReference == _entry.TradeReference &&
                               x.GlobalStatus == TradeGlobalStatus.ACTIVE &&
                               x.ItemStatus == TradeItemStatus.OPEN &&
                               x.Action == TradeAction.STOP
                               );
                }
            }

            //can't synchronise this block as it contains SetStop_UI()
            if (_entry != null && currentDataItem != null) //only set on a live order
            {
                if (_entry.Position == TradePosition.LONG ? lastResult > currentDataItem.Close : lastResult < currentDataItem.Close)
                {
                    AlertDialog.Show("Moving average price is wrong side of order");
                    return;
                }
                //just easier to allow the loop to calculate the first actual price, than do it here...
                var initialPrice = lastResult != 0 ? lastResult : _entry.Position == TradePosition.LONG ? currentDataItem.Close / 1.1 : currentDataItem.Close * 1.1;

                if (_stop == null)
                {
                    SetStop_UI(DateTime.UtcNow, initialPrice, TradeAction.STOP, type, TradeStop.TAKE_PROFIT_MA_CROSS);
                }
                else
                {
                    InitForDataContext(() =>
                    {
                        if (Stop.Id == _stop.Id)
                        {
                            Stop.Price = initialPrice;
                            Stop.Stop = TradeStop.TAKE_PROFIT_MA_CROSS;
                            Stop.Type = type;
                            SetStopOnExchange_Synchronised();
                            var chartLine = AllLinesForSymbol.GetLineForTrade(Stop);
                            if (chartLine != null)
                            {
                                chartLine.TradeId = null;
                                chartLine.TradeReference = null;
                                chartLine.Type = LineType.NONE;
                                SetUpdateRequest(chartLine);
                            }
                            SetUpdateRequest(Stop);
                        }
                    });

                }
            }
            MainObj.TradeStatusChanged(this);
        }
        internal void AddBreakToLineLocal_UI_synchronised(int chartLineId, TradePosition position, double price, bool isBacktest)
        {
            if (currentDataItem == null)
            {
                AlertDialog.Show("Waiting for connection", "Not possible");
                return;
            }

            InitForDataContext(() =>
            {
                var chartLine = AllLinesForSymbol.FirstOrDefault(x => x.Id == chartLineId);

                if (chartLine.Trade != null)
                {
                    AlertDialog.Show("Trade already set on this line");
                    return;
                }

                Order = SetLineOrder_UI_synchronised(position, isBacktest, chartLine);

                currentTradeSetReference = Order.TradeReference;

                if (!isBacktest)
                {
                    EnterTradeOnSystem_synchronised(price, 0, TradeExchangeStatus.ENTERED_ON_EXCHANGE);
                    Order.ActionValue = Entry.ActionValue = 0; //mark as using zero equity, to keep this off the profit / loss calculations
                }

            });

            MainObj.TradeStatusChanged(this);
        }
        internal void AdvanceTrade_UI_synchronised(int tradeId)
        {
            InitForDataContext(() =>
            {
                var trade = AllTrades.FirstOrDefault(x => x.Id == tradeId);
                if (trade == null)
                {
                    AlertDialog.Show("Could not find trade");
                    return;
                }
                if (Entry != null)
                {
                    AlertDialog.Show("Cannot advance a trade start time which has already entered");
                    return;
                }

                //extend line
                var line = AllLinesForSymbol.GetLineForTrade(trade);
                if (line != null)
                {
                    line.StartTimeStamp = DateTime.UtcNow;
                    line.EndTimeStamp = line.StartTimeStamp.AddDays(1);
                    SetUpdateRequest(line);

                    //var nextEarliest = GetNextInterval_UI(DateTime.UtcNow);
                    //var nextPrice = line.EndPrice + (line.EndPrice - line.StartPrice) * nextEarliest.Subtract(line.EndTimeStamp).TotalSeconds / line.EndTimeStamp.Subtract(line.StartTimeStamp).TotalSeconds;
                    trade.Timestamp = line.StartTimeStamp;
                    trade.Price = line.StartPrice;
                }
                else
                {
                    trade.Timestamp = DateTime.UtcNow;
                }
                SetUpdateRequest(trade);

            });

            MainObj.TradeStatusChanged(this);
        }
        internal void SetFloorAtPrice_UI_synchronised(DateTime timeStamp, double price)
        {
            SetStop_UI(timeStamp, price, TradeAction.FLOOR, TradeType.NO_FLIP_ORDER);
        }

        internal void SetCeilingAtPrice_UI_synchronised(DateTime timeStamp, double price)
        {
            //Set Take Profit - this assumes no chart line, so sets that ID to -1.
            //If this is not done, then it will be cloned from the order and that will result in the line changing status if the TP is deleted
            SetStop_UI(timeStamp, price, TradeAction.CEILING, TradeType.NO_FLIP_ORDER);
        }

        internal void SetBreakEvenAtPrice_UI_synchronised(DateTime timeStamp, double price)
        {
            SetStop_UI(timeStamp, price, TradeAction.BREAK_EVEN, TradeType.NO_FLIP_ORDER);
        }
        internal void RemoveWaitingTrades_UI_synchronised()
        {
            InitForDataContext(() =>
            {
                foreach (var order in WaitingOrders)
                {
                    RemoveTradeFromSystem_UI_synchronised(order, false);
                }
            });
        }        
        internal void RemoveUnallocatedLines_UI_synchronised()
        {
            InitForDataContext(() =>
            {
                if (AllLinesForSymbol.Count > 0)
                {
                    var unallocated = AllLinesForSymbol.Where(x => x.Trade == null);
                    foreach (var line in unallocated)
                    {
                        SetUpdateRequest(line, TradeUpdateRequest.DELETE);
                    }
                }
            });
        }
        internal void ExtendLines_UI_synchronised(DateTime nextEarliest)
        {
            InitForDataContext(() =>
            {
                foreach (var line in ActiveLines)
                {
                    line.EndPrice = line.EndPrice + (line.EndPrice - line.StartPrice) * nextEarliest.Subtract(line.EndTimeStamp).TotalSeconds / line.EndTimeStamp.Subtract(line.StartTimeStamp).TotalSeconds;
                    line.EndTimeStamp = nextEarliest;
                    SetUpdateRequest(line);
                }
            });
        }
        public static bool AnyEnteredLive(DataContext db)
        {
            return db.Trades.Any(x =>
                        x.GlobalStatus == TradeGlobalStatus.ACTIVE &&
                        x.ItemStatus == TradeItemStatus.OPEN &&
                        x.Action == TradeAction.ENTRY
                        );
        }

        */

        #endregion

    }
}