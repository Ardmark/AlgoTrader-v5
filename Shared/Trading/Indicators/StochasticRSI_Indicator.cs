using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.MovingAverages;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AlgoTrader.Shared.TradeIndicators
{
    public class StochasticRSI_Indicator : DisplayPanel
    {
        #region Fields
        const int smoothK_length = 3;
        const int smoothD_length = 3;
        readonly int lineThicknessK;
        readonly int lineThicknessD;
        readonly SolidColorBrush colorK;
        readonly SolidColorBrush colorD;
        readonly Stochastic stochastic;
        private readonly SMA smaK;
        private readonly SMA smaD;
        readonly RSI rSI;
        Line line1, line2;

        #endregion

        public StochasticRSI_Indicator(int timeInterval, TradeEngine tradeEngine, Chart chart, int length, CandlePosition candlePosition, int lineThickness, SolidColorBrush color) : base(null, timeInterval, tradeEngine, chart)
        {
            this.lineThicknessK = lineThickness;
            this.colorK = color;

            this.lineThicknessD = lineThickness;
            this.colorD = color;

            canvas = MainObj.indicator1_Canvas;
            /*
             * This is fairly close to TradingView with an RSI length of 7. Could possibly tweak this further
             * It is not exactly the same, though, and that could cause issues with trying to auto-pivot lines around high and low points
             * Since the strategy is based on TradingView's implementation, this will probably not be used.
             *  */

            /*
             * This currently only uses the single timeframe, not a dictionary, so that would need to be implemented if this was to be used for something
             * */

            movingAverages.Add(rSI = new RSI(candlePosition, chart, length));
            movingAverages.Add(stochastic = new Stochastic(candlePosition, chart, length));
            movingAverages.Add(smaK = new SMA(candlePosition, chart, 3));
            movingAverages.Add(smaD = new SMA(candlePosition, chart, 3));
            line1 = new();
            line2 = new();
        }

        public override void ItemAdded(_SymbolDataItem item, bool canComputeTradeEntry, bool liveItem)//, bool redrawOnly)
        {
            foreach (var timeFrame in MainObj.timeFrames)
            {
                var v = rSI.ItemAdded(item, timeFrame);
                var high = Math.Max(v, 0);
                var low = Math.Min(v, 0);
                var _item = new _SymbolDataItem
                {
                    Close = v,
                    Low = low,
                    High = high
                };
                var s = stochastic.ItemAdded(_item, timeFrame);

                smaD.AddElementNoAggregation(smaK.AddElementNoAggregation(s, timeFrame), timeFrame);
            }
        }

        public override void Draw(bool shallow)
        {
            int startFrame = Chart.ScrollBars;
            int numFrames = Chart.ScaleBars;

            InitDraw();
            canvas.Children.Clear();

            //List<double> items_rs1 = rSI.GetResults(startFrame, numFrames, Chart.TimeFrame);
            List<double> items_smaK = smaK.GetResults(startFrame, numFrames, Chart.TimeFrame);
            List<double> items_smaD = smaD.GetResults(startFrame, numFrames, Chart.TimeFrame);

            #region set the scaling to exactly fill the panel
            Low = double.MaxValue;
            High = double.MinValue;
            SetScaling([items_smaK, items_smaD]);
            #endregion

            //DrawLineSegments(items_rs1, lineThicknessK, System.Windows.Media.Brushes.Black);

            DrawLineSegments(items_smaK, lineThicknessK, System.Windows.Media.Brushes.SlateBlue); 
            //DrawLineSegments(items_smaD, lineThicknessD, System.Windows.Media.Brushes.Red);
            //DrawText(High.ToString("0"), 0, canvas.ActualHeight - 20);
            //DrawText(Low.ToString("0"), 0, 0);

            DrawLineSegmentNoOffset(new System.Windows.Point(0, 45), new System.Windows.Point(1000, 45), 1, System.Windows.Media.Brushes.Gray);
            DrawLineSegmentNoOffset(new System.Windows.Point(0, 75), new System.Windows.Point(1000, 75), 1, System.Windows.Media.Brushes.Gray);


            //for (int i=0; i < items_smaK.Count; i++)
            //{
            //    if (MovingIndicatorBase.Crossover(smaK, smaD, startFrame + i)) //the stored results are latest first, but the iterator returns earliest first so must reverse frame order
            //    {
            //        DrawMarker(MarkerStyle.TRIANGLEDOWN, new System.Windows.Point(i, 0), colorD, 10);
            //    }
            //    if (MovingIndicatorBase.Crossover(smaD, smaK, startFrame + i))
            //    {
            //        DrawMarker(MarkerStyle.TRIANGLEUP, new System.Windows.Point(i, canvas.ActualHeight - 10), colorK, 10);
            //    }
            //}
                   
        }

    }
}
