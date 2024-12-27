using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.MovingAverages;
using System.Windows.Media;


namespace AlgoTrader.Shared.TradeIndicators
{
    public class RMA_Indicator : DisplayPanel
    {
        readonly int lineThickness;
        readonly SolidColorBrush color;
        readonly MovingAverage indicator;

        public RMA_Indicator(int timeInterval, TradeEngine tradeEngine, Chart chart, int length, CandlePosition candlePosition, int lineThickness, SolidColorBrush color) : base(null, timeInterval, tradeEngine, chart)
        {
            this.lineThickness = lineThickness;
            this.color = color;
            movingAverages.Add(indicator = new RMA(candlePosition, chart, length));
        }

        public override void Draw(bool shallow) => DrawLineOverlayChart(indicator, lineThickness, color);

    }
}
