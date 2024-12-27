using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.MovingAverages;
using System.Windows.Media;

namespace AlgoTrader.Shared.TradeIndicators
{
    //this is the display class for the basic SMA
    public class SMA_Indicator : DisplayPanel
    {
        readonly int lineThickness;
        readonly SolidColorBrush color;
        readonly MovingAverage indicator;
        readonly double space;

        public SMA_Indicator(int timeInterval, TradeEngine tradeEngine, Chart chart, int length, CandlePosition candlePosition, int lineThickness, SolidColorBrush color, double space = 0) : base(null, timeInterval, tradeEngine, chart)
        {
            this.lineThickness = lineThickness;
            this.color = color;
            this.space = space;
            movingAverages.Add(indicator = new SMA(candlePosition, chart, length));
        }

        public override void Draw(bool shallow) => DrawLineOverlayChart(indicator, lineThickness, color, space);
    }
}
