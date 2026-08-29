using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using System.Text;
using System.Windows.Threading;

namespace AlgoTrader.Shared
{
    public static class ActiveTradeSummary
    {
        static readonly List<double> prices__8hour = new List<double>();
        static readonly List<double> prices_24hour = new List<double>();
        public static DateTime LastClosedTimeToShow = DateTime.UtcNow.AddDays(-1);
        //public static int LastClosedTradesTimeframe { get; set; } = 24;
        static DispatcherTimer countdownTimer;
        static string SPACER = "***********************************************************************************************************************************************************\n";

        public static void Init()
        {

            int dailySummaryTime = 0;
            const int tradeSummaryInterval = 60; //seconds
                                                 //const int dailySummaryInterval = 60;//do daily summary every 1 min, not every 10 secs
            LastClosedTimeToShow = DateTime.UtcNow;
            countdownTimer = new DispatcherTimer(new TimeSpan(0, 0, tradeSummaryInterval), DispatcherPriority.Normal, async (object sender, EventArgs e) =>
            {
                dailySummaryTime += tradeSummaryInterval;
                await Update(null);
            }, Dispatcher.CurrentDispatcher);
        }

        public static async Task<(List<_Trade>, List<_Trade>)> Update(TradeEngine tradeEngine, bool dailySummary = false)
        {

            try
            {
                var ret = await Task.Run(() =>
                {
                    using DataContext db = new();
                    //this is not required for a timed update, that is just to refresh the info window
                    //otherwise, it is assumed that this call has probably altered the waiting or active trade status somewhere

                    tradeEngine?.UpdateCurrentTradeSetReference(db); //this is called null propagation

                    var allTrades = db.Trades.AsNoTracking().AsParallel().Where(x => x.GlobalStatus == TradeGlobalStatus.ACTIVE).ToList();
                    //because check is made against script name, if a trade has exited but still has a script it will be interrupted so will show in the list
                    var enteredTrades = allTrades.Where(x => x.Action == TradeAction.ENTRY).ToList();
                    var allOrders = allTrades.Where(x => x.Action == TradeAction.ORDER).ToList();
                    var waitingTrades = allOrders.Where(x => x.ItemStatus == TradeItemStatus.OPEN).ToList();
                    //var stops = allTrades.Where(x => x.Action == TradeAction.STOP && x.ItemStatus == TradeItemStatus.OPEN);
                    //var takeProfits = allTrades.Where(x => x.Action == TradeAction.CEILING && x.ItemStatus == TradeItemStatus.OPEN);
                    var _closedOrders = db.Trades.AsNoTracking().AsParallel().Where(x => x.Action == TradeAction.CLOSE).OrderBy(x => x.Timestamp).ToList();
                    var closedOrders = _closedOrders.Where(x => x.Timestamp > LastClosedTimeToShow).ToList();
                    var numClosed = closedOrders.Count;
                     //want to always show intermediate order so set the prev. close order here. If the date range just shows one close,
                    // this will ensure the intermediate order is calculated as well.
                    var prevClose = _closedOrders.Count > numClosed+1 ? _closedOrders[^(numClosed + 1)] : null;
                    //double prevEquity = prevClose != null ? prevClose.Equity : 0;   

                    var outputString = string.Empty;
                    string dailySummaryString = string.Empty;

                    #region get 9 and 24-hour summaries (unused)

                    /*
                    unchecked
                    {
                        //if (dailySummary)
                        //{
                        //    countdownTimer.Stop();
                        //    dailySummaryString = string.Empty;
                        //    prices__8hour.Clear();
                        //    prices_24hour.Clear();
                        //    var earliestTime__9 = DateTime.UtcNow.AddHours(-9);
                        //    var earliestTime_24 = DateTime.UtcNow.AddHours(-24);
                        //    //remove seconds to get an exact boundary on a candle
                        //    earliestTime__9 = new DateTime(earliestTime__9.Year, earliestTime__9.Month, earliestTime__9.Day, earliestTime__9.Hour, earliestTime__9.Minute, 0);
                        //    earliestTime_24 = new DateTime(earliestTime_24.Year, earliestTime_24.Month, earliestTime_24.Day, earliestTime_24.Hour, earliestTime_24.Minute, 0);

                        //    var tradeGroups = db.SymbolDataItems.AsNoTracking().AsParallel().GroupBy(x => x.SymbolName);

                        //    foreach (var group in tradeGroups)
                        //    {
                        //        var earliestTrade__9 = group.Where(x => DateTime.Compare(earliestTime__9, x.Timestamp) == 0).FirstOrDefault();
                        //        var earliestTrade_24 = group.Where(x => DateTime.Compare(earliestTime_24, x.Timestamp) == 0).FirstOrDefault();
                        //        var latestTrade = group.LastOrDefault(); //assuming order by timestamp anyway
                        //        if (earliestTrade__9 != null && latestTrade != null)
                        //        {
                        //            var v = MainObj.GetPriceDiff(latestTrade.Close, earliestTrade__9.Close, TradePosition.LONG);
                        //            prices__8hour.Add(v);
                        //        }
                        //        if (earliestTrade_24 != null && latestTrade != null)
                        //        {
                        //            var v = MainObj.GetPriceDiff(latestTrade.Close, earliestTrade_24.Close, TradePosition.LONG);
                        //            prices_24hour.Add(v);
                        //        }
                        //    }
                        //    if (prices__8hour.Any())
                        //    {
                        //        var avg = prices__8hour.Average() * 100;
                        //        dailySummaryString += string.Format("8 hour average: {0}%\n", avg.ToString("F"));
                        //    }
                        //    if (prices_24hour.Any())
                        //    {
                        //        var avg = prices_24hour.Average() * 100;
                        //        dailySummaryString += string.Format("24 hour average: {0}%\n", avg.ToString("F"));
                        //    }

                        //    countdownTimer.Start();
                        //}
                    }
                    */
                    #endregion

                    var sb = new StringBuilder(dailySummaryString);

                    sb.Append($"{MainObj.GetUpdateCount()} data updates received per minute\n");

                    //build a list of current data items
                    var currentSymbolList = new Dictionary<string, _SymbolDataItem>();

                    foreach (var v in MainObj.displayPanelsForSymbol)
                    {
                        //TradeEngine _tradeEngine = v.Value.Where(x => x.GetType() == (TradeEngine.EXCHANGE == ExchangeId.BITMEX ? typeof(TradeEngineBitmex) : typeof(TradeEngineBinance))).FirstOrDefault() as TradeEngine;
                        //if (_tradeEngine != null && _tradeEngine.currentDataItem != null)

                        if (v.Value != null) //.Where(x => x.GetType() == (TradeEngine.EXCHANGE == ExchangeId.BITMEX ? typeof(TradeEngineBitmex) : typeof(TradeEngineBinance))).FirstOrDefault() is TradeEngine _tradeEngine && _tradeEngine.currentDataItem != null)
                        {
                            var _tradeEngine = MainObj.GetTradeEngineForSymbol(v.Key);
                            currentSymbolList.Add(v.Key, _tradeEngine.currentDataItem);
                        }
                    }

                    string GetTradeType(_Trade _trade)
                    {
                        string tradeType = string.Empty;
                        switch (_trade.Type)
                        {
                            case TradeType.MARKET_ORDER:
                                tradeType = "Market order";
                                break;
                            case TradeType.PRICE_BREAK_ORDER:
                                tradeType = "Price break";
                                break;
                            case TradeType.LINE_BREAK_ORDER:
                                tradeType = "Line break";
                                break;
                            case TradeType.HMA_BREAK_ORDER:
                                tradeType = "HMA break";
                                break;
                        }
                        return tradeType;
                    }

                    sb.Append(SPACER);

                    #region running trades
                    sb.Append("Running trades:\n");
                    //e.g. Running: Binance BNBBTC   Price +0.5%   Stop -1%    Take profit +16%  (Market Break)
                    foreach (var trade in enteredTrades.OrderBy(x => x.Timestamp))
                    {
                        if (currentSymbolList.TryGetValue(trade.SymbolName, out _SymbolDataItem currentSymbol))
                        {
                            if (currentSymbol != null)
                            {

                                var s = trade.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE ? " " : "Test ";
                                s += trade.ExchangeMethodScript == null ? "" : "INTERRUPTED";

                                //sb.Append(string.Concat(Pad12(trade.SymbolName)));

                                //var priceDiff = -MainObj.GetPriceDiff(trade.Price, currentSymbol.Close, trade.Position) * 100;
                                //sb.Append(Pad20(string.Format("Price {0}{1}%", priceDiff < 0 ? "" : "+", priceDiff.ToString("F"))));
                                //sb.Append(string.Concat("    ", Enum.GetName(typeof(TradeType), trade.Type)));

                                var entry = db.Trades.Where(x => x.TradeReference == trade.TradeReference && x.Action == TradeAction.ENTRY).FirstOrDefault();
                                //var entryString = entry != null
                                //    ? $",  entered {entry.Timestamp:g} at {entry.Price}"
                                //    : string.Empty;

                                //sb.Append(Pad20(entryString));
                                //sb.Append(s);
                                //sb.Append("\n");
                                var priceDiff = -MainObj.GetPriceDiff(trade.Price, currentSymbol.Close, trade.Position) * 100;
                                var pc = string.Format("Price {0}{1}%", priceDiff < 0 ? "" : "+", priceDiff.ToString("F"));
                                var symbolName = SymbolNames.GetBaseAssetForSymbol(trade.SymbolName) ?? trade.SymbolName;
                                if (string.IsNullOrEmpty(symbolName))
                                {
                                    symbolName = trade.SymbolName;
                                }
                                if (entry != null)
                                {
                                    var pos = entry.Position == TradePosition.LONG ? "long" : "short";

                                    sb.Append($"{symbolName}\t" +
                                       $"{entry.Timestamp:d}\t" +
                                       $"{entry.Timestamp:t}\t" +
                                       $"{PriceFormatter.Format(entry.Price)}\t\t" +
                                       $"{pos}\t"
                                       // + $"{profitLoss.ToString("F")}\t" +
                                       //$"{GetTradeType(close)}\t" +
                                       //$"{close.TimeFrame}\t" +
                                       //$"{script}" +
                                       //$"{s}"
                                       );
                                }

                                sb.Append("\n");
                            }
                        }
                    }

                    #endregion

                    sb.Append(SPACER);
                 
                    #region waiting trades
                    //e.g. Waiting: Binance ALGOBTC  
                    sb.Append("Waiting trades:\n");

                    foreach (var trade in waitingTrades.OrderBy(x => x.ExchangeId).ThenBy(x => x.SymbolName))
                    {
                        var pos = trade.Position == TradePosition.LONG ? "long" : "short";
                        sb.Append(string.Concat(

                            Pad20(trade.SymbolName),
                            Pad12($"({GetTradeType(trade)})\t"),
                            $"{pos}",
                            "\n"));
                    }

                    #endregion

                    sb.Append(SPACER);

                    #region closed trades
                    sb.Append("Closed trades:\n");
                    //sb.Append("\tLeverage\tAmount\tEntry: Date\tTime\tPrice\tBalance\tExit: Date\tTime\tPrice\tBalance\tEquity\tResult\tResult Leveraged\tEquity result\tDollar result\tSide\tTF\t$ P/L\tUSDGBP\t£ P/L\n");
                    //this does not really line up so omit it
                    foreach (var close in closedOrders)
                    {
                        var s = close.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE ? "" : "Test closed:  ";
                        var entry = db.Trades.Where(x => x.TradeReference == close.TradeReference && x.Action == TradeAction.ENTRY).FirstOrDefault();
                        var order = db.Trades.Where(x => x.TradeReference == close.TradeReference && x.Action == TradeAction.ORDER).FirstOrDefault();

                        if (entry != null)
                        {
                            var symbolName = SymbolNames.GetBaseAssetForSymbol(close.SymbolName) ?? close.SymbolName;
                            if (string.IsNullOrEmpty(symbolName))
                            {
                                symbolName = close.SymbolName;
                            }

                            //bitmex has a close quantity of $ (contract value * leverage) and a close price in BTC $ value
                            //binance has a close quantity of BTC and a close price of BTC $ value
                            double entryEquity = 0;
                            double closeEquity = 0;
                            double prevEquity = 0;
                            switch (TradeEngine.EXCHANGE)
                            {
                                case ExchangeId.BINANCE:
                                    entryEquity = entry.Quantity * entry.Price;
                                    closeEquity = close.Quantity * close.Price;
                                    break;
                            }

                            //display intermediate trade result
                            //if (TradeEngine.EXCHANGE == ExchangeId.BITMEX)
                            //{
                                if (prevClose != null)
                                {
                                    var _profit = entry.Balance - prevClose.Balance;
                                    var _result = (entry.Price / prevClose.Price - 1) * 100;
                                    var _gbp = _profit * entry.ExchangeRate;

                                    sb.Append(
                                        "BTC\t" +
                                        "\t" +
                                        "\t" +
                                        $"{prevClose.Timestamp:d}\t" +
                                        $"{prevClose.Timestamp:t}\t" +
                                        $"{PriceFormatter.Format(prevClose.Price)}\t" +
                                        $"{prevClose.Balance.ToString("F2")}\t" +
                                        $"{entry.Timestamp:d}\t" +
                                        $"{entry.Timestamp:t}\t" +
                                        $"{PriceFormatter.Format(entry.Price)}\t" +
                                        $"{entry.Balance:F2}\t" +
                                        //"\t" +
                                        $"{_result:F2}%\t" +
                                        //"\t" +
                                        "\t" +
                                        "\t" +
                                        "\t" +
                                        "\t" +
                                        $"{_profit:F4}\t" +
                                        $"{entry.ExchangeRate:F6}\t" +
                                        $"{_gbp:F2}"
                                    );
                                    sb.Append('\n');
                                }
                                prevClose = close;
                            //}

                            var leverage = close.Leverage == 0 ? 1 : close.Leverage; //just to be safe
                            var result = close.Result; //already adjusted for fees

                            var balanceResult = entry.Balance > 0 ? (close.Balance / entry.Balance - 1) * 100 : 0;
                            var profit = close.Balance - entry.Balance;
                            var pos = close.Position == TradePosition.LONG ? "long" : "short";
                            //var stopResult = order != null ? order.Result.ToString("F1") : "--"; //Order.Result holds the stop percent, I think
                            var leveragedResult = result * leverage;
                            var gbp = profit * close.ExchangeRate;
                            //prevEquity = close.Equity;
                            sb.Append(
                                $"{symbolName}\t" +
                                $"{leverage:F1}\t" +
                                $"{Math.Abs(entryEquity / leverage).ToString("F")}\t" +
                                $"{entry.Timestamp:d}\t" +
                                $"{entry.Timestamp:t}\t" +
                                $"{PriceFormatter.Format(entry.Price)}\t" +
                                $"{entry.Balance:F2}\t" +
                                $"{close.Timestamp:d}\t" +
                                $"{close.Timestamp:t}\t" +
                                $"{PriceFormatter.Format(close.Price)}\t" +
                                $"{close.Balance:F2}\t" +
                                $"{result:F}%\t" +
                                $"{leveragedResult:F2}%\t" +
                                $"{balanceResult:F}%\t" +
                                $"{pos}\t" +
                                $"{close.TimeFrame}\t" +
                                $"{profit:F}\t" +
                                $"{close.ExchangeRate:F6}\t" +
                                $"{gbp:F2}"
                            );
                            sb.Append('\n');
                        }
                    }

                    var total = closedOrders.Sum(x => x.Result);
                    sb.Append(string.Concat("Total: ", total.ToString("F"), "%\n"));

                    #endregion
                    sb.Append("Always check balance after trade closes, this is the figure that profit is calculated from for tax purposes\n");
                    sb.Append("If using mobile, always enable RDP (Settings -> System) and disable when finished\n");
                    sb.Append(SPACER);

                    MainObj.TradeSummary = sb.ToString();

                    //var activeTrades = allTrades.Where(x => x.Action == TradeAction.ORDER && x.GlobalStatus == TradeGlobalStatus.ACTIVE).ToList();
                    return (enteredTrades, waitingTrades);
                });

                return ret;
            }
            catch (Exception ex)
            {
                LogConnection.WriteLine("ActiveTradeSummary exception: " + ex.ToString());
                return (null, null);
            }



        }

        internal static async Task ClearClosedTrades(object param)
        {
            int.TryParse(param as string, out int time);
            LastClosedTimeToShow = DateTime.UtcNow.AddHours(-time);
            //LastClosedTradesTimeframe = time;
            await Update(null);
        }

        static string Pad12(string s)
        {
            return s != null ? s.PadRight(12) : string.Empty;
        }

        static string Pad20(string s)
        {
            return s != null ? s.PadRight(20) : string.Empty;
        }
    }
}
