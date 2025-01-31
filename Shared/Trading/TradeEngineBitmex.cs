using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;
using BitMEX;
using OpenExchangeRates;
using System.Transactions;
using System.Windows.Media.Animation;
using static BitMEX.BitMEXApi;


namespace AlgoTrader.Shared
{
    public class TradeEngineBitmex : TradeEngine
    {

        /*
        * Very simple in bitmex: specify symbol, and either a positive or negative order quantity or a positive quantity and the Order Side (Buy, Sell)
        * When an order is filled, a position becomes open
        * Adding a filled order will just update the quantity in the open position.
        * So can have orders of these types: Market, Limit, Stop, StopLimit, MarketIfTouched, LimitIfTouched, Pegged. 
        * Defaults to 'Limit' when price is specified. Defaults to 'Stop' when stopPx is specified. Defaults to 'StopLimit' when price and stopPx are specified.
        * Market orders will fill straight away, others will remain open until filled
        * Open orders can just be cancelled with the order id.
        * */

        /* 
         * Example of leverage: 
         * Set leverage < 1, this requires trading a proportionally SMALLER amount to get the cost to match the order value
         * Set leverage > 1, this requires trading a proportionally LARGER amount to get the cost to match the order value
         * 
         * Order value: .3333 XBT
         * 
         * Leverage     Cost        Liquidation     Liquidation difference
         * 0.1x         3.3366      2728            90.1%
         * 0.2x         1.3349      6004            80%
         * 0.5x         0.6677      10011           66.6%
         * 1x           0.3341      15026           50%
         * 2x           0.1673      20046           33.3%
         * */

        /*
         * Cross margin:
         * 
         * You can trade this on 100x, by setting the purchase amount to the correct value (1/100 order value) to achieve the same 
         *  as trading the entire amount on 1x, but this would incur a very close liquidation price but would allow for trading multiple symbols
         *  */


        #region Trading parameters
        public static string KEY { get; internal set; }
        public static string SECRET { get; internal set; }
        public static string DOMAIN { get; internal set; }

        /*
        public static double DEFAULT_FLOOR_OFFSET_PERCENT { get; internal set; }
        public static double MIN_LEVERAGE_1 { get; internal set; } = 2;
        public static double MAX_LEVERAGE_1 { get; internal set; } = 2;
        public static double LEVERAGE_STOP_MIN_VALUE_1 { get; set; } = 1; 
        public static double LEVERAGE_STOP_MAX_VALUE_1 { get; set; } = 2;
        public static double MIN_LEVERAGE_4 { get; internal set; } = 2;
        public static double MAX_LEVERAGE_4 { get; internal set; } = 2;
        public static double LEVERAGE_STOP_MIN_VALUE_4 { get; set; } = 1;
        public static double LEVERAGE_STOP_MAX_VALUE_4 { get; set; } = 2;
        */

        public static double MIN_LEVERAGE_SHORT { get; internal set; } = 1;
        public static double MIN_LEVERAGE_LONG { get; internal set; } = 1.2;
        //public static double DEFAULT_LEVERAGE { get; internal set; } = 1;
        public static double DEFAULT_LEVERAGE { get; internal set; } = 2;



        #endregion

        BitMEXApi bitmexAPI;
        const double fiatDivisor = 1000000;
        const double XBTdivisor = 100000000;

        #region Init

        public TradeEngineBitmex(string symbolName, int timeInterval) : base(symbolName, timeInterval) { }

        public override void Init(int _timeInterval)
        {

            base.Init(_timeInterval);
            bitmexAPI = new BitMEXApi(KEY, SECRET);

            ////do a timed check to make sure this has synchronised correctly
            //var timer = new System.Timers.Timer(2000)
            //{
            //    AutoReset = false
            //};
            //timer.Start();
            //timer.Elapsed += (o, e) =>
            //{
            //    var v = GetClosePrice("cb2a1585-3f4c-45ad-82fa-990492052be1");
            //};


        }
        #endregion

        _Trade GetOppositeOrder(TradePosition position)
        {
            var oppositeSide = position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
            return AllTrades.FirstOrDefault(x =>
                                            x.Action == TradeAction.ORDER &&
                                            x.Position == oppositeSide &&
                                            x.ItemStatus == TradeItemStatus.OPEN &&
                                            x.GlobalStatus == TradeGlobalStatus.ACTIVE);
        }
        protected override void EnterTradeOnSystem_ExchangeSpecific_Synchronised()
        {
            //todo - create a complete quote asset trade, with the opening balance from the last closed trade
            #region create quote asset trade

            #endregion

            #region Set flip from opposing trade

            //if there is an opposing trade:
            // 1) remove any stop and floor from this trade
            // 2) convert any opposing trade stop to waiting stop
            // 3) convert opposing trade to flip order

            //var oppositeSide = Order.Position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
            var firstOppositeOrder = GetOppositeOrder(Order.Position);
            if (firstOppositeOrder != null)
            {
                // 1) remove any stop and floor from this trade since the opposing order will become a flip order stop and any other stop will either 
                // invalidate this or be left orphaned when this triggers
                var stop = AllTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.STOP &&
                                    x.TradeReference == Order.TradeReference);
                if (stop != null)
                {
                    //remove any line
                    var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == stop.Id);
                    if (stopLine != null)
                    {
                        SetUpdateRequest(stopLine, TradeUpdateRequest.FORCE_DELETE);
                    }

                    SetUpdateRequest(stop, TradeUpdateRequest.FORCE_DELETE);
                }
                
                var floor = AllTrades.FirstOrDefault(x =>
                                    x.Action == TradeAction.FLOOR &&
                                    x.TradeReference == Order.TradeReference);
                if (floor != null)
                {
                    SetUpdateRequest(floor, TradeUpdateRequest.FORCE_DELETE);
                }

                // 2) convert any opposing trade stop to waiting stop and remove any opposite take profit, ceiling or break even trades
                List<_Trade> oppTrades = [.. AllTrades.Where(x => x.TradeReference == firstOppositeOrder.TradeReference)];

                var oppStop = oppTrades.FirstOrDefault(x => x.Action == TradeAction.STOP);
                var oppFloor = oppTrades.FirstOrDefault(x => x.Action == TradeAction.FLOOR);
                if (oppStop != null)
                {
                    //use this stop
                    oppStop.TradeReference = Order.TradeReference;
                    oppStop.Type = TradeType.STOP_WAITING;
                    oppStop.Action = TradeAction.WAITING_STOP;
                    SetUpdateRequest(oppStop);

                    var oppStopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeReference == oppStop.TradeReference);
                    if (oppStopLine != null)
                    {
                        oppStopLine.TradeReference = Order.TradeReference;
                        oppStopLine.Type = LineType.SUPPORT;
                        SetUpdateRequest(oppStopLine);
                    }

                    //remove any floor
                    if (oppFloor != null)
                    {
                        SetUpdateRequest(oppFloor, TradeUpdateRequest.FORCE_DELETE); //we don't want a floor with a stop, the stop will have converted but the floor will not be usable
                    }
                }
                else if (oppFloor != null)
                {
                    //use this as stop
                    oppFloor.TradeReference = Order.TradeReference;
                    oppFloor.Type = TradeType.STOP_WAITING;
                    oppFloor.Action = TradeAction.WAITING_STOP;//convert it to a Stop, although there is no stop line
                    SetUpdateRequest(oppFloor);
                }
                else
                {
                    //add a waiting stop at the original order price
                    var trade = Order.Clone();
                    trade.Id = 0; //so it will be added to db
                    trade.ItemId = null;
                    trade.ItemStatus = TradeItemStatus.OPEN;
                    trade.Action = TradeAction.WAITING_STOP;
                    trade.Timestamp = DateTime.UtcNow;
                    trade.TimeFrame = TimeFrame;

                    //this may be an existing Moving Average stop, if so just changing the Type will stop the moving average update loop (see Order::ExecuteStop_synchronised())
                    trade.Type = TradeType.STOP_WAITING;
                    //flip side if this is a waiting stop
                    trade.Quantity = Order.Quantity;
                    trade.Position = Order.Position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
                    trade.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;
                    SetUpdateRequest(trade);
                }

                //remove any take profits, etc.
                var oppTP = oppTrades.FirstOrDefault(x => x.Action == TradeAction.TAKE_PROFIT);
                var oppCeiling = oppTrades.FirstOrDefault(x => x.Action == TradeAction.CEILING);
                var oppBE = oppTrades.FirstOrDefault(x => x.Action == TradeAction.BREAK_EVEN);
                if (oppTP != null) SetUpdateRequest(oppTP, TradeUpdateRequest.FORCE_DELETE);
                if (oppCeiling != null) SetUpdateRequest(oppCeiling, TradeUpdateRequest.FORCE_DELETE);
                if (oppBE != null) SetUpdateRequest(oppBE, TradeUpdateRequest.FORCE_DELETE);


                // 3) convert opposing trade to flip order (which is also a stop)
                var line = AllLinesForSymbol.FirstOrDefault(x => x.TradeReference == firstOppositeOrder.TradeReference);
                if (line != null)
                {
                    line.TradeReference = Order.TradeReference;
                    line.Type = LineType.SUPPORT;
                    SetUpdateRequest(line);
                }

                firstOppositeOrder.TradeReference = Order.TradeReference;
                firstOppositeOrder.Action = TradeAction.STOP;
                firstOppositeOrder.Type = TradeType.FLIP_ORDER;
                firstOppositeOrder.Position = Order.Position; //because a flip is the same side as the order it is flipping.
                SetUpdateRequest(firstOppositeOrder);

            }

            #endregion
        }


        //public override void RunTrade_exchangeSpecific()
        //{
        //}

        void WriteDeliniatedMessage(string msg)
        {
            LogTrades.WriteLine($"\n**************************************\n{msg}\n**************************************");
        }


        #region Leverage and order quantity

        public static double CalculateLeverageFromStop(double stopPC, TradePosition position)
        {
            var minLeverage = position == TradePosition.LONG ? MIN_LEVERAGE_LONG : MIN_LEVERAGE_SHORT;
            stopPC = Math.Abs(stopPC); //to be safe
            if (stopPC == 0)
            {
                LogTrades.WriteLine($"No stop to calculate leverage from, using default leverage of {DEFAULT_LEVERAGE}");
                return DEFAULT_LEVERAGE;
            }

            stopPC += STOP_LINE_PRICE_GAP_PC; //because this will be added in when it stops out, if it is a stop line (just use it anyway)

            //var leverage = MAX_STOP_LOSS / stopPC; //this is the default leverage here

            //if leverage is outside of min and max range, clamp it
            //leverage = Math.Max(Math.Min(leverage, MAX_LEVERAGE), minLeverage);


            //clamp max loss to a limit by limiting leverage, but keep leverage to the minimum value
            //don't want every loss (initially set) to be the same large default, it is very bothersome
            //but also don't want a trade to accidentally set a huge initial loss of, say, 3 * 8 = 24% + spread + fees
            //e.g. DEFAULT_LEVERAGE = 3, MAX_STOP_LOSS = 9
            //stop = 1, leverageScale = 1 * 3 / 9 = 0.333, leverage = 3 / 0.333 = 9
            //stop = 2, leverageScale = 2 * 3 / 9 = 0.667, leverage = 3 / 0.667 = 4.5
            //stop = 3, leverageScale = 3 * 3 / 9 = 1, leverage = 3 / 1 = 3
            //stop = 6, leverageScale = 6 * 3 / 9 = 2, leverage = 3 / 2 = 1.5
            //stop = 9, leverageScale = 9 * 3 / 9 = 3, leverage = 3 / 3 = 1
            var leverageScale = stopPC * DEFAULT_LEVERAGE / MAX_STOP_LOSS;           
            var leverage = Math.Min(DEFAULT_LEVERAGE / leverageScale, DEFAULT_LEVERAGE);



            //This algorithm attempts to set the leverage so the maximum allowed loss is always the same.
            //For small initial stops, the leverage may be outside of the max. allowed leverage, so that will clamp the leverage and reduce the
            // effective maximum loss (i.e. to hit the max. allowed loss, a high leverage may be needed)
            //For large initial stops, the leverage may be too small, so it will be increased to the minimum - however, this will 
            // INCREASE the maximum potential loss.

            //With long positions, 1X leverage will merely track the rising value of the underlying asset, so no profit can be taken.
            //With short positions, 1X is fine as the sell will purchase the underlying asset at a higher level of equity than it was sold for
            //e.g. Long - market rises by 10%, XBT sold for $100, XBT bought for $110, but BTC value has gone up by 10% anyway
            //Short - market falls by 10%, XBT sold for $100, XBT bought for $110, so more XBT purchased than was originally sold

            return leverage;

            #region Auto-calculate leverage (unused)
            /*
    * Leverage is scaled depending on the min and max stop levels, and the min and max allowable leverage
    * The max stop level is not necessarily the maximum actually used, but the max. which would generally be used. However, this value does not
    *  really affect the final result by very much when it is greater than 3%
    * Calculation:
    * Let stop range from Smin to Smax, and leverage from Lmin to Lmax
    * So the scale factor for the leverage is what the stop (S) can be divided into to get the leverage (L)
    * e.g. if the stop is 0.5% and the max leverage is 5, the scale (C) is 2.5, i.e. 2.5 / 0.5 = 5
    * So the scale factor is S * L
    * Scale will range from Cmin to Cmax: Cmin = Smin * Lmax, Cmax = Smax * Lmin
    *                                 * 
    * For a given stop value of X:
    * Let P be the ratio of the value of X to the range of S, so if Smin = 1, Smax = 2 and X = 1.5, P = 0.5 i.e it is half way between Smin and Smax
    * P = (X - Smin) / (Smax - Smin)
    * Calculate a value of C for this:
    * C = (Cmax - Cmin) * P + Cmin
    * Therefore the leverage L = C / X
    * 
    * Full formula: L = ((Cmax - Cmin) * (X - Smin) / (Smax - Smin) + Cmin) / X
    * 
    * */

            /* Examples:
             * For a min. stop of 0.7:
             * Stop     Leverage
             * 0.7      5
             * 1        3.8
             * 2        2.4
             * 3        2.0
             * 4        1.77
             * 5        1.6
             * 6        1.54
             * 
             * For a min. stop of 1.0
             * 1        5
             * 2        2.75
             * 3        2
             * 4        1.63
             * 5        1.4
             * 6        1.25

            /*
             * NOTE - if the stop percent is less than LEVERAGE_STOP_MIN_VALUE, the calculated leverage will be greater than MAX_LEVERAGE so it will be clamped
             * Also, the stop will then be reset to 
             * */
            /*
            double leverage = 2;
            var X = stopPC;
            var Smin = LEVERAGE_STOP_MIN_VALUE_1;
            var Smax = LEVERAGE_STOP_MAX_VALUE_1;
            var Lmin = MIN_LEVERAGE_1;
            var Lmax = MAX_LEVERAGE_1;
            var Cmin = Smin * Lmax;
            var Cmax = Smax * Lmin;
            if (Smax - Smin > 0)
            {
                var P = (X - Smin) / (Smax - Smin);
                var C = (Cmax - Cmin) * P + Cmin;
                leverage = C / X;
            }
            else
            {
                LogTrades.WriteLine($"Max and Min stop values are the same, cannot set leverage from stop. Using current Leverage of {Leverage}");
            }

            leverage = SetMinLeverage(leverage, position);

            LogTrades.WriteLine($"Setting leverage of {leverage}");

            try
            {
                //note, if leverage is < 0 this will fail with an exception, so leverage is always 1 or more.
                MainObj.SetLeverageMenuState((int)leverage); //NOTE, calling Application.Current.Dispatcher.Invoke() within this thread causes a lock condition
            }
            catch { }

            return leverage;
            */
            #endregion

        }

        public static double CalculateStopFromLeverage(double leverage, TradePosition position)
        {
            leverage = position == TradePosition.LONG
                ? Math.Max(leverage, MIN_LEVERAGE_LONG)
                : Math.Max(leverage, MIN_LEVERAGE_SHORT);

            var stop = MAX_STOP_LOSS / leverage; //this obviously cannot be greater than the target stop loss
            stop = Math.Max(stop, STOP_MIN_VALUE); //use this minimum stop value as a lower limit
            return stop;
        }

        //inverse of the above
        #region Auto-calculate stop from leverage (unused)
        /*
        double CalculateStopFromLeverage( double leverage, TradePosition position )
        {

            
            leverage = SetMinLeverage(leverage, position);
    double X = LEVERAGE_STOP_MIN_VALUE_1;
    var Smin = LEVERAGE_STOP_MIN_VALUE_1;
    var Smax = LEVERAGE_STOP_MAX_VALUE_1;
    var Lmin = MIN_LEVERAGE_1;
    var Lmax = MAX_LEVERAGE_1;
    var Cmin = Smin * Lmax;
    var Cmax = Smax * Lmin;
    if (leverage - Cmax + Cmin != 0)
    {
        X = (Cmin * Smax - Cmax * Smin) / (leverage * (Smax - Smin) - Cmax + Cmin);  
    }
    MainObj.SetLeverageMenuState((int)leverage); //in case it has been clamped
    return X;
    
        }
        */
        #endregion

        (double, double) CalculateOrderQuantity(double walletAmount, double lastQuoteValue, double leverage, double lotSize, TradePosition position)
        {
            //re-calculate the order quantity, as the returned amount from the stop should be higher than the previous order
            var orderQuantity = walletAmount / XBTdivisor * lastQuoteValue;
            orderQuantity *= position == TradePosition.LONG ? 1 : -1;
            orderQuantity = BitMEXApi.GetTickSize(symbolName, orderQuantity);


            //note, the stop quantity is reversed when the order is sent to the exchange, but it is a positive quantity here
            if (TradeEngine.RESTRICTED_TRADE_VALUE > 0)
            {
                orderQuantity = position == TradePosition.LONG ? TradeEngine.RESTRICTED_TRADE_VALUE : -TradeEngine.RESTRICTED_TRADE_VALUE;
            }
            if (TradeEngine.EXCHANGE == Shared.Enums.ExchangeId.BITMEX && MainObj.BACKTEST == true)
            {
                //testnet trading - always limit to $100
                orderQuantity = position == TradePosition.LONG ? 100 : -100;
            }


            //if leverage is 2X and you want to spend $100, you need to buy / sell $200
            var leveragedOrderQuantity = orderQuantity * leverage;

            //must be multiple of lot size which is $100
            leveragedOrderQuantity = (int)(leveragedOrderQuantity / lotSize) * lotSize;

            //calculate the actual leverage from this

            var actualLeverage = leveragedOrderQuantity / orderQuantity;
            orderQuantity *= actualLeverage; //this is the dead amount to send to the exchange, to consume 100% wallet balance

            //a higher margin will consume less equity - so increase margin to cover any fees, rounding errors etc.
            actualLeverage *= (1 + takerFee * 100);
            actualLeverage *= 1.005; //plus 0.5% to be safe
            actualLeverage = Math.Round(actualLeverage, 3); //round to 3 decimal places, don't really want a long decimal number

            //this way, the sumitted order quantity / actualLeverage will be slightly less than the wallet amount, but if it was more then the order would fail
            // because there would not be enough equity in the account to cover the order at that leverage
            //e.g.
            //order quantiy before leverage: 500
            //leverage: 2.6356
            //order quantity after leverage: 1317.8 -> 1300
            //actual leverage: 2.6 -> 2.613
            // - so an order of 1300 @ 2.613 will require equity of $497.5, plus taker fee of 0.075% = $497.87

            return (orderQuantity, actualLeverage);
        }

        bool SetLeverageOnExchange(string symbolName, double leverage)
        {
            return bitmexAPI.SetLeverage(symbolName, leverage);
        }
        #endregion

        //The entry point for all trades
        //This method is only called from a single point, which is within a synchronised block.
        //So it is vital that this does NOT start another synchronised section.
        public override async void StartEntryOrClose_Synchronised()
        {
            void _CancelStop(_Trade trade)
            {
                if (trade != null && !USE_SIMULATED_EXCHANGE)
                {
                    //could just delete all orders with this id instead, or can delete a list of orders (see docs)
                    var tradeClose = bitmexAPI.DeleteOrder(trade.ItemId)?.FirstOrDefault();
                    if (tradeClose != null)
                    {
                        if (!string.IsNullOrEmpty(tradeClose.OrderRejectionReason))
                        {
                            WriteDeliniatedMessage($"Failed to remove stop:\n{tradeClose.OrderRejectionReason}");
                        }
                        else
                        {
                            SetUpdateRequest(trade, TradeUpdateRequest.DELETE);
                        }
                    }
                }
            }

            //script name will already be set, use this to determine correct action
            //upon completion, clear the script so the trades will then be drawn as not pending
            if (Entry != null)
            {
                switch (Entry.ExchangeMethodScript)
                {
                    case NEW_ORDER_SCRIPT:

                        entryTimeStamp = DateTime.UtcNow;

                        //since we are not removing orders if the exchange returns as order closed (no open positions) or stop filled, because these things
                        // can happen in error (and has been observed), there may still be stops on the exchange. These cannot increase positions, but MUST be
                        // removed prior to placing any more orders.
                        LogTrades.WriteLine("Deleting any residual stops...");
                        bitmexAPI.DeleteAllOrders(symbolName); //not asynchronous, so will block until completed.
                        LogTrades.WriteLine("All stops and orders removed.");

                        void _KillOrder(string msg, bool interrupted = false)
                        {
                            WriteDeliniatedMessage(msg);
                            KillEntryOrCloseOrder(interrupted);
                        }
                        //this will just be a 95% order for now

                        //var wallet = bitmexAPI.GetWalletSingleCurrency(SymbolNames.GetCurrencyForSymbol(symbolName));
                        var wallet1 = bitmexAPI.GetWallet();
                        if (wallet1 != null)
                        {
                            var amount = wallet1.Where(x => x.Currency == SymbolNames.GetCurrencyForSymbol(symbolName)).Select(x => x.Amount).FirstOrDefault();
                            LogTrades.WriteLine($"GetWalletSingleCurrency() returned {amount} for new order");

                            //save balance and exchange rate before entering trade
                            Entry.Balance = GetAccountEquity(wallet1);
                            SetUpdateRequest(Entry);

                            //if this has just restarted, will try to enter any trades immediately but the price won't be available from the websocket yet.
                            //so use the REST api instead
                            var lastQuoteValue = USE_SIMULATED_EXCHANGE ? 0 : bitmexAPI.GetLastPrice(symbolName); //can't get price feed from testnet, only live
                            if (lastQuoteValue == 0)
                            {
                                (var v2, var s1) = GetValueForSymbol(symbolName); //note that the symbol name does not use the fiat asset symbol

                                if (s1 != null)
                                {
                                    LogTrades.WriteLine($"Error getting balance: {s1}");
                                    break;
                                }
                                lastQuoteValue = v2;
                            }

                            #region Set leverage
                            //the problem here is the stops are only calculated AFTER the trade has entered, including any reversal trades.
                            //So that needs to be done first


                            //need the closest of the current stop or opposing trade. If neither found, use the default floor value
                            double stopPC = 0;

                            var trades = AllTrades.Where(x => x.TradeReference == Order.TradeReference);

                            Ceiling = trades.FirstOrDefault(x => x.Action == TradeAction.CEILING);
                            TakeProfit = trades.FirstOrDefault(x => x.Action == TradeAction.TAKE_PROFIT);



                            var _stop = trades.FirstOrDefault(x => x.Action == TradeAction.STOP);
                            if (_stop != null)
                            {
                                var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == _stop.Id);
                                if (stopLine != null)
                                {
                                    (var stopPrice, _) = stopLine.GetPriceAtTime(DateTime.UtcNow, true);
                                    var stopDiff = MainObj.GetPriceDiffPercent(currentDataItem, stopPrice, Order.Position) * 100;
                                    if (stopPC == 0 || stopDiff < stopPC) //use smallest stop
                                    {
                                        stopPC = stopDiff;
                                    }
                                }
                                Stop = _stop;
                            }
                            var _floor = trades.FirstOrDefault(x => x.Action == TradeAction.FLOOR);
                            if (_floor != null)
                            {
                                var floorDiff = MainObj.GetPriceDiffPercent(currentDataItem, _floor.Price, Order.Position) * 100;
                                if (stopPC == 0 || floorDiff < stopPC) //use smallest stop
                                {
                                    stopPC = floorDiff;
                                }
                                Floor = _floor;
                            }

                            //if there is no stop, take the opposing order as one instead
                            if (stopPC == 0)
                            {
                                var _oppOrder = GetOppositeOrder(Order.Position);
                                if (_oppOrder != null)
                                {
                                    var orderLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == _oppOrder.Id);
                                    if (orderLine != null)
                                    {
                                        (var oppOrderPrice, _) = orderLine.GetPriceAtTime(DateTime.UtcNow, true);
                                        stopPC = MainObj.GetPriceDiffPercent(currentDataItem, oppOrderPrice, Order.Position) * 100;
                                    }
                                }
                            }

                            //the current stop will be removed anyway if there is a flip order, as that becomes the new stop; 
                            //so orders like this will tend to have a much smaller leverage


                            var lotSize = GetLotSize();
                            var leverage = CalculateLeverageFromStop(stopPC, Entry.Position);
                            LogTrades.WriteLine($"Stop is at {stopPC} at entry time");

                            (var orderQuantity, var actualLeverage) = CalculateOrderQuantity(amount, lastQuoteValue, leverage, lotSize, Entry.Position);

                            if (
                               (Entry.Position == TradePosition.LONG && actualLeverage < MIN_LEVERAGE_LONG) ||
                               (Entry.Position == TradePosition.SHORT && actualLeverage < MIN_LEVERAGE_SHORT))
                            {
                                var s = Entry.Position == TradePosition.LONG ? "long" : "short";
                                _KillOrder($"Leverage of {actualLeverage} was too small to make {s} trade worthwhile, exiting");
                                SetUpdateRequest(Entry, TradeUpdateRequest.FORCE_DELETE);
                                break;
                            }

                            Leverage = actualLeverage;

                            //save the calculated leverage, even if we are not using it
                            Order.Leverage = Entry.Leverage = Leverage;
                            Order.Result = AdjustResultForFees(stopPC);

                            //the correct order quantity is determined by leverage
                            if (!SetLeverageOnExchange(symbolName, Leverage))
                            {
                                _KillOrder("Failed to set leverage");
                                break;
                            }

                            #endregion

                            if (USE_SIMULATED_EXCHANGE)
                            {
                                EnterTradeOnSystem_synchronised((decimal)lastQuoteValue, orderQuantity, TradeExchangeStatus.ENTERED_ON_EXCHANGE);
                            }
                            else
                            {
                                Order.ExchangeStatus = Entry.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;

                                var orderEntry = bitmexAPI.SendOrder(symbolName, orderQuantity);
                                if (orderEntry != null)
                                {
                                    var orderValid = true;
                                    var orderError = string.Empty;
                                    if (!string.IsNullOrEmpty(orderEntry.OrderStatus) && orderEntry.OrderStatus != "Filled")
                                    {
                                        orderValid = false;
                                        orderError += $"Order was not filled: {orderEntry.OrderRejectionReason}\n";
                                    }
                                    //the returned orderQuantity will always be positive, but for short positions we need to save it as the original quantity (sign) so stops will be correctly signed
                                    if (!string.IsNullOrEmpty(orderEntry.OrderRejectionReason))
                                    {
                                        orderValid = false;
                                        orderError += $"Order was rejected: {orderEntry.OrderRejectionReason}\n";
                                    }
                                    if (orderEntry.AveragePrice == 0)
                                    {
                                        orderValid = false;
                                        orderError += "Order returned zero price\n";
                                    }
                                    if (orderEntry.OrderQuantity == 0)
                                    {
                                        orderValid = false;
                                        orderError += "Order returned zero cumulative quantity\n";
                                    }

                                    if (!orderValid)
                                    {
                                        _KillOrder(orderError, true);
                                        break;
                                    }
                                    var pos = Entry.Position == TradePosition.LONG ? "Long" : "Short";
                                    LogTrades.WriteLineOrder($"\n{pos} order FILLED for {Entry.SymbolName} at {DateTime.UtcNow}");

                                    Order.ItemId = Entry.ItemId = orderEntry.OrderID;

                                    //at this point, trade is entered live
                                    EnterTradeOnSystem_synchronised((decimal)orderEntry.AveragePrice, orderQuantity, TradeExchangeStatus.ENTERED_ON_EXCHANGE);
                                }
                            }
                            EndEntryOrCloseScript();

                            //set stop
                            #region Get liquidation price (not always immediately available) unused
                            /*
                    double liquidationPrice;
                    if (USE_SIMULATED_EXCHANGE)
                    {
                        liquidationPrice = Entry.Position == TradePosition.LONG ? Entry.Price * 0.8 : Entry.Price / 0.8 ;
                    }
                    else
                    {
                        try
                        {
                            //exchange doesn't always return open position straight away, which results in no stop being set.
                            //providing order has been accepted, should always set stop anyway. Will have exited this loop before now if order was not accepted.
                            //liquidation price, however, is not available if the position is not available as this is not returned in the initial order JSON

                            var position = bitmexAPI.GetOpenPositions(symbolName);
                            if (position == null || position.Count == 0)
                            {
                                WriteDeliniatedMessage("Failed to set liquidation stop!!!!");
                                break;
                            }
                            liquidationPrice = (double)position[0].LiquidationPrice;
                        }
                        catch 
                        {
                            WriteDeliniatedMessage("Failed to set liquidation stop (error thrown)!!!!");
                            break;
                        }
                    }

                    Order.LiquidationPrice = Entry.LiquidationPrice = liquidationPrice; //update request already set


                            */
                            #endregion


                            var stop = tradeEngine.GetTradeForTradeReference(TradeAction.STOP, Entry.TradeReference);
                            if (stop != null)
                            {
                                Stop = stop;
                                Stop.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE; //or will not be entered by base method
                                Stop.Quantity = -Entry.Quantity;
                                SetStopOnExchange_Synchronised(Stop);
                                SetUpdateRequest(Stop);
                            }
                            else if (!AUTO_SET_TRAILING_STOP) //trailing stop will be subsequently set if this value is > 0, otherwise set a floor
                            {
                                //add a floor if there is not already one. The stop will be dynamically updated, so should not be dropped, but if there was an error which caused the stop to be misplaced or lost, a default floor is needed to avoid liquidation

                                var floor = tradeEngine.GetTradeForTradeReference(TradeAction.FLOOR, Entry.TradeReference);
                                if (floor != null)
                                {
                                    Floor = floor;
                                }
                                else
                                {
                                    Floor = Entry.Clone();

                                    var stopOffsetPC_Leveraged = STOP_MIN_VALUE;// CalculateStopFromLeverage(Entry.Leverage, Entry.Position);

                                    var floorPrice = Order.Position == TradePosition.LONG
                                                ? Entry.Price / (1 + stopOffsetPC_Leveraged / 100)
                                                : Entry.Price * (1 + stopOffsetPC_Leveraged / 100);

                                    Floor.Price = (double)floorPrice;
                                    LogTrades.WriteLine($"Setting Floor at default level of {STOP_MIN_VALUE}");

                                }

                                Floor.Action = TradeAction.FLOOR;
                                Floor.Quantity = -Entry.Quantity;
                                Floor.ItemId = null; //need a new ID for this as it is a new order.
                                Floor.ExchangeStatus = TradeExchangeStatus.ENTERING_ON_EXCHANGE;

                                SetStopOnExchange_Synchronised(Floor);
                                SetUpdateRequest(Floor);
                            }

                            using (DataContext db = new DataContext())
                            {
                                var tr = Entry.TradeReference;                    
                                var ex = (double)await GetGBPUSDRate();
                                var _entry = db.Trades.FirstOrDefault(x => x.TradeReference == tr && x.Action == TradeAction.ENTRY);

                                if (_entry != null )
                                {
                                    _entry.ExchangeRate = ex;
                                    db.SaveChanges();
                                }
                            }
                        }
                        break;

                    case CLOSE_ORDER_SCRIPT:

                        entryTimeStamp = DateTime.UtcNow;

                        if (Close == null)
                        {
                            _KillOrder($"No Close set in CLOSE_ORDER_SCRIPT for {symbolName}, cannot close trade");
                            break;
                        }

                        var tradeRef = Order.TradeReference;

                        LogTrades.WriteLine($"Closing order(s) for {symbolName}");

                        if (USE_SIMULATED_EXCHANGE)
                        {
                            //remove any floor
                            _CancelStop(Floor);
                            _CancelStop(Stop);
                            Entry.ExchangeMethodScript = Close.ExchangeMethodScript = null;
                            SetUpdateRequest(Entry);
                            (var v2, var s1) = GetValueForSymbol(symbolName);
                            LogTrades.WriteLine("Closing order on simulated exchange");
                            CloseTradeOnSystem_synchronised((decimal)v2);
                        }
                        else
                        {
                            var orderClose = bitmexAPI.CloseOrder(symbolName);
                            if (orderClose != null)
                            { 
                                LogTrades.WriteLine($"Order status of {orderClose.OrderStatus} received");

                                bool hasClosed = false;
                                if (orderClose.AveragePrice != null)
                                {
                                    LogTrades.WriteLine("Closing order with average price");
                                    hasClosed = true;
                                }
                                else if (orderClose.OrderClosedOnExchange)
                                {
                                    hasClosed = true;
                                    //LogTrades.WriteLine($"{orderClose.Text}");
                                    LogTrades.WriteLine($"Order was probably closed manually on exchange, closing trade on system. Exact close price not available");
                                }
                                else if (orderClose.OrderStatus == "Canceled")
                                {
                                    LogTrades.WriteLine("Order status was Canceled, no action taken");
                                }

                                if (hasClosed)
                                {
                                    //should pull the last transaction to get the correct close value here
                                    CloseTradeOnSystem_synchronised((decimal)currentDataItem.Close);

                                    //just delete all orders to be safe, this ensures no stops left behind.
                                    bitmexAPI.DeleteAllOrders(symbolName);

                                    if (Stop != null)
                                    {
                                        if (Stop.Type == TradeType.FLIP_ORDER)
                                        {
                                            DoFlip_Synchronised();
                                        }
                                    }
                                    try
                                    {
                                        //there seems to not be an Order or Close at this point, so the trade reference has been saved previously...
                                        var ex = (double)await GetGBPUSDRate();
                                        using DataContext db = new(); //Close can drop out of context after the await method call above
                                        var _close = db.Trades.FirstOrDefault(x => x.TradeReference == tradeRef && x.Action == TradeAction.CLOSE);
                                        if (_close != null)
                                        {
                                            _close.ExchangeRate = ex;
                                            db.SaveChanges();
                                        }
                                        UpdateAccountEquityForTrade(tradeRef);
                                    }
                                    catch {
                                        LogTrades.WriteLine("Error saving exchange rate data");

                                    }
                                }
                            }

                            LogTrades.WriteSpacer();
                        }
                        break;
                }
            }
        }

        async Task<decimal> GetGBPUSDRate()
        {
            try
            {
                //this is tested and it works. Will need to display the data in ActiveTradeSummary, the entry rate will be the exit rate for the intermediate BTC trade
                using var client = new OpenExchangeRatesClient("51581f4479b84a29921ca25369900524");
                var latestRatesResponse = await client.GetLatestRatesAsync();
                var gbp = latestRatesResponse.Rates.FirstOrDefault(x => x.Key == "GBP").Value;
                //convertResponse = await client.ConvertAsync("GBP", "USD", 1m); //1m is decimal  - this throws 'Forbidden' exception error, poss. a paid function
                return gbp;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        #region Stop 

        bool isManualSynchStop = false;
        bool isManualSynchFloor = false;
        public override void SynchStopsWithExchange_synchronised()
        {
            MainObj.RefreshConnector();

            InitForDataContext(() =>
            {
                if (Entry != null)
                {
                    if (Stop != null)
                    {
                        isManualSynchStop = true;
                        SetStopOnExchange_Synchronised(Stop);
                    }
                    if (Floor != null)
                    {
                        isManualSynchFloor = true;
                        SetStopOnExchange_Synchronised(Floor);
                    }
                }
                else
                {
                    //LogTrades.WriteLine("Not entered on exchange - cannot synchronise stops with exchange.");
                }
                //do a timed check to make sure this has synchronised correctly
                var timer = new System.Timers.Timer(2000)
                {
                    AutoReset = false
                };
                timer.Start();
                timer.Elapsed += (o, e) =>
                {
                    if (isManualSynchStop)
                    {
                        LogTrades.WriteLine("Failed to synchronise Stop with exchange");
                        isManualSynchStop = false;
                    }
                    if (isManualSynchFloor)
                    {
                        LogTrades.WriteLine("Failed to synchronise Floor with exchange");
                        isManualSynchFloor = false;
                    }
                };

            });
        }

        public override async void SetStopOnExchange_Synchronised(_Trade stop)
        {

            base.SetStopOnExchange_Synchronised(stop);

            if (USE_SIMULATED_EXCHANGE)
            {
                return;
            }

            var s = $"{stop.Type} ";
            s += stop.Action == TradeAction.STOP ? "stop" : "floor";

            if (stop.ExchangeStatus == TradeExchangeStatus.ENTERED_OFF_EXCHANGE && stop.Type != TradeType.STOP_WAITING)
            {
                LogTrades.WriteLine($"Did not set {s} on exchange, exchange status was {stop.ExchangeStatus} - you need to add a new stop!");
                stop.ExchangeStatus = TradeExchangeStatus.NO_WARNING;
                SetUpdateRequest(stop);
                return;
            }
            if (stop.Action == TradeAction.WAITING_STOP)
            {
                return;
            }

            //just an extra safety check...
            (var lastValue, var _) = GetValueForSymbol(symbolName);

            if (stop.Position == TradePosition.SHORT && stop.Price < lastValue || stop.Position == TradePosition.LONG && stop.Price > lastValue)
            {
                LogTrades.WriteLine($"Did not set {s} on exchange, price was wrong side of candle data");
                return;
            }

            if (stop.Quantity == 0 && Entry != null && Order.ActionValue != 0)
            {
                //this will be called from Order when setting stops automatically
                stop.Quantity = -Entry.Quantity;
                var __s = string.Empty;
                switch (stop.Action)
                {
                    case TradeAction.STOP:
                        __s = "Stop";
                        break;
                    case TradeAction.FLOOR:
                        __s = "Floor";
                        break;
                }

                LogTrades.WriteLine($"{__s} quantity was zero, {__s} not yet set, setting {__s}...");
            }


            if (stop.ExchangeStatus == TradeExchangeStatus.ENTERING_ON_EXCHANGE)
            {
                //trade is entering, run synchronously and allow enclosing InitForDataContext() to save changes
                try
                {
                    SendStopToExchange(stop);
                }
                catch (Exception ex)
                {
                }
            }
            else
            {
                //trade is running, update stop asynchronously using a new data context
                //however, this method will be called on both Stop and Floor, so only update the stop as the Floor is always flat
                if (stop.Action == TradeAction.STOP)
                {
                    await Task.Run(() =>
                    {
                        InitForDataContext(() =>
                        {
                            try
                            {
                                SendStopToExchange(Stop);
                            }
                            catch (Exception ex)
                            {
                            }
                        });

                    });
                }
            }

        }

        public void SendStopToExchange(_Trade st)
        {
            if (st == null || st.Quantity == 0) //can be zero if attempting to move a closed stop
            {
                return; //happens with locally placed order
            }
            bool isUpdating = st.ItemId != null;

            var orders = bitmexAPI.GetOrders("BTCUSD");
            BitMEXApi.Order orderStop = isUpdating
               ? bitmexAPI.AmendStopOrder(st)
               : bitmexAPI.SendOrder(symbolName, st.Quantity, "ReduceOnly", 0, st.Price);

            if (orderStop != null)
            {
                if (!string.IsNullOrEmpty(orderStop.OrderRejectionReason))
                {
                    if (!isUpdating) //if this is a new stop order and it gets rejected, remove the stop. Otherwise leave it with an error message
                    {
                        RemoveTradeFromLine_UI_synchronised(st, false);
                        SetUpdateRequest(st, TradeUpdateRequest.DELETE);

                        //st.ExchangeStatus = TradeExchangeStatus.ENTERED_OFF_EXCHANGE; //just reset to default
                        WriteDeliniatedMessage($"Stop or Floor order was rejected: {orderStop.OrderRejectionReason}");
                    }
                    else
                    {
                        WriteDeliniatedMessage($"Stop or Floor order was not updated: {orderStop.OrderRejectionReason}");
                    }
                    return;
                }
                else
                {
                    st.ItemId = orderStop.OrderID;

                    //the tick price calculation will return as a double, but the exchange tick price will be rounded for BTC
                    // - this will result in sending a new update request when one is not needed, which will return an error lots of times
                    // - and we want to keep showing errors in the log, so by setting the stop price to the actual stopPx value, this will be avoided
                    st.Price = (double)orderStop.StopTriggerPrice;
                    st.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;

                    SetUpdateRequest(st);
                    if (isManualSynchStop && st.Action == TradeAction.STOP)
                    {
                        LogTrades.WriteLine($"{st.Action} synchronised with Exchange");
                        isManualSynchStop = false;
                    }
                    if (isManualSynchFloor && st.Action == TradeAction.FLOOR)
                    {
                        LogTrades.WriteLine($"{st.Action} synchronised with Exchange");
                        isManualSynchFloor = false;
                    }
                    return;
                }
            }
            else
            {
                //this happens a lot, about once every 4 mins typically - probably because the stop price is unchanged and a new order is not required.
                //LogTrades.WriteLine($"Did not set {s} on exchange, orderStop was null at {DateTime.UtcNow:G}");
                return;
            }
        }

        protected override void RemoveStopFromExchange_Synchronised(_Trade stop)
        {
            if (stop == null)
            {
                return;
            }
            if (!(stop.Action == TradeAction.STOP || stop.Action == TradeAction.FLOOR))
            {
                return;
            }

            //if stop or floor is on the exchange, remove it

            bool canRemove = false;
            if (stop.ItemId != null)
            {

                var ret = bitmexAPI.DeleteOrder(stop.ItemId);

                if (ret != null && ret.Any() && ret[0].OrderStatus == "Canceled")
                {
                    canRemove = true;
                }
                else
                {
                    //stop may be missing from exchange, or there may be a different stop there which this one cannot delete.
                    LogTrades.WriteLine("Failed to remove stop or floor from the exchange, check exchange status!");
                }
            }
            else
            {
                //this should not be necessary, as a stop with no ItemId would not be entered on the exchange anyway
                canRemove = true;
            }
            if (canRemove)
            {
                SetUpdateRequest(stop, TradeUpdateRequest.DELETE);
                RemoveTradeFromLine_UI_synchronised(stop);
            }
        }

        protected override void RemoveTradeBeforeExchangeRemoval_Synchronised(_Trade trade, bool removeLine, bool allowForceRemove)
        {
            if (!allowForceRemove && (trade.Action == TradeAction.STOP || trade.Action == TradeAction.FLOOR))
            {
                return;
            }
            RemoveTradeFromLine_UI_synchronised(trade, removeLine);
            SetUpdateRequest(trade, TradeUpdateRequest.DELETE);
        }
        #endregion

        double GetLotSize()
        {
            var instrument = bitmexAPI.GetInstrument(symbolName);
            double lotSize = 100;
            if (instrument == null)
            {
                LogTrades.WriteLine("Did not get instrument, defaulting to $100");
            }
            else
            {
                takerFee = instrument.TakerFee;
                LogTrades.WriteLine($"Taker fee is {instrument.TakerFee * 100}%, lotSize is {instrument.LotSize}");
                lotSize = instrument.LotSize;
            }
            return lotSize;
        }



        public override double AdjustResultForFees(double result)
        {
            return result;

            if (takerFee == 0)
            {
                GetLotSize();
            }
            return result - 2 * takerFee * 100; //result is a percent, so take 2 * taker fee as percent off (or add it if loss)
        }

        #region Flip trade

        protected override void DoFlip_Synchronised()
        {
            var flipStop = Stop;// AllTrades.FirstOrDefault(t => t.Type == TradeType.FLIP_ORDER && t.TradeReference == Stop.TradeReference);
            if (flipStop != null)
            {
                //get the current price and the stop price
                var stopPrice = flipStop.Price;
                var position = flipStop.Position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;

                var wallet = bitmexAPI.GetWalletSingleCurrency(SymbolNames.GetCurrencyForSymbol(symbolName));
                if (wallet != null)
                {
                    LogTrades.WriteLine($"GetWalletSingleCurrency() returned {wallet.Amount} for flip order");
                    //if this has just restarted, will try to enter any trades immediately but the price won't be available from the websocket yet.
                    //so use the REST api instead
                    var lastQuoteValue = bitmexAPI.GetLastPrice(symbolName); //can't get price feed from testnet, only live
                    if (lastQuoteValue != 0)
                    {
                        var diff = Math.Abs(MainObj.GetPriceDiff(stopPrice, lastQuoteValue, Stop.Position) * 100);

                        //determine if this is within an acceptable range

                        bool isWithinRange = true;
                        if (diff > TradeEngine.MAX_ENTRY_DIFF_PERCENT)
                        {
                            //Note, this uses the previous stop price, not the close price, which could be different anyway.

                            //it is very difficult to keep the trade running at this point; it has already closed, so would need to be re-entered as a new trade here.
                            //Also, the trade could then re-enter a few hours later which would be wrong.
                            //So ideally, the stop should not be set to 'Close on trigger' and be double the quantity, to do an immediate flip on the exchange,
                            // then should synch with the order value to flip the local order. This is outside the scope of this software, as it would be too
                            // messy to bolt this additional code on, and also it would cause major refactoring changes to be made which would delay the roll-out.
                            //Should be considered for a future version.
                            LogTrades.WriteLineOrder("Did not flip trade as price difference was too great");
                            return;
                        }

                        var nextStop = AllTrades.FirstOrDefault(t => t.Type == TradeType.STOP_WAITING && t.TradeReference == Stop.TradeReference);

                        #region Leverage

                        double stopPC_flip = 0;
                        flipStop.Result = 0;

                        //next stop can be a floor or stop, so get the correct value and set leverage from that
                        if (nextStop != null)
                        {
                            switch (nextStop.Action)
                            {
                                case TradeAction.STOP:
                                case TradeAction.WAITING_STOP:
                                    var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == nextStop.Id);
                                    if (stopLine != null)
                                    {
                                        (var stopPrice1, _) = stopLine.GetPriceAtTime(DateTime.UtcNow, true);
                                        stopPC_flip = MainObj.GetPriceDiffPercent(currentDataItem, stopPrice1, position) * 100;
                                    }
                                    else
                                    {
                                        //in case the waiting stop does not have a line
                                        stopPC_flip = MainObj.GetPriceDiffPercent(currentDataItem, nextStop.Price, position) * 100;
                                    }
                                    break;

                                case TradeAction.FLOOR:
                                    var floorDiff = MainObj.GetPriceDiffPercent(currentDataItem, nextStop.Price, position) * 100;
                                    if (stopPC_flip == 0 || floorDiff < stopPC_flip) //use smallest stop
                                    {
                                        stopPC_flip = floorDiff;
                                    }
                                    break;
                            }
                        }

                        var lotSize = GetLotSize();

                        var leverage = CalculateLeverageFromStop(stopPC_flip, position);
                        LogTrades.WriteLine($"Stop is at {stopPC_flip} at entry time");


                        //re-calculate the order quantity, as the returned amount from the stop should be higher than the previous order
                        (var orderQuantity, var actualLeverage) = CalculateOrderQuantity(wallet.Amount, lastQuoteValue, leverage, lotSize, flipStop.Position);

                        if ((Entry.Position == TradePosition.LONG && actualLeverage < MIN_LEVERAGE_LONG) ||
                            (Entry.Position == TradePosition.SHORT && actualLeverage < MIN_LEVERAGE_SHORT))
                        {
                            var s = Entry.Position == TradePosition.LONG ? "long" : "short";
                            LogTrades.WriteLine($"Leverage of {actualLeverage} was too small to make {s} trade worthwhile, exiting");
                            return;
                        }

                        Leverage = actualLeverage;

                        //save the calculated leverage, even if we are not using it
                        flipStop.Leverage = leverage;
                        flipStop.Result = stopPC_flip;

                        //the correct order quantity is determined by leverage
                        if (!SetLeverageOnExchange(symbolName, Leverage))
                        {
                            WriteDeliniatedMessage("Failed to set leverage for flip trade");
                            return;
                        }
                        #endregion

                        //get new trade reference for this order
                        var newReference = _Trade.GetNewTradeId();

                        var line = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == flipStop.Id);

                        //update the line trade reference (tradeId will still be valid)
                        if (line != null)
                        {
                            line.TradeReference = newReference;
                            SetUpdateRequest(line);
                        }

                        //create a new order from the stop, so the Id is still valid for the line
                        var order = flipStop;

                        order.Timestamp = DateTime.UtcNow;
                        order.TradeReference = newReference;
                        order.Type = isWithinRange ? TradeType.MARKET_ORDER : TradeType.LINE_BREAK_ORDER;
                        order.Position = position;
                        order.Quantity = orderQuantity;
                        order.Action = TradeAction.ORDER;
                        order.Price = lastQuoteValue;
                        order.GlobalStatus = TradeGlobalStatus.ACTIVE;
                        order.ItemStatus = TradeItemStatus.OPEN;

                        SetUpdateRequest(order);

                        //why is entry still open at this point?
                        Entry = null;
                        Close = null;
                        Floor = null;
                        Ceiling = null;
                        Stop = null;

                        LogTrades.WriteLineOrder($"Flipping order if possible...");
                        if (isWithinRange)
                        {

                            (bool hasEntered, bool tradeKilled) = EnterIfPossible_synchronised(order, null);
                            if (tradeKilled)
                            {
                                order.ItemStatus = TradeItemStatus.CLOSED;
                                order.GlobalStatus = TradeGlobalStatus.STOPPED;
                            }
                            if (hasEntered)
                            {
                                Order = order;

                                if (nextStop != null)
                                {
                                    //set the next stop from the waiting stop

                                    //must not set too close because the rising MA stop will quickly overrun the stop
                                    //this will already have been done with the default values, these will not be correct for a MA stop.
                                    AdjustStopPriceToMinimum_Synchronised(Entry, nextStop, STOP_MIN_VALUE);

                                    nextStop.Action = TradeAction.STOP;
                                    nextStop.Timestamp = DateTime.UtcNow;
                                    nextStop.Type = TradeType.NO_FLIP_ORDER; //because there can't be another stop set yet, so this must not be a flip
                                    nextStop.Position = position;
                                    nextStop.Quantity = orderQuantity;
                                    nextStop.TradeReference = newReference;
                                    nextStop.GlobalStatus = TradeGlobalStatus.ACTIVE;
                                    nextStop.ItemStatus = TradeItemStatus.OPEN;
                                    SetUpdateRequest(nextStop);

                                    //this will now be the new Stop
                                    Stop = nextStop;
                                    SetStopOnExchange_Synchronised(Stop);
                                    SetUpdateRequest(Stop);
                                }
                                else
                                {
                                    //set default stop
                                    Floor = Order.Clone();
                                    Floor.Action = TradeAction.FLOOR;
                                    Floor.Timestamp = DateTime.UtcNow;
                                    Floor.Position = position;
                                    Floor.Quantity = orderQuantity;
                                    Floor.TradeReference = newReference;
                                    Floor.GlobalStatus = TradeGlobalStatus.ACTIVE;
                                    Floor.ItemStatus = TradeItemStatus.OPEN;
                                    SetUpdateRequest(Floor);
                                    SetStopOnExchange_Synchronised(Floor);
                                    SetUpdateRequest(Floor);
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Update state from server

        decimal? GetClosePrice(string id)
        {
            var orders = bitmexAPI.GetExecutionHistory(symbolName, DateTime.UtcNow, "3cfb894a-58aa-4b2b-a7dc-e6105e10b1a7");

            //so this seems very awkward, it would need to be differentiated between API close and exchange stop close, in order to get the correct close price.
            //leave it for now.
            return 0;
        }
        protected override async void UpdateLiveState()
        {
            //return;
            if (USE_SIMULATED_EXCHANGE)
            {
                return;
            }
            //await Task.Run(() =>
            {
                //check if live trade is still running on the server. 
                //The position could have been closed and the stop still left running, but this would have been done manually
                //in any case, there should be only one position running at a time
               InitForDataContext(() =>
                {

                    bool _CloseOrderOrUpdateStop(_Trade floorOrStop, string name)
                    {
                        if (floorOrStop == null)
                        {
                            return false;
                        }
                        try
                        {
                            var _order = bitmexAPI.GetOrder(symbolName, floorOrStop.ItemId);
                            if (_order != null)
                            {
                                //order is found, so set state back to default, which will be correct for any stop running on the exchange (not trail, etc.)
                                if (floorOrStop.ExecutionSystem == TradeExecutionSystem.EXCHANGE_UNSYNCHED)
                                {
                                    floorOrStop.ExecutionSystem = TradeExecutionSystem.NORMAL;
                                    SetUpdateRequest(floorOrStop);
                                }

                                if (_order.OrderStatus != null && _order.OrderStatus == "Filled")
                                {
                                    //stop is filled
                                    //problem here is there are occasional "Filled" returns from the exchange, which are not correct
                                    // - so order should only be closed locally when the actual order is filled, not the stop 
                                    LogTrades.WriteLine("Stop order status is Filled, order may have closed on exchange.");
                                    //CloseTradeOnSystem_synchronised((decimal)_order.AveragePrice);
                                    //EndEntryOrCloseScript();
                                    return true;
                                }
                                else if (_order.OrderStatus != null && _order.OrderStatus == "Canceled") //NOTE THE MISSPELLING!! NOT AN ERROR
                                {
                                    //stop has been removed from exchange manually (cancelled)
                                    //BUT - what if the stop was removed in error? This would not happen here, because stops are only removed after trade is closed - 
                                    // but it could still be removed manually and that would end the stop on the local system as well.
                                    //Safer to just let the local system re-set the stop.
                                    WriteDeliniatedMessage($"{name} CANCELLED ON EXCHANGE, RESETTING LOCAL TRADE");
                                    //RemoveTradeFromLine_UI_synchronised(floorOrStop, false);
                                    floorOrStop.ItemId = null;
                                    SetUpdateRequest(floorOrStop);
                                    SetStopOnExchange_Synchronised(floorOrStop);
                                }
                                else
                                {
                                    //just set the price
                                    var orderStop = bitmexAPI.AmendStopOrder(floorOrStop);
                                    //the tick price calculation will return as a double, but the exchange tick price will be rounded for BTC
                                    // - this will result in sending a new update request when one is not needed, which will return an error lots of times
                                    // - and we want to keep showing errors in the log, so by setting the stop price to the actual stopPx value, this will be avoided
                                    if (orderStop != null)
                                    {
                                        floorOrStop.Price = (double)orderStop.StopTriggerPrice;
                                        SetUpdateRequest(floorOrStop);
                                    }
                                }
                            }

                            else //_order was not found, but this will happen initially as exchange may not have yet registered order (first pass)
                            {
                                if (floorOrStop.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                                {
                                    //stop was never placed on exchange
                                    //LogTrades.WriteLine($"{name} MISSING FROM OR NOT YET REGISTERED ON EXCHANGE");

                                    floorOrStop.ExecutionSystem = TradeExecutionSystem.EXCHANGE_UNSYNCHED;
                                    SetUpdateRequest(floorOrStop);
                                }
                            }
                        }
                        catch
                        {
                            //do not close, this could be a connection error so closing would terminate the order
                        }
                        return false;
                    }

                    bool tradeRunning = true;
                    List<Position> openPositions = null;

                    if (Entry != null)
                    {

                        if (!USE_SIMULATED_EXCHANGE)
                        {
                            openPositions = bitmexAPI.GetOpenPositions(symbolName);
                        }
                        //first check for triggered stops, as this will give the correct close price.
                        //if stops untriggered, then close trade locally
                        //then check for stops on exchange, if missing then remove the stops locally. Otherwise, synch price to be safe

                        var floorClosed = false;
                        var stopClosed = false;

                        floorClosed = _CloseOrderOrUpdateStop(Floor, "FLOOR");
                        if (!floorClosed)
                        {
                            stopClosed = _CloseOrderOrUpdateStop(Stop, "STOP");
                        }

                        bool wasUnsynched = Entry.ExchangeMethodScript == TradeEngine.EXCHANGE_NOT_SYNCHED;
                        if (Entry.ExchangeMethodScript == TradeEngine.EXCHANGE_NOT_SYNCHED)
                        {
                            //clear this out here, it will not get done otherwise on any entry operation
                            Entry.ExchangeMethodScript = null;

                            SetUpdateRequest(Entry);
                        }
                        if (!USE_SIMULATED_EXCHANGE)
                        {
                            try
                            {
                                bool save = false;

                                var openPositons = bitmexAPI.GetOpenPositions();// does not always work, sometimes returns no open positions incorrectly, so could use margin balance instead
                                if (openPositions  != null && openPositions.Count == 0) 
                                //var margin = bitmexAPI.GetMargin(); //this appears to have failed once so no idea the trade had closed on exchange
                                //if (margin != null && margin.UnrealisedPnl == 0) //no unrealised PNL so assume no trades running
                                {
                                    //exchange may not return open position immediately after order placement, so wait for a few seconds before displaying a warning message
                                    var timeDiff = DateTime.UtcNow.Subtract(Entry.Timestamp).TotalSeconds;
                                    if (timeDiff > CHECK_EXCHANGE_INTERVAL_SECONDS + 1)
                                    {
                                        if (Entry.ExchangeMethodScript != TradeEngine.EXCHANGE_NOT_SYNCHED)
                                        {
                                            //only want to do this once since the order will show as unsynched anyway
                                            //if (!wasUnsynched)
                                            //{
                                            //    WriteDeliniatedMessage("NO OPEN POSITIONS ON EXCHANGE, STOP MAY HAVE FILLED. CHECK EXCHANGE!!!");
                                            //}

                                            //Problem is, this often gets hit incorrectly
                                            //Example, a trade entered at 20:39 and exited here at 21:10, so it was not a coding error (exit straight away, etc.)
                                            // - so for whatever reason, the server just does not return the correct data. Possible an authentication problem?
                                            //Whatever, can't exit trade here.

                                            Entry.ExchangeMethodScript = TradeEngine.EXCHANGE_NOT_SYNCHED;

                                            tradeRunning = false;
                                            save = true;
                                        }
                                    }
                                }
                                else
                                {
                                    //position is still open, it may have just returned no open positions during the UpdateLiveState() loop which has happened before
                                    // no action here
                                }
                                if (save)
                                {
                                    SetUpdateRequest(Entry);
                                    MainObj.DrawCurrentDispatcherDelayed();
                                }
                            }
                            catch (Exception)
                            {
                                //don't close on error
                            }
                        }

                    }

                    if (tradeRunning)
                    {
                        #region Get liquidation price (not always immediately available)

                        //this is done here as it needs to be done only occasionally and not in every update loop, or will get rate limited - 
                        // this happens if trade is placed locally so there is never a liquidation price
                        //This could be done on ItemAdded, but there is already a check for open positions here

                        if (Order != null)// && Order.LiquidationPrice == 0) //should never be null but this can happen
                        {
                            double liquidationPrice = 0;
                            if (USE_SIMULATED_EXCHANGE)
                            {
                                liquidationPrice = Entry.Position == TradePosition.LONG ? Entry.Price * 0.8 : Entry.Price / 0.8;
                            }
                            else
                            {
                                try
                                {
                                    //exchange doesn't always return open position straight away, which results in no stop being set.
                                    //providing order has been accepted, should always set stop anyway. Will have exited this loop before now if order was not accepted.
                                    //liquidation price, however, is not available if the position is not available as this is not returned in the initial order JSON

                                    if (openPositions != null && openPositions.Count > 0)
                                    {
                                        liquidationPrice = (double)openPositions[0].LiquidationPrice;

                                        //can get liquidation price from wrong open position, when transitioning between trades,
                                        //so always do the update
                                        if (Order.LiquidationPrice != liquidationPrice)
                                        {
                                            Order.LiquidationPrice = Entry.LiquidationPrice = liquidationPrice; //update request already set
                                            SetUpdateRequest(Order);
                                        }
                                    }
                                }
                                catch
                                {
                                    WriteDeliniatedMessage("Failed to set liquidation stop (error thrown)!!!!");
                                }
                            }
                        }

                        #endregion
                    }
                });
            }
            //);
        }
        #endregion

        #region Get account balances

        protected override double GetAccountEquity(List<Wallet> wallet = null)
        {
            if (wallet == null)
            {
                wallet = bitmexAPI.GetWallet(1);
            }
            if (wallet == null)
            {
                LogTrades.WriteLine("Failed to get Wallet for account balances");
                return 0;
            }
            // Total equity
            double totalEquity = 0;
            foreach (var w in wallet)
            {
                if (w.Currency.ToUpper() == SymbolNames.FIAT_ASSET)
                {
                    //just the fiat balance
                    totalEquity += w.Amount / fiatDivisor;
                }
                else
                {
                    (var lastQuoteValue, var s1) = GetValueForSymbol(w.Currency.ToUpper(), SymbolNames.GetQuoteAssetForSymbol(symbolName)); //note that the symbol name does not use the fiat asset symbol

                    if (s1 != null)
                    {
                        //errorMsg += $"\n{s1}"; //since we are only trading a single symbol, no ned for any errors for symbols not trading on this system
                    }
                    else
                    {
                        totalEquity += w.Amount / XBTdivisor * lastQuoteValue;
                    }
                }
            }
            return totalEquity;
        }

        //get account balances - this is for display only, it is not part of trade placement
        internal override void ShowAccountBalances()
        {
            UpdateLiveState(); //can do this anyway here
            //var EntryPrice = 30000;
            //var stopPrice =  EntryPrice - (EntryPrice - 20000) * LIQUIDATION_STOP_OFFSET;
            //var trade = new _Trade { Price = EntryPrice };
            //SetStopOnExchange(trade);

            //Balance is the total of XBt + USDt. There is no need to read open orders, they are included in XBt
            //The available balance would just be the total less the open orders
            string errorMsg = string.Empty;
            string resultMsg = string.Empty;
            double totalEquity = 0;

            var wallet = bitmexAPI.GetWallet(1);
            if (wallet != null)
            {
                // Total equity
                totalEquity = GetAccountEquity(wallet);

                // Individual balances
                foreach (var w in wallet)
                {
                    double value = 0;
                    if (w.Currency.ToUpper() == SymbolNames.FIAT_ASSET)
                    {
                        //just the fiat balance
                        value = w.Amount / fiatDivisor;
                    }
                    else
                    {
                        (var lastQuoteValue, var s1) = GetValueForSymbol(w.Currency.ToUpper(), "USD");

                        if (s1 != null)
                        {
                            //errorMsg += $"\n{s1}";
                        }
                        else
                        {
                            value = w.Amount / XBTdivisor * lastQuoteValue;
                        }
                    }

                    var pc = value / totalEquity * 100;
                    resultMsg += $"{w.Currency}:\t{pc.ToString("0.0")}%\t${value.ToString("0.00")}\t{(w.Amount / XBTdivisor).ToString()}\n";
                }

            }

            //open orders
            double quantity = 0;
            try
            {
                var positions = bitmexAPI.GetOpenPositions(null, 1);
                if (positions != null)
                {
                    foreach (var position in positions)
                    {
                        quantity += Math.Abs((double)position.CurrentQuantity);
                    }
                }
            }
            catch { }

            resultMsg += $"\n------------------------------------\nTotal equity:\t${totalEquity.ToString("0.00")}\n------------------------------------\n";
            resultMsg += $"Open orders:\t${quantity.ToString("0.00")}\n------------------------------------\n";

            var btcEngine = MainObj.GetTradeEngineForSymbol("XBTUSD");
            if (btcEngine != null && btcEngine.currentDataItem != null)
            {
                resultMsg += $"\nLast BTC price:\t${btcEngine.currentDataItem.Close}";
            }

            resultMsg += $"{errorMsg}";

            AlertDialog.Show(resultMsg, "Balances (server also updated)");
        }
        #endregion

    }
}