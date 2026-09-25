using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Windows;

namespace AlgoTrader.Shared
{
    /*
     * All methods only called from mainViewModel, in response to UI input
     * */
    public partial class _ChartLine : UIDrawingBase //as opposed to line, which is a WPF object
    {

        #region Mouse

        //cursor exits line when dragging, so chart will get the mouse up event instead of line, generally
        public void MouseUp_UI_synchronised()
        {
            var tradeEngine = MainObj.GetTradeEngineForSymbol(SymbolName);

            tradeEngine.InitForDataContext(() => //need the Entry property set
            {
                if (StartTimeStamp > EndTimeStamp)
                {
                    AlertDialog.Show("Start of line must be before end. Try a shorter timeframe.");
                    if (Id > 0)
                    {
                        var chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                        if (chartLine != null)
                        {
                            (StartTimeStamp, EndTimeStamp) = (chartLine.StartTimeStamp, chartLine.EndTimeStamp);
                            (StartPrice, EndPrice) = (chartLine.StartPrice, chartLine.EndPrice);
                        }
                    }
                    else
                    {
                        (StartTimeStamp, EndTimeStamp) = (EndTimeStamp, StartTimeStamp);
                        var id = tradeEngine.SetUpdateRequestAdd_UI(this);
                        line.Name = "_" + Id.ToString(); //cannot start with a number
                    }   
                    
                    tradeEngine.UpdateUIList(this);
                    tradeEngine.SetUpdateRequest(this);
                    MainObj.DrawCurrentDispatcher(true);
                    return;

                }

                movingLine = null;
                isDraggingEnd = isDraggingStart = false;

                if (Id == 0)
                {
                    //line is not in DB and is being drawn
                    var id = tradeEngine.SetUpdateRequestAdd_UI(this);
                    line.Name = "_" + Id.ToString(); //cannot start with a number
                    tradeEngine.SetUpdateRequest(this);
                }      
                else
                {
                    //tradeEngine.Lines is only for the active trade
                    //tradeEngine.UILInes does not Include Trades

                    //get the actual line from the db
                    var chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                    if (chartLine != null)
                    {
                        void SetLineToOrder(_Trade trade, _Trade orderEntry, bool hasEntered, _ChartLine line, TimeSpan translateTime)
                        {
                            if (line == null || trade == null || orderEntry == null)
                            {
                                return;
                            }

                            //if the trade line is the one currently being moved, we need those values. Otherwise, use the
                            // line data from the database

                            var currentLine = (TradeId == trade.Id || StopId == trade.Id) ? this : line;

                            var _gradient = (currentLine.EndPrice - currentLine.StartPrice) / (currentLine.EndTimeStamp - currentLine.StartTimeStamp).Ticks;
                            var _timediff = (currentLine.EndTimeStamp - currentLine.StartTimeStamp).Ticks;

                            //order, stop and take profit must always line up
                            //order line must only start with order
                            //stop and tp lines can start anywhere
                            //order line must always end at current time if trade is entered
                            // otherwise it can end anywhere
                            //stop and tp lines can end anywhere
                            //no order or entry, line is unbounded anyway

                            //trade time is always lined up with order, or for stop and tp it is current time if trade has entered
                            //set line start always before end
                            if (currentLine.StartTimeStamp > currentLine.EndTimeStamp)
                            {
                                var v1 = currentLine.StartTimeStamp;
                                var v2 = currentLine.StartPrice;
                                currentLine.StartTimeStamp = currentLine.EndTimeStamp;
                                currentLine.StartPrice = currentLine.EndPrice;
                                currentLine.EndTimeStamp = v1;
                                currentLine.EndPrice = v2;
                            }

                            //line start must not be after current time, etc.
                            var lineStartTime = currentLine.StartTimeStamp;
                            var lineEndTime = currentLine.EndTimeStamp;
                            if (lineStartTime > DateTime.UtcNow)
                            {
                                lineStartTime = DateTime.UtcNow;
                            }
                            if (lineEndTime < DateTime.UtcNow)
                            {
                                lineEndTime = DateTime.UtcNow;
                            }

                            bool isOrderOrEntry = trade.Action == TradeAction.ORDER || trade.Action == TradeAction.ENTRY;
                            DateTime startTime, endTime, tradeTime;
                            if (isOrderOrEntry)
                            {
                                startTime = hasEntered ? orderEntry.Timestamp : lineStartTime;
                                endTime = hasEntered ? DateTime.UtcNow : lineEndTime;
                                tradeTime = orderEntry.Timestamp;
                            }
                            else
                            {
                                startTime = lineStartTime;
                                endTime = lineEndTime;
                                tradeTime = hasEntered ? DateTime.UtcNow : orderEntry.Timestamp;
                            }

                            var diff = chartLine.EndTimeStamp - chartLine.StartTimeStamp;
                            //need to show the line as a minimum of a couple of frames
                            if ((endTime - startTime).TotalMinutes < tradeEngine.TimeFrame * 2)
                            {
                                endTime = startTime.AddMinutes(tradeEngine.TimeFrame * 2);
                            }

                            //scaling bug will move candles and trade across if the trade is beyond the candle bounds.
                            //This is not an issue if only an empty line is drawn
                            //this will show as the line starting before the last candle, with the trade being apparently disconnected
                            // - but the timestamp for the last candle will appear to be wrong as the candles are shifted across
                            //Fix by not allowing the trade to go that far, by moving the line start and end back to just before the last candle
                            var lastItemTime = tradeEngine.Results.Last().Timestamp;
                            if (tradeTime > lastItemTime)
                            {
                                tradeTime = lastItemTime;
                            }

                            if (isOrderOrEntry)
                            {
                                tradeTime += translateTime;
                            }
                            //map the prices to the correct level based on line gradient
                            //work from the opposite end of the line to the required start or end time
                            //will update the database line here, not the current line, which may be the UI object
                            line.StartPrice = currentLine.EndPrice - _gradient * (endTime - startTime).Ticks;
                            line.EndPrice = currentLine.StartPrice + _gradient * (endTime - startTime).Ticks;
                            line.StartTimeStamp = startTime;
                            line.EndTimeStamp = endTime;
                            trade.Timestamp = tradeTime;
                            (trade.Price, _) = line.GetPriceAtTime(orderEntry.Timestamp, true);

                            tradeEngine.SetUpdateRequest(line);
                            tradeEngine.SetUpdateRequest(trade);
                            tradeEngine.UpdateUIList(trade);
                            tradeEngine.UpdateUIList(line);
                        }

                        void SetLineStartAndEnd(_Trade orderEntry = null)
                        {
                            if (orderEntry == null)
                            {
                                return;
                            }
                            var tradeRef = orderEntry.TradeReference;
                            var order = tradeEngine.GetTradeForTradeReference(TradeAction.ORDER, tradeRef);
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, tradeRef);
                            var stop = tradeEngine.GetTradeForTradeReference(TradeAction.STOP, tradeRef);
                            var takeProfit = tradeEngine.GetTradeForTradeReference(TradeAction.TAKE_PROFIT, tradeRef);

                            TimeSpan translateTime = TimeSpan.Zero;
                            if (TradeId == orderEntry.Id || StopId == orderEntry.Id)
                            {
                                //if the order line gets moved, update the order time so that can be moved away from the current time
                                if (!isDraggingEnd && !isDraggingStart)
                                {
                                    var lines = tradeEngine.GetLinesForTradeReference(tradeRef);
                                    var _line = lines.FirstOrDefault(x => x.Id == Id);
                                    translateTime = StartTimeStamp - _line.StartTimeStamp;
                                }
                            }

                            //pass each of the lines and trades in, the order will update the others anyway so they then won't make any changes
                            //and vice-versa with the stop and tp, they will not update the order though
                            //Perhaps not efficient but the most clear way to do this
                            if (Stop == null) //no stop set on this line
                            {
                                //don't move the order if you are moving TP or stop, but OK if vice-versa
                                if (order != null)
                                {
                                    var _line = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.TradeId == order.Id);
                                    SetLineToOrder(order, orderEntry, entry != null, _line, translateTime);
                                }
                            }
                            if (stop != null)
                            {
                                var _line = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.StopId == stop.Id);
                                SetLineToOrder(stop, orderEntry, entry != null, _line, translateTime);
                            }
                            if (takeProfit != null)
                            {
                                var _line = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.StopId == takeProfit.Id);
                                SetLineToOrder(takeProfit, orderEntry, entry != null, _line, translateTime);
                            }
                        }

                        if (Trade == null && Stop == null)
                        {
                            CopyTo(chartLine);
                            tradeEngine.SetUpdateRequest(chartLine);
                            tradeEngine.UpdateUIList(chartLine);
                            tradeEngine.SetUpdateRequest(chartLine);
                            MainObj.DrawCurrentDispatcher(true);
                        }
                        else
                        {
                            var tradeRef = Trade == null ? Stop.TradeReference : Trade.TradeReference;
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, tradeRef);

                            //bail if trade is active and line has the order attached
                            if (entry != null && Trade != null)
                            {
                                return;
                            }

                            var order = tradeEngine.GetTradeForTradeReference(TradeAction.ORDER, tradeRef);

                            if (order != null)
                            {
                                var order_entry = entry ?? order;
                                var lines = tradeEngine.GetLinesForTradeReference(tradeRef);
                                var orderLine = lines.FirstOrDefault(x => x.TradeId == order.Id);

                                (bool isClose, bool isWrongSide) = tradeEngine.CanSetTrade(this, order.Price, order.Action, entry, order.Position);

                                if (isClose)
                                {
                                    if (isWrongSide)
                                    {
                                        AlertDialog.Show("Trade wrong side of price");
                                        MainObj.DrawCurrentDispatcher(true);
                                    }
                                    else
                                    {
                                        //want the default condition to be moving the line to where it has been set with the mouse
                                        CopyTo(chartLine);
                                        tradeEngine.UpdateUIList(chartLine);
                                        MainObj.DrawCurrentDispatcher(true);

                                        //AlertDialog.ShowCallback($"Trade would be too close to price and may trigger close.\nDo you wish to procede? ", $"Position may not be valid", (ret) =>
                                        //{
                                         //   if (ret)
                                            {
                                                //enclosing datacontext will be lost now
                                                Task.Run(() =>
                                                {
                                                    tradeEngine.InitForDataContext(() =>
                                                    {
                                                        SetLineStartAndEnd(order_entry);
                                                        MainObj.DrawCurrentDispatcher(true);
                                                        return;
                                                    });
                                                });
                                            }

                                            MainObj.DrawCurrentDispatcher(true);
                                        //});
                                    }
                                }
                                else
                                {
                                    SetLineStartAndEnd(order_entry);
                                    MainObj.DrawCurrentDispatcher(true);
                                    return;
                                }
                            }
                        }
                    }
                }   
            });
        }

        #endregion

        #region Draw

        public void Draw_UI(Chart chart)
        {
            try
            {
                //SetMinimumLength(chart.TimeFrame);

                (line.X1, line.Y1) = chart.GetDrawPosition1(StartTimeStamp, StartPrice);
                (line.X2, line.Y2) = chart.GetDrawPosition1(EndTimeStamp, EndPrice);

                lineVisual.X1 = line.X1;
                lineVisual.Y1 = line.Y1;
                lineVisual.X2 = line.X2;
                lineVisual.Y2 = line.Y2;

                if (!chart.canvas.Children.Contains(lineVisual))
                {
                    chart.canvas.Children.Add(lineVisual);
                }
                if (!chart.canvas.Children.Contains(line))
                {
                    chart.canvas.Children.Add(line);
                }

            }
            catch
            {

            }
        }

        #endregion

        #region Move

        public void MoveLine_UI(double oldMouseX, double newMouseX, double oldMouseY, double newMouseY, bool shiftDown, bool ctrlDown, Chart chart)
        {

            var deltaX = newMouseX - oldMouseX;
            var deltaY = newMouseY - oldMouseY;

            if (Trade != null)
            {
                //If order and has entered, cannot move line
                if (Trade.Action == TradeAction.ORDER && Trade.ItemStatus == TradeItemStatus.CLOSED)
                {
                    return;
                }
            }

            if (isDraggingEnd)
            {
                line.X2 += deltaX;
                line.Y2 += deltaY;
                if (shiftDown)
                {
                    line.Y2 = line.Y1;
                }
            }
            else if (isDraggingStart)
            {
                line.X1 += deltaX;
                line.Y1 += deltaY;
                if (shiftDown)
                {
                    line.Y1 = line.Y2;
                }
            }
            else
            {
                //test for drag starting
                (isDraggingStart, isDraggingEnd) = IsOverLineEnds(newMouseX, newMouseY);

                if (isDraggingEnd && isDraggingStart)
                {
                    isDraggingStart = false; //no need for both
                }
                if (!(isDraggingStart || isDraggingEnd))
                {
                    line.X1 += deltaX;
                    line.Y1 += deltaY;
                    line.X2 += deltaX;
                    line.Y2 += deltaY;
                }
            }

            lineVisual.X1 = line.X1;
            lineVisual.Y1 = line.Y1;
            lineVisual.X2 = line.X2;
            lineVisual.Y2 = line.Y2;

            if (line.X1 == 0)
            {
                return;
            }

            (StartTimeStamp, StartPrice) = chart.GetTimeAndPrice(line.X1, line.Y1);
            (EndTimeStamp, EndPrice) = chart.GetTimeAndPrice(line.X2, line.Y2);
        }

        #endregion

    }
}
