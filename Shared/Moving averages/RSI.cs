using AlgoTrader.Shared.Enums;

namespace AlgoTrader.Shared.MovingAverages
{
    //Stochastic
    public class RSI : MovingAverage
    {
        RMA rma1, rma2;
        List<_SymbolDataItem> items = [];
        public RSI(CandlePosition candlePosition, Chart chart, int length) : base(candlePosition, chart, length) {

            rma1 = new RMA(candlePosition, chart, length);
            rma2 = new RMA(candlePosition, chart, length);
        }

        public override void Init(int timeInterval)
        {
            base.Init(timeInterval);
            rma1.Init(timeInterval);
            rma2.Init(timeInterval);
        }

        public override double ItemAdded(_SymbolDataItem item, int timeFrame)
        {
            //todo - do as a dictionary and then retrieve the appropriate timeframe, this is just fixed
            items.Add(item);
            return base.ItemAdded(item, timeFrame);
        }

        public override double CalculateMovingAverage(_SymbolDataItem item, int timeFrame, bool isAggregating)
        {
            
            if (items.Count > 1)
            {
                var x0 = items[^1]; //same as items[items.Count - 1]
                var x1 = items[^2];
                var u = Math.Max(x0.Close - x1.Close, 0);
                var d = Math.Max(x1.Close - x0.Close, 0);
                var item1 = item.Clone();
                var item2 = item.Clone();
                item1.Close = u;
                item2.Close = d;
                var r1 = rma1.ItemAdded(item1, timeFrame);
                var r2 = rma2.ItemAdded(item2, timeFrame);
                if (r2 != 0)
                {
                    var rs = r1 / r2;
                    var res = 100 - 100 / (1 + rs);
                    return res;
                }
            }

            return 0;
        }

    }
}
