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
                    //so need a new lines list

                    var chartLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.Id == Id);
                    if (chartLine != null)
                    {
                        //bail if trade is active and line has the order attached
                        if (chartLine.Trade != null && chartLine.Trade.Action == TradeAction.ORDER)
                        {
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, chartLine.Trade.TradeReference);
                            if (entry != null)
                            {
                                return;
                            }
                        }

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


                        tradeEngine.SetUpdateRequest(chartLine);

                        //SetMinimumLength(tradeEngine.TimeFrame);

                        var diff = chartLine.EndTimeStamp - chartLine.StartTimeStamp;
                        //need to show the line as a minimum of one frame length, on the current timeframe
                        if (diff.TotalMinutes < tradeEngine.TimeFrame)
                        {
                            chartLine.EndTimeStamp = StartTimeStamp.AddMinutes(tradeEngine.TimeFrame);
                        }

                        if (chartLine.Trade != null)
                        {
                            var order = tradeEngine.GetTradeForTradeReference(TradeAction.ORDER, chartLine.Trade.TradeReference);
                            var entry = tradeEngine.GetTradeForTradeReference(TradeAction.ENTRY, chartLine.Trade.TradeReference);

                            if (order != null && entry != null)
                            {
                                var orderLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.TradeId == order.Id);

                                bool isClose = true;
                                bool isWrongSide = true;

                                if (tradeEngine.currentDataItem != null)
                                {
                                    if (chartLine.Trade.Type != TradeType.STOP_WAITING)
                                    {

                                        //first see if stop line has moved
                                        var stopLine = tradeEngine.AllLinesForSymbol.FirstOrDefault(x => x.TradeId == chartLine.Trade.Id);
                                        (isClose, isWrongSide) = tradeEngine.CanSetTrade(orderLine, stopLine, chartLine.Trade.Price, chartLine.Trade.Action, entry, order.Position);

                                        if (!isClose)
                                        {
                                            //then see if order line has moved
                                            (isClose, isWrongSide) = tradeEngine.CanSetTrade(orderLine, chartLine, 0, chartLine.Trade.Action, entry, order.Position);
                                        }
                                    }
                                    static void CopyLineBack(TradeEngine tradeEngine, _ChartLine chartLine, _ChartLine originalLine)
                                    {
                                        using DataContext db = new();
                                        var _line = db.Lines.FirstOrDefault(x => x.Id == chartLine.Id);
                                        if (_line != null)
                                        {
                                            originalLine.CopyTo(_line);
                                            var attachedTrade = db.Trades.FirstOrDefault(x => x.Id == _line.TradeId);
                                            if (attachedTrade != null)
                                            {
                                                (attachedTrade.Price, _) = _line.GetPriceAtTime(attachedTrade.Timestamp, true);

                                                tradeEngine.UpdateUIList(attachedTrade);
                                            }
                                            db.SaveChanges();
                                        }
                                        originalLine.CopyTo(chartLine);

                                        tradeEngine.UpdateUIList(chartLine);
                                        tradeEngine.SetUpdateRequest(chartLine);
                                    }

                                    if (isClose)
                                    {

                                        _ChartLine newLine = new();
                                        chartLine.CopyTo(newLine);
                                        CopyLineBack(tradeEngine, chartLine, originalLine);

                                        if (isWrongSide)
                                        {
                                            AlertDialog.Show("Trade wrong side of price");
                                        }
                                        else
                                        {
                                            //this is problematic because the enclosing method terminates before this dialog shows.
                                            //so the line is moved back by default then forward again if agreed.
                                            //The line copies and the database update are independent of the synchronised block
                                            AlertDialog.ShowCallback($"Trade would be too close to price and may trigger close.\nDo you wish to procede? ", $"Position may not be valid", () =>
                                            {
                                                CopyLineBack(tradeEngine, chartLine, newLine);
                                                MainObj.DrawCurrentDispatcher(true);
                                            });
                                        }
                                    }
                                    else
                                    {
                                        tradeEngine.UpdateUIList(chartLine);
                                        //do a price update here so the trade can be immediately redrawn
                                        (chartLine.Trade.Price, _) = GetPriceAtTime(chartLine.Trade.Timestamp, true);
                                        tradeEngine.UpdateUIList(chartLine.Trade);
                                    }
                                    
                                }

                                //tradeEngine.LineUpTrades_UI_Synchronised(chartLine.Trade, chartLine);
                                tradeEngine.currentTradeSetReference = chartLine.Trade.TradeReference;
                                MainObj.DrawCurrentDispatcher(true);
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

            // MainObj.DrawImmediate();
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
