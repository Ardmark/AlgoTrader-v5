using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;
using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{       
    /*
    * ISOLATED MARGIN:
    * Leverage is set to 10X permanantly, you can't change that and there is no need because you just borrow what you need
    * You can change the margin level in Cross margin but the API method AdjustCrossMarginMaxLeveradgeAsync() only accepts 3 or 5
    *  - however there is an error saying you need to complete a questionaire if you want to go up to 10.
    * Isolated is nothing to do with Spot, you only trade with your isolated balance.
    * 
    * Your total available borrowing is the amount of BTC + USDT, multiplied by leverage (always 10x with the API)
    * 
    * You can determine the amount to borrow from QueryMaxBorrowAsync()
    * You can then either set the amount to borrow or repay with SetMarginBorrowRepay() or auto-borrow by setting
    *  the sideEffectType parameter when submitting the buy order.
    *  
    *  MarginLevel will trigger liquidation when it is <= 1.05 (starts of at 999)
    *  
    * */

    public class TradeEngineBinanceMargin : TradeEngineBinance
    {
        #region Strings
        public const string TRUE = "true";
        public const string FALSE = "false";
        public const string PRICE = "price";
        public const string STOP_PRICE = "stopPrice";
        public const string STOP_CLIENT_ORDER_ID = "stopClientOrderId";
        public const string LIMIT_CLIENT_ORDER_ID = "limitClientOrderId";
        public const string IS_ISOLATED = "isIsolated";
        public const string AUTO_REPAY_AT_CANCEL = "autoRepayAtCancel";
        public const string SIDE_EFFECT_TYPE = "sideEffectType";
        public const string NO_SIDE_EFFECT = "NO_SIDE_EFFECT";
        public const string MARGIN_BUY = "MARGIN_BUY";
        public const string AUTO_REPAY = "AUTO_REPAY";
        public const string AUTO_BORROW_REPAY = "AUTO_BORROW_REPAY"; //this will repay any liabilitites as well as borrow to buy or sell.
        public const string STOP_LIMIT_PRICE = "stopLimitPrice";    
        #endregion

        public override void Test()
        {

            return;
            CancelOpenOrders();

           // return;
            SendOrder(TradePosition.LONG, 0.0003, _Trade.GetNewTradeId(), Side.CLOSE, out double averagePrice, out double averageQuantity);

            //OCO pricing for a long, mark price 77300
            //double abovePrice = 79000;
            //double belowPrice = 77000;

            //OCO pricing for a short, mark price 77300
            //double belowPrice = 77000;
            //double abovePrice = 79000;

            //SendOCO(TradePosition.LONG, 0.0001, belowPrice, _Trade.GetNewTradeId(), abovePrice, _Trade.GetNewTradeId());

            return;
        }

        #region Binance data classes (JSON deserializes into these directly)

        public class MaxBorrowData
        {
            public string Amount { get; set; }
            public string BorrowLimit { get; set; }
        }

        //The JSON node here is an unnamed array of these objects, so to deserialize: Deserialize<List<IsolatedMarginFeeData>>(response);
        public class IsolatedMarginFeeData
        {
            public class IsolatedMarginData
            {
                public string Coin { get; set; }
                public double DailyInterest { get; set; }
                public double BorrowLimit { get; set; }
            }
            public double VipLevel { get; set; }
            public string Symbol { get; set; }
            public double Leverage { get; set; }
            public List<IsolatedMarginData> Data { get; set; }
        }

        public class IsolatedMarginAccountData
        {
            public class BaseQuoteAssetData
            {
                public string Asset { get; set; }
                public bool BorrowEnabled { get; set; }
                public double Borrowed { get; set; }
                public double Free { get; set; }
                public double Interest { get; set; }
                public double Locked { get; set; }
                public double NetAsset { get; set; }
                public double NetAssetOfBTC { get; set; }
                public bool RepayEnabled { get; set; }
                public double TotalAsset { get; set; }

            }
            public class AssetData
            {
                public BaseQuoteAssetData BaseAsset { get; set; }
                public BaseQuoteAssetData QuoteAsset { get; set; }
                public string Symbol { get; set; }
                public bool IsolatedCreated { get; set; }
                public bool Enabled { get; set; }
                public double MarginLevel { get; set; }
                public string MarginLevelStatus { get; set; }
                public double MarginRatio { get; set; }
                public double IndexPrice { get; set; }
                public double LiquidatePrice { get; set; }
                public double LiquidateRate { get; set; }
                public bool TradeEnabled { get; set; }
            }
            public List<AssetData> Assets { get; set; }
            public double TotalAssetOfBtc { get; set; }
            public double TotalLiabilityOfBtc { get; set; }
            public double TotalNetAssetOfBtc { get; set; }
           
            public BaseQuoteAssetData GetBaseAsset(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName)?.BaseAsset;
            }
            public BaseQuoteAssetData GetQuoteAsset(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName)?.QuoteAsset;
            }
            public AssetData GetAssetData(string symbolName)
            {
                return Assets.FirstOrDefault(x => x.Symbol == symbolName);
            }
        }

        #endregion

        #region Init
        public TradeEngineBinanceMargin(string symbolName, int timeInterval) : base(symbolName, timeInterval) 
        {
            binanceAPI = new BinanceMarginAPI
                         (
                              LIVE_KEY,
                              LIVE_SECRET,
                              null,
                              15000,
                              true,
                              "https://api.binance.com/"
                         );

            GetExchangeInfo(); //takes about 2 seconds so don't want to do this when entering a trade
        }
        #endregion

        #region Data for order and OCO

        /*
         * This works differently than the OCO for spot trading:
         * The 2 orders are created, but they are as follows:
         * 
         * Order [0]
         * side = SELL
         * stopPrice = the stop price
         * type = STOP_LOSS
         * 
         * Order [1]
         * side = SELL
         * price = the take profit price
         * type = LIMIT_MAKER
         * 
         * HOWEVER, only the limit maker order shows in the web site, but if you cancel that then it cancels both orders.
         * 
         * 
         * 
         * */
        protected override Dictionary<string, string> GetOrderData(TradePosition position, double orderQuantity, Side side, string orderId)
        {
            //short: Enter: {SIDE, SELL}, {SIDE_EFFECT_TYPE, MARGIN_BUY}
            //       Close: {SIDE, BUY},  {SIDE_EFFECT_TYPE, AUTO_REPAY}
            //Long:  Enter: {SIDE, BUY},  {SIDE_EFFECT_TYPE, MARGIN_BUY}
            //       Close: {SIDE, SELL}, {SIDE_EFFECT_TYPE, AUTO_REPAY}

            string sideValue = string.Empty;
            string sideEffectType = string.Empty;
            switch (position)
            {
                case TradePosition.LONG:
                    switch (side)
                    {
                        case Side.ENTER:
                            sideValue = BUY;
                            sideEffectType = MARGIN_BUY;
                            break;
                        case Side.CLOSE:
                            sideValue = SELL;
                            sideEffectType = AUTO_REPAY;
                            break;
                    }
                    break;

                case TradePosition.SHORT:
                    switch (side)
                    {
                        case Side.ENTER:
                            sideValue = SELL;
                            sideEffectType = MARGIN_BUY;
                            break;
                        case Side.CLOSE:
                            sideValue = BUY;
                            sideEffectType = AUTO_REPAY;
                            break;
                    }
                    break;

            }
            return new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {SIDE, sideValue},
                {ORDER_TYPE, MARKET },
                {QUANTITY, orderQuantity.ToString() },
                {CLIENT_ORDER_ID_NEW, orderId },
                {RESPONSE_TYPE, FULL },

                //additional data for margin trade
                //{AUTO_REPAY_AT_CANCEL, TRUE }, //not sure what this does unless you need to borrow to set a limit order?
                {SIDE_EFFECT_TYPE, sideEffectType},
                {IS_ISOLATED, TRUE }
            };
        }
        
        protected override Dictionary<string, string> GetOpenOrderData()
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { IS_ISOLATED, TRUE }
            };
        }
        
        protected override Dictionary<string, string> GetCancelOrderData()
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { IS_ISOLATED, TRUE }
            };
        }
        
        protected override Dictionary<string, string> GetOCOData(TradePosition position, double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId)
        {
            string sideValue = string.Empty;
            string sideEffectType = string.Empty;

            switch (position)
            {
                case TradePosition.LONG:
                    sideValue = SELL;
                    sideEffectType = AUTO_REPAY;
                    break;
                case TradePosition.SHORT:
                    sideValue = BUY;
                    sideEffectType = MARGIN_BUY;
                    break;
            }
                    
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { SIDE, sideValue},  
                //{ ABOVE_TYPE, TAKE_PROFIT},
                //{ ABOVE_STOP_PRICE, abovePrice.ToString()},
                //{ ABOVE_CLIENT_ORDER_ID, TPId },
                { QUANTITY, orderQuantity.ToString()},                  
                //{ BELOW_TYPE, STOP_LOSS},  
                //{ BELOW_STOP_PRICE, belowPrice.ToString()},
                //{ BELOW_CLIENT_ORDER_ID, stopId },
                { PRICE, abovePrice.ToString() }, 
                { STOP_PRICE, belowPrice.ToString() },
                { STOP_CLIENT_ORDER_ID, stopId },
                { LIMIT_CLIENT_ORDER_ID, TPId },

                {SIDE_EFFECT_TYPE, sideEffectType},
                {IS_ISOLATED, TRUE }
            };
        }
        
        protected override Dictionary<string, string> GetCancelOCOData(string orderListId)
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { LIST_CLIENT_ORDER_ID, orderListId },
                { IS_ISOLATED, TRUE }
            };
        }
        #endregion

        protected override double GetFreeBaseAssetBalance()
        {
            //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response == null)
            {
                return 0;
            }
            var baseAsset = response.GetBaseAsset(symbolName);
            return baseAsset != null ? baseAsset.Free : 0;
        }

        protected override double GetBalanceToEnterTrade(double freeBalance, double lastQuoteValue, TradePosition position)
        {
            //Get the correct precision for a trade and return the amended amount, this is for the base asset (USDT)
            //All trades are in base asset, both sell and buy (e.g. BTC)
            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response == null)
            {
                return 0;
            }
            var baseAsset = response.GetBaseAsset(symbolName);
            var quoteAsset = response.GetQuoteAsset(symbolName);
            var assetData = response.GetAssetData(symbolName);
            if (baseAsset == null || quoteAsset == null || assetData == null)
            {
                return 0;
            }

            //with margin trading, the balance is leverage-derived rather than setting a leverage value on the exchange
            var marginLevel = Math.Clamp(assetData.MarginLevel, Leverage, 10);

            if (marginLevel < 10)
            {
                LogTrades.WriteLine($"*** Asset data margin level returned {marginLevel}, which is less than the assumed level of 10");
            }

            var leveragedAmount = Leverage / marginLevel * quoteAsset.Free;

            double orderQuantity = 0;
            switch (position)
            {
                case TradePosition.LONG:

                    double orderQuantityBuy = base.GetBalanceToEnterTrade(leveragedAmount, GetLastPrice());

                    var data1 = new Dictionary<string, string>
                    {
                        { "asset", "BTC" },
                        { "isolatedSymbol", "BTCUSDT" }
                    };

                    //get the max borrow limit - this is not fixed, it is dependent on your current equity so get it each time
                    double maxBorrowLimit = 0;
                    var response1 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync(data1).Result);
                    if (response != null)
                    {
                        double.TryParse(response1.Amount, out maxBorrowLimit);
                    }

                    if (orderQuantityBuy > maxBorrowLimit)
                    {
                        LogTrades.WriteLine($"*** Max borrow limit EXCEEDED, required amount was ${orderQuantityBuy} but limit is ${maxBorrowLimit}");  
                    }
                    if (maxBorrowLimit > 0)
                    {
                        orderQuantity = GetCorrectPrecisionForBaseAssetTrade(Math.Min(maxBorrowLimit, orderQuantityBuy));
                    }
                    break;
                case TradePosition.SHORT:
                    orderQuantity = GetCorrectPrecisionForBaseAssetTrade(baseAsset.Free);
                    break;
            }

            return orderQuantity;
        }

        protected override (double freeBalance, double totalEquity) GetTotalEquity() //e.g. USDT, BTC
        {
            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response != null)
            {
                var baseAsset = response.GetBaseAsset(symbolName);
                var quoteAsset = response.GetQuoteAsset(symbolName);

                if (baseAsset != null && quoteAsset != null)
                {
                    var price = GetLastPrice();
                    var freeEquity = baseAsset.Free + quoteAsset.Free * price;
                    var totalEquity = baseAsset.TotalAsset + quoteAsset.TotalAsset * price;

                    return (Math.Round(freeEquity, 2), Math.Round(totalEquity, 2));
                }
            }
            return (0, 0);
        }
        
        protected override bool IsTradeClosed()
        {
            var exchangeInfo = GetExchangeInfo();
            if (exchangeInfo != null)
            {
                var __symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                if (__symbol != null)
                {
                    var lotSize = __symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
                    if (lotSize != null)
                    {
                        var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
                        if (response != null)
                        {
                            var baseAsset = response.GetBaseAsset(symbolName);
                            if (baseAsset != null)
                            {
                                var _minValue = lotSize.MinQty * 2; //it should always use the lot size notional value, same as MinQty, to calculate trade quantity so would only leave < MinQty but double this to be safe
                                var _baseAssetBalanceFree = baseAsset.Free;
                                var _baseAssetBalanceLocked = baseAsset.Locked;
                                return _baseAssetBalanceLocked < _minValue && _baseAssetBalanceFree < _minValue; //i.e there is no BTC
                            }
                        }
                    }
                }            
            }
            return false;
        }

        public override void ShowAccountBalances()
        {
            string message = string.Empty;

            var response = Deserialize<IsolatedMarginAccountData>(binanceAPI.QueryIsolateMarginAccountInfoAsync().Result);
            if (response != null)
            {
                var baseAsset = response.GetBaseAsset(symbolName);
                var quoteAsset = response.GetQuoteAsset(symbolName);
                var asset = response.GetAssetData(symbolName);
                var currentPrice = GetLastPrice();
                if (baseAsset != null && quoteAsset != null && asset != null)
                {
                    message += "---------------------------------\n";
                    message += "BTC:\n";
                    message += $"Free:\t\t{baseAsset.Free}\n";
                    message += $"Locked:\t\t{baseAsset.Locked}\n";
                    message += $"Borrowed:\t{baseAsset.Borrowed}\n";
                    message += $"Interest:\t\t{baseAsset.Interest}\n";


                    message += "---------------------------------\n";
                    message += "USDT:\n";
                    message += $"Free:\t\t{quoteAsset.Free:F2}\n";
                    message += $"Locked:\t\t{quoteAsset.Locked:F2}\n";
                    message += $"Borrowed:\t{quoteAsset.Borrowed:F2}\n";
                    message += $"Interest:\t\t{quoteAsset.Interest:F2}\n";

                    message += "---------------------------------\n";

                    var openOrders = GetOpenOrders();
                    if (openOrders != null && openOrders.Count > 0) 
                    {
                        message += "OPEN ORDERS:\n";
                        var side = openOrders[0].Side;
                        var price = openOrders[0].Side == BUY ? openOrders[0].Price : openOrders[0].StopPrice;
                        var type = openOrders[0].Type;

                        message += $"{type}, {side}@{price} \n";
                        if (openOrders.Count > 1)
                        {
                            side = openOrders[1].Side;
                            price = openOrders[1].Side == BUY ? openOrders[1].StopPrice : openOrders[1].Price;
                            type = openOrders[1].Type;
                            message += $"{type}, {side}@{price} \n";
                        }
                    }
                    else
                    {
                        message += "No open orders.\n\n";
                    }
                    message += "--------------------------------------------------------\n";


                    //this returns the same value as the Liquidation Rate property
                    //var position = asset.LiquidatePrice < currentPrice ? TradePosition.SHORT : TradePosition.LONG;
                    //message += $"Liquidation percentage:\t{MainObj.GetPriceDiff(asset.LiquidatePrice, currentPrice, position)*100:F1}%\n";

                    
                    message += $"Margin Level:\t\t{asset.MarginLevel}\n";
                    message += $"Margin Level Status:\t{asset.MarginLevelStatus}\n";
                    message += $"Margin Ratio:\t\t{asset.MarginRatio}\n";
                    message += $"Liquidate Price:\t\t${asset.LiquidatePrice:F2}\n";
                    message += $"Liquidate Rate:\t\t{asset.LiquidateRate:F2}%\n";
                    message += $"Index Price:\t\t${asset.IndexPrice:F2}\n";
                    message += $"Enabled:\t\t\t{asset.Enabled && asset.TradeEnabled}\n";

                    message += "--------------------------------------------------------\n";

                    message += "Total of base + quote assets:\n";
                    message += $"Including borrowing:\t{response.TotalAssetOfBtc} = ${response.TotalAssetOfBtc*currentPrice:F2}\n";
                    message += $"Net of borrowing:\t\t{response.TotalNetAssetOfBtc} = ${response.TotalNetAssetOfBtc*currentPrice:F2}\n";
                    message += $"Total Liability BTC:\t\t{response.TotalLiabilityOfBtc} = ${response.TotalLiabilityOfBtc * currentPrice:F2}\n";

                    message += "--------------------------------------------------------\n";
                    var data1 = new Dictionary<string, string>
                    {
                        { "asset", "BTC" },
                        { "isolatedSymbol", "BTCUSDT" }
                    };

                    var response1 = Deserialize<MaxBorrowData>(binanceAPI.QueryMaxBorrowAsync(data1).Result);
                    if (response1 != null)
                    {
                        double.TryParse(response1.Amount, out double amt);
                        message += $"Max. borrowble amount:\t{amt} = ${amt*currentPrice:F2}\n";
                    }
                    message += "--------------------------------------------------------\n";

                    AlertDialog.Show(message, "Account balance and info:");
                }
            }                
        }
        
        #region Margin-specific methods (unused)
        public bool EnableIsolatedMargin()
        {
            try
            {
                JsonNode response = binanceAPI.EnableIsolateMarginAsync(symbolName).Result;
                return response["success"].ToString() == "true";
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error enabling isolated margin: {e.Message}");
                return false;
            }
        }

        public void QueryIsolatedMarginAccountInfo()
        {
            try
            {
                JsonNode response = binanceAPI.QueryIsolateMarginAccountInfoAsync().Result;
            }
            catch (Exception ex)
            {

            }
        }
        #endregion


    }
}
