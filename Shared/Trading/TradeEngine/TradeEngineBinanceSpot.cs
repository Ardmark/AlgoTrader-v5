using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;
using System.Text.Json.Nodes;
using System.Windows.Forms;

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

        protected override double BalanceToCloseTrade(TradePosition position) 
        {
            //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset

            try
            {
                var accountData = GetAccountInfo();

                //can ignore locked amount, this is just for trades placed on the exchange which have not yet entered

                if (accountData != null)
                {
                    var symbol = SymbolNames.GetBaseAssetForSymbol(symbolName);
                    var quoteAssetBalance = (double)accountData?.Balances.FirstOrDefault(x => x.Asset == symbol)?.Free;

                    quoteAssetBalance = GetRestrictedBalanceBase(quoteAssetBalance);
                    
                    return quoteAssetBalance;
                }
                
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return -1;
            }
            return 0;
        }

        protected override double BalanceNet()
        {
            (var netBalance, var leveragedBalance) =  BalanceToEnterTrade(GetLastPrice(), TradePosition.LONG);
            return netBalance;
        }

        protected override (double, double) BalanceToEnterTrade(double lastQuoteValue, TradePosition position) //e.g. USDT, BTC
        {
            try
            {
                var accountData = GetAccountInfo();

                //can really ignore locked amount, this is just for trades placed on the exchange which have not yet entered
                var v = accountData.Balances.FirstOrDefault(x => x.Asset == SymbolNames.FIAT_ASSET);
                double freeEquity = v != null ? v.Free : 0;
                if (freeEquity == 0)
                {
                    return (0, 0);
                }
                var baseBalance = freeEquity / lastQuoteValue;
                var balanceToTrade = GetRestrictedBalanceBase(baseBalance);
                return (Math.Round(freeEquity, 2), GetCorrectPrecisionForBaseAssetTrade(balanceToTrade));
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting balance to enter trade: {e.Message}");
                return (0, 0);
            }
        }
        protected override double BalanceFree()
        {
            var accountData = GetAccountInfo();

            //can really ignore locked amount, this is just for trades placed on the exchange which have not yet entered
            var v = accountData.Balances.FirstOrDefault(x => x.Asset == SymbolNames.FIAT_ASSET);
            double freeEquity = v != null ? v.Free : 0;
            return freeEquity;
        }

        protected override bool IsTradeClosed(TradePosition position)
        {
            var ret = false;
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
                            //var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
                            ret = _baseAssetBalanceFree < _minValue; //i.e there is no BTC
                        }
                    }
                }
            }
            if (ret)
            {
                LogTrades.WriteLine($"++ {DateTime.UtcNow} - SPOT IsTradeClosed() returned true");
            }
            return ret;
        }

        public override void ShowAccountBalances()
        {
            var message = string.Empty;
            var accountData = GetAccountInfo();
            if (accountData != null)
            {
                var __baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                var _quoteAsset = SymbolNames.GetQuoteAssetForSymbol(symbolName);

                var __baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault() * GetLastPrice();
                var __baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault() * GetLastPrice();
                var _quoteAssetBalanceFree = accountData.Balances.Where(x => x.Asset == _quoteAsset).Select(x => x.Free).FirstOrDefault();
                var _quoteAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == _quoteAsset).Select(x => x.Locked).FirstOrDefault();

                double bnbPrice = 0;
                JsonNode response = binanceAPI.TickerPriceAsync("BNBUSDT").Result;
                try
                {
                    double.TryParse(response["price"].ToString(), out bnbPrice);
                }
                catch { }
                var __bnbAssetBalanceFree = accountData.Balances.Where(x => x.Asset == "BNB").Select(x => x.Free).FirstOrDefault() * bnbPrice;
                var __bnbAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == "BNB").Select(x => x.Locked).FirstOrDefault() * bnbPrice;

                message += "---------------------------------\n";
                message += "BTC:\n";
                message += $"Free:\t\t${__baseAssetBalanceFree:F2}\n";
                message += $"Locked:\t\t${__baseAssetBalanceLocked:F2}\n";
                message += "---------------------------------\n";
                message += "USDT:\n";
                message += $"Free:\t\t${_quoteAssetBalanceFree:F2}\n";
                message += $"Locked:\t\t${_quoteAssetBalanceLocked:F2}\n";
                message += "---------------------------------\n";
                message += "BNB:\n";
                message += $"Free:\t\t${__bnbAssetBalanceFree:F2}\n";
                message += $"Locked:\t\t${__bnbAssetBalanceLocked:F2}\n";


                AlertDialog.Show(message, "Account balance and info:");
            }

        }

        protected override double GetLiquidationPrice()
        {
            return 0;
        }
    } 
}