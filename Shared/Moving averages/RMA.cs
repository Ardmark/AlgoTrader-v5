namespace AlgoTrader.Shared.MovingAverages
{
    //Moving average used in RSI. It is the exponentially weighted moving average with alpha = 1 / length.
    //So just need to set the dividend to the correct value
    public class RMA : EMA
    {
        public RMA(AlgoTrader.Shared.Enums.CandlePosition candlePosition, Chart chart, int length) : base(candlePosition, chart, length, 1) { }
    }
}
