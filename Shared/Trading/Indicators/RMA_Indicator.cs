using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.MovingAverages;
using System.Windows.Media;


namespace AlgoTrader.Shared.TradeIndicators
{
    public class EMA_Indicator : DisplayPanel
    {
        readonly int lineThickness;
        readonly SolidColorBrush color;
        readonly MovingAverage indicator;

        public EMA_Indicator(int timeInterval, TradeEngine tradeEngine, Chart chart, int length, CandlePosition candlePosition, int lineThickness, SolidColorBrush color) : base(null, timeInterval, tradeEngine, chart)
        {
            this.lineThickness = lineThickness;
            this.color = color;
            movingAverages.Add(indicator = new EMA(candlePosition, chart, length));
        }

        public override void Draw(bool shallow) => DrawLineOverlayChart(indicator, lineThickness, color);

    }
}
