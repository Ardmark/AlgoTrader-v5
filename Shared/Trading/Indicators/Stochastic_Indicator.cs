using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.MovingAverages;
using System.Windows.Media;

namespace AlgoTrader.Shared.TradeIndicators
{
    public class Stochastic_Indicator : DisplayPanel
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


        #endregion

        public Stochastic_Indicator(int timeInterval, TradeEngine tradeEngine, Chart chart, int length, CandlePosition candlePosition, int lineThickness, SolidColorBrush color) : base(null, timeInterval, tradeEngine, chart)
        {
            this.lineThicknessK = lineThickness;
            this.colorK = color;

            this.lineThicknessD = lineThickness;
            this.colorD = color;

            canvas = MainObj.indicator1_Canvas;
            movingAverages.Add(stochastic = new Stochastic(candlePosition, chart, length));
            movingAverages.Add(smaK = new SMA(candlePosition, chart, 3));
            movingAverages.Add(smaD = new SMA(candlePosition, chart, 3));
        }

        public override void ItemAdded(_SymbolDataItem item, bool canComputeTradeEntry, bool liveItem)//, bool redrawOnly)
        {
            foreach (var timeFrame in MainObj.timeFrames)
            {
                smaD.AddElementNoAggregation(smaK.AddElementNoAggregation(stochastic.ItemAdded(item, timeFrame), timeFrame), timeFrame);
            }
        }

        public override void Draw(bool shallow)
        {
            int startFrame = Chart.ScrollBars;
            int numFrames = Chart.ScaleBars;

            InitDraw();
            canvas.Children.Clear();
            List<double> items_smaK = stochastic.GetResults(startFrame, numFrames, Chart.TimeFrame);
            List<double> items_smaD = smaD.GetResults(startFrame, numFrames, Chart.TimeFrame);

            #region set the scaling to exactly fill the panel
            Low = double.MaxValue;
            High = double.MinValue;
            SetScaling([items_smaK, items_smaD]);
            #endregion

            DrawLineSegments(items_smaK, lineThicknessK, colorK); //blue
            DrawLineSegments(items_smaD, lineThicknessD, colorD);  //red
            DrawText(High.ToString("0"), 0, canvas.ActualHeight - 20);
            DrawText(Low.ToString("0"), 0, 0);

            /*       for (int i=0; i < items_smaK.Count; i++)
                   {
                       if (MovingIndicatorBase.Crossover(smaK, smaD, startFrame + i)) //the stored results are latest first, but the iterator returns earliest first so must reverse frame order
                       {
                           DrawMarker(MarkerStyle.TRIANGLEDOWN, new System.Windows.Point(i, 0), colorD, 10);
                       }
                       if (MovingIndicatorBase.Crossover(smaD, smaK, startFrame + i))
                       {
                          DrawMarker(MarkerStyle.TRIANGLEUP, new System.Windows.Point(i, canvas.ActualHeight - 10), colorK, 10);
                       }
                   }
                   */
        }
    }
}
