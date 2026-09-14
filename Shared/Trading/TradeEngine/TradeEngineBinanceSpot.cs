using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;

namespace AlgoTrader.Shared
{
    public class TradeEngineBinanceSpot : TradeEngineBinance
    {
        #region Strings
        public const string STOP_LOSS = "STOP_LOSS";
        public const string ABOVE_STOP_PRICE = "aboveStopPrice";
        public const string ABOVE_CLIENT_ORDER_ID = "aboveClientOrderId";
        public const string BELOW_CLIENT_ORDER_ID = "belowClientOrderId";
        public const string TAKE_PROFIT = "TAKE_PROFIT";
        public const string BELOW_STOP_PRICE = "belowStopPrice";
        public const string ABOVE_TYPE = "aboveType"; //STOP_LOSS_LIMIT, STOP_LOSS, LIMIT_MAKER - limit maker should trigger the actual stop loss limit, presumable at the above stop price
        public const string BELOW_TYPE = "belowType";
        #endregion
     
        public override async void Test()
        {
            var d = CalculateLeverageFromPercent(0.7, TradePosition.LONG);
        }

        #region Init

        public TradeEngineBinanceSpot(string symbolName, int timeInterval) : base(symbolName, timeInterval)
        {
            binanceAPI = USE_TESTNET

               //testnet can only run on spot
               ? new BinanceSpotAPI
               (
                    TESTNET_KEY,
                    TESTNET_SECRET,
                    null,
                    15000,
                    true,
                    "https://testnet.binance.vision/"
                )
               
                //live spot exchange
                : new BinanceSpotAPI
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
        protected override Dictionary<string, string> GetOrderData(TradePosition position, double orderQuantity, Side side, string orderId)
        {
            string sideValue = string.Empty;
            switch (side)
            {
                case Side.ENTER:
                    sideValue = BUY;
                    break;
                case Side.CLOSE:
                    sideValue = SELL;
                    break;
            }
            return new Dictionary<string, string>
            {
                {SYMBOL, symbolName },
                {SIDE, sideValue.ToString()},
                {ORDER_TYPE, MARKET },
                {QUANTITY, orderQuantity.ToString() },
                {CLIENT_ORDER_ID_NEW, orderId },
                {RESPONSE_TYPE, FULL }
            };
        }
        
        protected override Dictionary<string, string> GetOpenOrderData()
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName },
            };
        }
        
        protected override Dictionary<string, string> GetCancelOrderData()
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName },
            };
        }
        
        protected override Dictionary<string, string> GetOCOData(TradePosition position, double orderQuantity, double belowPrice, string stopId, double abovePrice, string TPId)
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName},
                { SIDE, SELL},
                { ABOVE_TYPE, TAKE_PROFIT},
                { ABOVE_STOP_PRICE, abovePrice.ToString()},
                { ABOVE_CLIENT_ORDER_ID, TPId },
                { BELOW_CLIENT_ORDER_ID, stopId },
                { QUANTITY, orderQuantity.ToString()},
                { BELOW_TYPE, STOP_LOSS},
                { BELOW_STOP_PRICE, belowPrice.ToString()},      

                //{ABOVE_STOP_PRICE, 70000}, //so presumably this would be for a buy, not a sell
                //{ABOVE_TIMEINFORCE, 1000},
                //{TRADE_ID_NEW, Stop.ItemId} //generates -1104 if sent
                //{RESPONSE_TYPE, FULL}
                //{TIME_IN_FORCE, GTC}- default anyway
            };
        }
        
        protected override Dictionary<string, string> GetCancelOCOData(string orderListId)
        {
            return new Dictionary<string, string>
            {
                { SYMBOL, symbolName },
                { LIST_CLIENT_ORDER_ID, orderListId }

            };
        }
        #endregion

        protected override double GetFreeBaseAssetBalance() 
        {
            //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

            try
            {
                var accountData = GetAccountInfo();

                //can ignore locked amount, this is just for trades placed on the exchange which have not yet entered

                if (accountData != null)
                {
                    var symbol = SymbolNames.GetBaseAssetForSymbol(symbolName);
                    return (double)accountData?.Balances.FirstOrDefault(x => x.Asset == symbol)?.Free;
                }
                
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return -1;
            }
            return 0;
        }
        
        protected override double GetBalanceToEnterTrade(double freeBalance, double lastQuoteValue, TradePosition position = TradePosition.LONG)
        {
            var balanceToTrade = GetRestrictedBalance(freeBalance, lastQuoteValue);

            var fiatAmount = balanceToTrade / lastQuoteValue;

            return GetCorrectPrecisionForBaseAssetTrade(fiatAmount);       
        }

        protected override (double freeBalance, double totalEquity) GetTotalEquity() //e.g. USDT, BTC
        {
            try
            {
                var accountData = GetAccountInfo();

                //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

                double totalEquity = 0;
                double freeEquity = 0;

                //can really ignore locked amount, this is just for trades placed on the exchange which have not yet entered


                /*************/
                /* Note, this only tests for BTC and USDT since testnet has every symbol with some asset valuu */
                foreach (var v in accountData.Balances.Where(x => x.Asset == SymbolNames.FIAT_ASSET || x.Asset == "BTC" && (x.Free > 0 || x.Locked > 0)))
                {
                    if (SymbolNames.SYMBOLS_TO_EXCLUDE.Contains(v.Asset))
                    {
                        continue;
                    }

                    if (v.Asset == SymbolNames.FIAT_ASSET)
                    {
                        //e.g. USDT
                        totalEquity += v.Free + v.Locked;
                        freeEquity += v.Free;
                        //resultMsg += $"{v.Asset}:\t{pc.ToString("0.0")}%\t\t${value.ToString("0.00")}\n";
                    }
                    else if (v.Free + v.Locked > 0)
                    {
                        var price = GetLastPrice();
                        totalEquity += (v.Free + v.Locked) * price;
                        freeEquity += v.Free * price;
                    }
                }
                
                return (Math.Round(freeEquity, 2), Math.Round(totalEquity, 2));
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return (0, 0);
            }
        }

        protected override bool IsTradeClosed()
        {
            var exchangeInfo = GetExchangeInfo();
            if (exchangeInfo != null)
            {
                var __baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                if (__baseAsset != null)
                {
                    var __symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                    if (__symbol != null)
                    {
                        var lotSize = __symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
                        if (lotSize != null)
                        {
                            var accountData = GetAccountInfo();
                            var _minValue = lotSize.MinQty * 2; //it should always use the lot size notional value, same as MinQty, to calculate trade quantity so would only leave < MinQty but double this to be safe
                            var _baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault();
                            var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
                            return _baseAssetBalanceLocked < _minValue && _baseAssetBalanceFree < _minValue; //i.e there is no BTC
                        }
                    }
                }
            }
            return false;
        }

        public override void ShowAccountBalances()
        {
            //never implemented in Binance spot, only Bitmex so nothing missing here
            AlertDialog.Show("This method is unimplemented.", "Account balance and info:");
        }
    } 
}