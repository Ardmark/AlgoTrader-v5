using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Windows;
using System.Data.Entity.Core.Common.EntitySql;
using System.Windows.Controls;
using System.Windows.Documents;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

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

            //var v = tradeEngine.chart.Results;
            //if (v != null)
            //{
            //    Debug.WriteLine("----------------------------");
            //    for (var i = v.Count - 1; i >= v.Count - 30; i--)
            //    {
            //        Debug.WriteLine(v[i].Timestamp);
            //    }
            //}

            //can't just use UILines here, because the data context they are derived from may be disposed.
            //so instead, need to get all lines with trades
            //If using UILines (persistant data context in TradeEngine) would need to Include Trades as well, not so good for performance
            // - so this may be a better appropach anyway
            if (StartTimeStamp > EndTimeStamp)
            {
                AlertDialog.Show("Start of line must be before end. Try a shorter timeframe.");
                tradeEngine.InitForDataContext(() =>
                {
                    if (Id > 0)
                    {
                        var chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                        if (chartLine != null)
                        {
                            StartTimeStamp = chartLine.StartTimeStamp;
                            EndTimeStamp = chartLine.EndTimeStamp;
                            StartPrice = chartLine.StartPrice;
                            EndPrice = chartLine.EndPrice;
                            tradeEngine.UpdateUIList(this);
                            tradeEngine.SetUpdateRequest(this);
                            MainObj.DrawCurrentDispatcher(true);
                            return;
                        }
                    }
                });
            }

            tradeEngine.InitForDataContext(() => //need the Entry property set
            {
                movingLine = null;

                isDraggingEnd = isDraggingStart = false;

                //move stop price and time after line move completed
                if (Id > 0)
                {
                    //tradeEngine.Lines is only for the active trade
                    //tradeEngine.UILInes does not Include Trades
  
                    //get the actual line from the db
                    var chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                    if (chartLine != null)
                    {
                        void SetLineStartAndEnd(_Trade trade = null)
                        {
                            //scaling bug will move candles and trade across if the trade is beyond the candle bounds.
                            //This is not an issue if only an empty line is drawn
                            //this will show as the line starting before the last candle, with the trade being apparently disconnected
                            // - but the timestamp for the last candle will appear to be wrong as the candles are shifted across
                            //Fix by not allowing the trade to go that far, by moving the line start and end back to just before the last candle
                            var lastItemTime = tradeEngine.Results.Last().Timestamp;
                            if (StartTimeStamp > lastItemTime)
                            {
                                var diff1 = lastItemTime.Subtract(StartTimeStamp).TotalMinutes - 1;
                                StartTimeStamp = StartTimeStamp.AddMinutes((int)diff1);
                                EndTimeStamp = EndTimeStamp.AddMinutes((int)diff1);
                                if (chartLine.Trade != null)
                                {
                                    chartLine.Trade.Timestamp = StartTimeStamp;
                                }
                            }

                            var lineCopy = new _ChartLine();
                            chartLine.CopyTo(lineCopy);
                            chartLine.StartPrice = StartPrice;
                            chartLine.EndPrice = EndPrice;
                            chartLine.StartTimeStamp = StartTimeStamp;
                            chartLine.EndTimeStamp = EndTimeStamp;

                            //set start always before end
                            if (chartLine.StartTimeStamp > chartLine.EndTimeStamp)
                            {
                                var v1 = chartLine.StartTimeStamp;
                                var v2 = chartLine.StartPrice;
                                chartLine.StartTimeStamp = chartLine.EndTimeStamp;
                                chartLine.StartPrice = chartLine.EndPrice;
                                chartLine.EndTimeStamp = v1;
                                chartLine.EndPrice = v2;
                            }

                            //line up the line start with the order or entry
                            if (trade != null)
                            {
                                var _gradient = (chartLine.EndPrice - chartLine.StartPrice) / (chartLine.EndTimeStamp - chartLine.StartTimeStamp).Ticks;
                                var _timediff = (chartLine.EndTimeStamp - chartLine.StartTimeStamp).Ticks;
                                chartLine.StartPrice -= _gradient * (chartLine.StartTimeStamp - trade.Timestamp).Ticks;
                                chartLine.StartTimeStamp = trade.Timestamp;
                            }

                            tradeEngine.SetUpdateRequest(chartLine);

                            //SetMinimumLength(tradeEngine.TimeFrame);

                            var diff = chartLine.EndTimeStamp - chartLine.StartTimeStamp;
                            //need to show the line as a minimum of one frame length, on the current timeframe
                            if (diff.TotalMinutes < tradeEngine.TimeFrame)
                            {
                                chartLine.EndTimeStamp = StartTimeStamp.AddMinutes(tradeEngine.TimeFrame);
                            }
                        }
                        
                        //bail if trade is active and line has the order attached
                        if (Trade != null && Trade.Action == TradeAction.ORDER)
                        {
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, chartLine.Trade.TradeReference);
                            if (entry != null)
                            {
                                return;
                            }
                        }

                        if (Trade == null)
                        {
                            SetLineStartAndEnd();
                            tradeEngine.UpdateUIList(chartLine);
                            tradeEngine.SetUpdateRequest(chartLine);
                            MainObj.DrawCurrentDispatcher(true);
                        }
                        else
                        {
                            var lines = tradeEngine.GetLinesForTradeReference(chartLine.Trade.TradeReference);
                            var order = tradeEngine.GetTradeForTradeReference(TradeAction.ORDER, chartLine.Trade.TradeReference);
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, chartLine.Trade.TradeReference);
                            var order_entry = entry != null ? entry : order;

                            if (order != null)
                            {
                                var orderLine = lines.FirstOrDefault(x => x.TradeId == order.Id);

                                bool isClose = true;
                                bool isWrongSide = true;

                                if (tradeEngine.currentDataItem != null)
                                {
                                    if (Trade.Type != TradeType.STOP_WAITING)
                                    {
                                        (isClose, isWrongSide) = tradeEngine.CanSetTrade(orderLine, this, Trade.Price, Trade.Action, entry, order.Position);
                                    }
                                    else
                                    {
                                        var flipOrder = tradeEngine.ActiveTrades.FirstOrDefault(x => x.Action == TradeAction.STOP && x.Type == TradeType.FLIP_ORDER);
                                        if (flipOrder != null)
                                        {
                                            //var flipOrderLine = lines.FirstOrDefault(x => x.TradeId == chartLine.Trade.Id);
                                            var flipLine = lines.FirstOrDefault(x => x.TradeId == flipOrder.Id);
                                            var position = flipOrder.Position == TradePosition.SHORT ? TradePosition.LONG : TradePosition.SHORT;
                                            if (flipLine != null)
                                            {
                                                (isClose, isWrongSide) = tradeEngine.CanSetTrade(flipLine, this, flipOrder.Price, chartLine.Trade.Action, entry, position);
                                            }
                                        }
                                    }

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

                                            AlertDialog.ShowCallback($"Trade would be too close to price and may trigger close.\nDo you wish to procede? ", $"Position may not be valid", (ret) =>
                                            {
                                                if (ret)
                                                {
                                                    //enclosing datacontext will be lost now
                                                    Task.Run(() =>
                                                    {
                                                        tradeEngine.InitForDataContext(() =>
                                                        {
                                                            chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                                                            lines = tradeEngine.GetLinesForTradeReference(chartLine.Trade.TradeReference);
                                                            order = tradeEngine.GetTradeForTradeReference(TradeAction.ORDER, chartLine.Trade.TradeReference);
                                                            entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, chartLine.Trade.TradeReference);
                                                            SetPrice();
 
                                                            SetLineStartAndEnd(order_entry);
                                                            tradeEngine.UpdateUIList(chartLine.Trade);
                                                            tradeEngine.UpdateUIList(chartLine);
                                                            MainObj.DrawCurrentDispatcher(true);
                                                            return;
                                                        });
                                                    });
                                                }

                                                MainObj.DrawCurrentDispatcher(true);
                                            });
                                        }
                                    }
                                    else
                                    {
                                        SetPrice();
                                        SetLineStartAndEnd(order_entry);
                                        tradeEngine.UpdateUIList(chartLine.Trade);
                                        tradeEngine.UpdateUIList(chartLine);
                                        MainObj.DrawCurrentDispatcher(true);
                                        return;
                                    }
                                }

                                void SetPrice()
                                {
                                    //if the order is moved, then move the line to that (not entered, obviously)
                                    //then update all lines to move stops, etc. and line starts to coincide with the order start time
                                    //finally, Chart.Draw() will no longer need to line up stops with order
                                    if (chartLine.TradeId == order.Id && order.ItemStatus == TradeItemStatus.OPEN)
                                    {
                                        //move the order to the line start
                                        order.Timestamp = chartLine.StartTimeStamp;
                                        order.Price = chartLine.StartPrice;

                                        //always run the order line to the current time
                                        if (chartLine.EndTimeStamp < DateTime.UtcNow)
                                        {
                                            var _gradient = (chartLine.EndPrice - chartLine.StartPrice) / (chartLine.EndTimeStamp - chartLine.StartTimeStamp).Ticks;
                                            var _timediff = (chartLine.EndTimeStamp - chartLine.StartTimeStamp).Ticks;
                                            chartLine.EndTimeStamp = DateTime.UtcNow;
                                            chartLine.EndPrice = chartLine.StartPrice + _gradient * (DateTime.UtcNow - chartLine.StartTimeStamp).Ticks;
                                        }
                                    }


                                    //line up other trades and lines to the order
                                    tradeEngine.LineUpTrades_synchronised(lines, TradeAction.STOP, order, entry != null);
                                    tradeEngine.LineUpTrades_synchronised(lines, TradeAction.TAKE_PROFIT, order, entry != null);

                                    tradeEngine.UpdateUIList(chartLine);
                                    tradeEngine.currentTradeSetReference = chartLine.Trade.TradeReference;
                                    MainObj.DrawCurrentDispatcher(true);

                                    if (chartLine.Trade.Action == TradeAction.STOP && chartLine.Trade.TradeExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                                    {
                                        tradeEngine.SetStopOnExchange_Synchronised();
                                    }

                                    //do a price update here so the trade can be immediately redrawn
                                    (chartLine.Trade.Price, _) = GetPriceAtTime(chartLine.Trade.Timestamp, true);

                                }
                            }
                        }
                    }
                }

                if (Id == 0)
                {
                    //line is not in DB and is being drawn
                    var id = tradeEngine.SetUpdateRequestAdd_UI(this);
                    line.Name = "_" + Id.ToString(); //cannot start with a number
                    tradeEngine.SetUpdateRequest(this);
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

                //trade may be order or stop. If order and has entered, cannot move line
                if (Trade.Action == TradeAction.ORDER && Trade.ItemStatus == TradeItemStatus.CLOSED)
                {
                    return;
                }

                //block stop movement above current price, with a small margin of 1% (or would just close straight away if on exact price anyway)
                //if (Trade.Action == TradeAction.STOP)
                //{
                //    var tradeEngine = MainObj.GetTradeEngineForSymbol(Trade.SymbolName);
                //    if (tradeEngine.currentDataItem != null)
                //    {
                //        var entry = tradeEngine.UI_Trades.FirstOrDefault(x => x.Action == TradeAction.ENTRY && x.TradeReference == Trade.TradeReference);
                //        if (entry != null && entry.GlobalStatus == TradeGlobalStatus.ACTIVE)
                //        {
                //            //this only applies to live orders
                //            (var currLinePrice, _) = GetPriceAtTime(tradeEngine.currentDataItem.Timestamp, true);

                //            //calculate the maximum price (i.e. long) which you must not go above when moving a line.
                //            //The line may move incrementally anyway so it can get very close to the current price otherwise, and then the price can move against the
                //            // line resulting in a trade exit
                //            double beVal = 1 + TradeEngine.BREAK_EVEN_THRESHOLD / 100;
                //            var maxPrice =  entry.Position == TradePosition.LONG
                //                ? currLinePrice * beVal
                //                : currLinePrice / beVal; 

                //            bool not_ok = Trade.Position == TradePosition.LONG
                //                ? maxPrice > tradeEngine.currentDataItem.Close
                //                : maxPrice < tradeEngine.currentDataItem.Close;
                //            if (not_ok)
                //            {
                //                //is line moving away from the price? Must test for this, or line will get locked once threshold is hit
                //                //locks trade to the price, cannot move
                //                not_ok = Trade.Position == TradePosition.LONG
                //                    ? deltaY < 0
                //                    : deltaY > 0;
                //                if (not_ok)
                //                {
                //                    AlertDialog.Show($"Cannot move stop too within {TradeEngine.BREAK_EVEN_THRESHOLD}% of current price! Set a floor if this is desirable.");
                //                    return;
                //                }
                //            }
                //        }   
                //    }
                //}
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

            var frame = chart.GetFrameNumFromMouseX(line.X1); //frame matches display output.
            int barNum = (int)(line.X1 / MainObj.chart_Canvas.ActualWidth * Chart.ScaleBars) + chart.ScrollBars;

            //this matches screen output
            //var dateTime = barNum < Chart.items_chart.Count ? Chart.items_chart[barNum].Timestamp : Chart.items_chart.Last().Timestamp.AddMinutes(chart.TimeFrame * numExtraMins);

            (var _StartTimeStamp, _) = chart.GetTimeAndPrice(line.X1, line.Y1);

            var firstTime = chart.Results.First().Timestamp;
            var frameTime = firstTime.AddMinutes(frame);

            //this does not match the above
            (StartTimeStamp, StartPrice) = chart.GetTimeAndPrice(line.X1, line.Y1);
            (EndTimeStamp, EndPrice) = chart.GetTimeAndPrice(line.X2, line.Y2);

            //  SetMinimumLength(chart.TimeFrame);
        }

        #endregion

    }
}
