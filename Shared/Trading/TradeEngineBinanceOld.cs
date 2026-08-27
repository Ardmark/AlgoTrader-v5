using AlgoTrader.Shared;
using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using AlgoTrader.Shared.Windows;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.VoiceCommands;
using static AlgoTrader.Shared.ExchangeConnection.ExchangeDataProcessorBinance;

/*
 * Quote-to-base trading and stub orders: This code has been removed but is still in a project-excluded copy of the original file.
 * */
namespace AlgoTrader.Shared
{
    internal class _TradeEngineBinance : TradeEngine
    {

        #region Trading parameters
        public static double RETAINED_BNB_PERCENTAGE { get; set; }
        #endregion

        #region Fields
        DateTime lastExchangeInfoRequest = new DateTime();
        ExchangeInfoData exchangeInfo = null;
        long drift;
        BinanceAPI binanceRest;
        #endregion

        #region Init
        public _TradeEngineBinance(string symbolName, int timeInterval) : base(symbolName, timeInterval)
        { 
             binanceRest = USE_TESTNET

                ? new BinanceAPI(
                    TESTNET_KEY,
                    TESTNET_SECRET,
                    null,
                    15000,
                    true,
                    TESTNET_URL
                )
                : new BinanceAPI(
                    LIVE_KEY,
                    LIVE_SECRET,
                    null,
                    15000,
                    true, 
                    LIVE_URL
                 );
        
        }

        #endregion

        /*
         * This works as follows:
         * 1) Entry is added, with ExchangeMethodScript = NEW_ORDER_SCRIPT, or for a close, CLOSE_ORDER_SCRIPT is set
         * 2) Request for Account Info is made from exchange
         * 3) When account info is received, the Entry trade can then be executed as an on-exchange order, either as a buy or sell
         * 
         * */


        const string TESTNET_URL = "https://testnet.binance.vision/";
        const string LIVE_URL = "https://api.binance.com/";
        /*
         * BUY - QuoteOrderQuantity will buy as much of the base asset as can be purchased for the underlying asset,
         *  so BNBBTC will buy as much BNB as can be purchased for a specified amount of BTC.
         * SELL - QuoteOrderQuantity will sell as much of the underlying asset as needed to receive the base asset amount specified
         *  so BNBBTC will sell as much BNB as needed to receive the specified amount of BTC, so the amount specified is in BTC NOT BNB
         *  it seems that, since the minimum trade of BNB is 0.01, the exchange will only trade in MULTIPLES of this amount. If the sell would
         *  require 0.015 BNB to receive the specified amount of BTC, it will only sell 0.01 BNB
         * */
        const string SYMBOL               = "symbol";
        const string BTCUSDT              = "BTCUSDT";
        const string TRADE_ID_ORIGINAL    = "origClientOrderId";
        const string TRADE_ID_NEW         = "newClientOrderId";
        const string EXCHANGE_ORDER_ID    = "orderId";
        const string SIDE                 = "side";
        const string ORDER_TYPE           = "type"; //LIMIT, MARKET, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER
        const string QUOTE_ORDER_QTY      = "quoteOrderQty";
        const string QUANTITY             = "quantity";
        const string ICEBERG_QUANTITY     = "icebergQty";
        const string PRICE                = "price";
        const string STOP_PRICE           = "stopPrice";
        const string RESPONSE_TYPE        = "newOrderRespType";
        const string TIME_IN_FORCE        = "timeInForce";
        const string TIMEOUT_MS           = "recvWindow"; //this defaults to 5000ms. It is recommended to use this value or less, cannot go beyond 60000
        const string ORIG_CLIENT_ORDER_ID = "origClientOrderId";
        const string BUY                  = "buy";
        const string SELL                 = "sell";
        const string LIMIT                = "limit";
        const string MARKET               = "market";
        const string STOP_LOSS            = "STOP_LOSS";
        const string STOP_LOSS_LIMIT      = "STOP_LOSS_LIMIT";
        const string LIMIT_MAKER          = "LIMIT_MAKER";
        const string TAKE_PROFIT_LIMIT    = "TAKE_PROFIT_LIMIT";
        const string ABOVE_TYPE           = "aboveType"; //STOP_LOSS_LIMIT, STOP_LOSS, LIMIT_MAKER - limit maker should trigger the actual stop loss limit, presumable at the above stop price
        const string ABOVE_PRICE          = "abovePrice";
        const string ABOVE_STOP_PRICE     = "aboveStopPrice";
        const string ABOVE_TIMEINFORCE    = "aboveTimeInForce";
        const string BELOW_TYPE           = "belowType";
        const string BELOW_PRICE          = "belowPrice";
        const string BELOW_STOP_PRICE     = "belowStopPrice";
        const string BELOW_TIMEINFORCE    = "belowTimeInForce";
        const string ACK                  = "ACK";
        const string RESULT               = "RESULT";
        const string FULL                 = "FULL";


        #region Start entry or close order

        void Test()
        {



            //try
            //{
            //    Task.Run(async () => 
            //    {
            //        await binanceRest.CalculateDriftAsync();
            //        Debug.WriteLine($"Drift calculated as {binanceRest._drift}ms");
            //    });
            //}
            //catch (Exception e)
            //{

            //}

            return;

                //var query = new Dictionary<string, string>
                //{
                //    { SYMBOL, BTCUSDT },
                //    { "interval", "5m" },
                //    { "limit", "10"   }
                //};
                //var response = binanceRest.KlinesAsync(query).Result;

            //response = binanceRest.TickerPriceAsync(BTCUSDT).Result;

            //// Generate Entry order
            //LogTrades.WriteLine("Generating entry order");
            //var quoteData = JsonConvert.DeserializeObject<QuoteData>(response.ToString());

                var data = new Dictionary<string, string>
            {
                {SYMBOL, BTCUSDT },
                {SIDE, BUY},
                {ORDER_TYPE, MARKET },
                {QUANTITY, 0.00155.ToString() },
                {TRADE_ID_NEW, Guid.NewGuid().ToString() },
                {RESPONSE_TYPE, FULL   }
            };
            try
            {
                var response2 = binanceRest.NewOrderAsync(data).Result;
                return;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error executing test order: {e.Message}");
            }
            return;
        }
        /* This is the API calling method for executing trades. The old system of messaging through Add Request is now dropped */
        bool GetAccountInfo(BinanceAPI binanceRest)
        {
            accountData = null;
            try
            {
                var response = binanceRest.AccountAsync().Result;
                var accountDataUnfiltered = JsonConvert.DeserializeObject<AccountData>(response.ToString());
                accountData = new AccountDataFiltered
                {
                    Balances = accountDataUnfiltered.Balances
                };

                double totalEquity = 0;
                string errorMsg = string.Empty;
                string resultMsg = string.Empty;

                //e.g. BTCUSDT - BTC is the base asset, USDT is the quote asset
                //so for these calculations, BTC will always be the quote asset, the base asset will be ADA, IOTA etc
                // and there is a separate fiat asset
                //const string QUOTE_ASSET = "BTC";

                //can ignore locked amount, this is just for trades placed on the exchange which have not yet entered

                #region Get total equity
                foreach (var v in accountData.Balances.Where(x => x.Free > 0 || x.Locked > 0))
                {
                    if (SymbolNames.SYMBOLS_TO_EXCLUDE.Contains(v.Asset))
                    {
                        continue;
                    }

                    //get the quote asset: probably USDT, may be BTC
                    var quoteAsset = SymbolNames.GetQuoteAssetForBaseAsset(v.Asset);


                    if (v.Asset == SymbolNames.FIAT_ASSET || v.Asset == "BUSD" || v.Asset == "USDC")
                    {
                        //e.g. USDT
                        totalEquity += v.Free + v.Locked;
                    }
                    else if (quoteAsset == SymbolNames.FIAT_ASSET || quoteAsset == "BUSD" || quoteAsset == "USDC")
                    {
                        //e.g ADA, where ADAUSDT is available
                        (var lastSymbolValue, var s1) = GetValueForSymbol(v.Asset, quoteAsset);
                        totalEquity += (v.Free + v.Locked) * lastSymbolValue;
                    }
                    else if (quoteAsset == null)
                    {
                        //errorMsg += $"\n{v.Asset} not on system, not included in total equity";
                    }
                    else
                    {
                        //e.g LOOM, where only LOOMBTC is available
                        //so get BTCUSDT
                        (var lastQuoteValue, var s1) = GetValueForSymbol(quoteAsset, SymbolNames.FIAT_ASSET);
                        if (s1 != null)
                        {
                            errorMsg += $"\n{s1}";
                        }
                        else
                        {
                            (var lastSymbolValue, _) = GetValueForSymbol(v.Asset, quoteAsset);
                            totalEquity += (v.Free + v.Locked) * lastSymbolValue * lastQuoteValue;
                        }
                    }
                }
                #endregion

                foreach (var v in accountData.Balances
                            .Where(x => x.Free > 0 || x.Locked > 0)
                            .OrderBy(x => x.Free)
                            .ThenBy(x => x.Locked))
                {
                    if (SymbolNames.SYMBOLS_TO_EXCLUDE.Contains(v.Asset))
                    {
                        continue;
                    }
                    double value = 0;
                    bool noSymbol = false;
                    //get the quote asset: probably USDT, may be BTC
                    var quoteAsset = SymbolNames.GetQuoteAssetForBaseAsset(v.Asset);

                    if (v.Asset == SymbolNames.FIAT_ASSET || v.Asset == "BUSD" || v.Asset == "USDC")
                    {
                        //e.g. USDT
                        value = v.Free + v.Locked;
                    }
                    else if (quoteAsset == SymbolNames.FIAT_ASSET || quoteAsset == "BUSD" || quoteAsset == "USDC")
                    {
                        //e.g ADA, where ADAUSDT is available
                        (var lastSymbolValue, var s1) = GetValueForSymbol(v.Asset, quoteAsset);
                        if (s1 != null)
                        {
                            noSymbol = true;
                        }
                        else
                        {
                            value = (v.Free + v.Locked) * lastSymbolValue;
                        }
                    }
                    else
                    {
                        //e.g LOOM, where only LOOMBTC is available
                        //so get BTCUSDT
                        (var lastQuoteValue, var s1) = GetValueForSymbol(quoteAsset, SymbolNames.FIAT_ASSET);
                        if (s1 != null)
                        {
                            noSymbol = true;
                        }
                        else
                        {
                            (var lastSymbolValue, _) = GetValueForSymbol(v.Asset, quoteAsset);
                            value = (v.Free + v.Locked) * lastSymbolValue * lastQuoteValue;
                        }
                    }

                    var pc = value / totalEquity * 100;

                    //var free = $"{pc}%";
                    //if (v.Free > 0 && v.Locked == 0)
                    //{
                    //    free = "100";
                    //}
                    //else if (v.Free == 0 && v.Locked > 0)
                    //{
                    //    free = "0";
                    //}
                    //else
                    //{
                    //    free = ((v.Free + v.Locked) / v.Free * 100).ToString("0.00");
                    //}

                    //free += "% free";
                    if (noSymbol)
                    {
                        //resultMsg += $"{v.Asset}:\tno symbol\n";
                    }
                    else
                    {
                        resultMsg += $"{v.Asset}:\t{pc.ToString("0.0")}%\t\t${value.ToString("0.00")}\n";
                    }
                }

                resultMsg += $"\n------------------------------------\nTotal equity:\t${totalEquity.ToString("0.00")}\n------------------------------------\n";

                var btcEngine = MainObj.GetTradeEngineForSymbol("BTCUSDT");
                if (btcEngine != null && btcEngine.currentDataItem != null)
                {
                    resultMsg += $"\nLast BTC price:\t${btcEngine.currentDataItem.Close}";
                }

                resultMsg += $"{errorMsg}";

                if (showBalancesDialog)
                {
                    AlertDialog.Show(resultMsg, "Balances");
                }
                showBalancesDialog = false;

                LogTrades.WriteLine($"\n*** Total equity at {DateTime.Now} was ${totalEquity}"); //log since Binance can't really set this as the Balance for a trade entry or exit

                return true;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting account info: {e.Message}");
                return false;
            }
        }
        bool GetExchangeInfo(BinanceAPI binanceRest)
        {
            exchangeInfo = null;
            try
            {
                var response = binanceRest.ExchangeInfoAsync().Result;
                exchangeInfo = JsonConvert.DeserializeObject<ExchangeInfoData>(response.ToString());
                return true;
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Error getting exchange info: {e.Message}");
                return false;
            }
        }

        public override void StartEntryOrClose_Synchronised()
        {
            if (!dataReady) return;

            //Test();

            //return;

            try
            {
                if (DataContext != null)
                {
                    DataContext.SaveChanges(); //because system is still executing trade so won't save for a while if something hangs the db
                }
            }
            catch (Exception e)
            {
            }


            if (DateTime.UtcNow.Subtract(lastExchangeInfoRequest).TotalDays > 1)
            {
                exchangeInfo = null; //fetch a new data set, once a day should be fine to keep up to date with any changes
                lastExchangeInfoRequest = DateTime.UtcNow;
            }

            //todo - error handling

            //1) Get account info
            System.Text.Json.Nodes.JsonNode response;

            if (!GetAccountInfo(binanceRest))
            {
                LogTrades.WriteLine($"Could not get account info for {symbolName}, exiting StartEntryOrClose_Synchronised");
                KillEntryOrCloseOrder();
                return;
            }


            //2) Get exchange info, if not already present
            if (exchangeInfo == null)
            {
                if (!GetExchangeInfo(binanceRest))
                {
                    LogTrades.WriteLine($"Could not get exchange info for {symbolName}, exiting StartEntryOrClose_Synchronised");
                    KillEntryOrCloseOrder();
                    return;
                }
                //can have this as a static field, and only request it once. Otherwise, refresh every time a trade executes
                // sending symbol name just validates it's presence, it still returns the lot, (ApiParamNames.SYMBOL, symbolName));
            }

            //3 Execute the trade (duplicated from Node response handler, but this is now the main method for executing trades)
            if (Entry != null && Entry.ExchangeMethodScript != null)
            {
                switch (Entry.ExchangeMethodScript)
                {
                    #region New order
                    case NEW_ORDER_SCRIPT:

                        entryTimeStamp = DateTime.UtcNow;
                        //process is 
                        //1) get the account data, for the amount of BTC available
                        //2) get the price of all assets, and use account data to calculate a purchase amount

                        /*********************/
                        // Get quote price
                        /*********************/

                        LogTrades.WriteLine("Getting quote price");
                        response = binanceRest.TickerPriceAsync(BTCUSDT).Result;

                        /*********************/
                        // Generate Entry order
                        /*********************/

                        LogTrades.WriteLine("Generating entry order");
                        
                        var quoteData = JsonConvert.DeserializeObject<QuoteData>(response.ToString());
                        if (quoteData == null)
                        {
                            LogTrades.WriteLine($"Quote data was null in NEW_ORDER_SCRIPT for {symbolName}, exiting OnExchangeDataReceived");
                            KillEntryOrCloseOrder();
                            return;
                        }

                        ShowAccountBalances(); //will log the balance

                        //From the API docs:
                        // base asset refers to the asset that is the quantity of a symbol. For the symbol BTCUSDT, BTC would be the base asset.
                        // quote asset refers to the asset that is the price of a symbol. For the symbol BTCUSDT, USDT would be the quote asset.

                        (var baseAsset1, var quoteAsset) = SymbolNames.GetAssetsForSymbol(quoteData.Symbol);

                        if (quoteAsset == null)
                        {
                            LogTrades.WriteLine($"Quote asset was NULL for {quoteData.Symbol}, exiting OnExchangeDataReceived");
                            KillEntryOrCloseOrder();
                            return;
                        }
                        var assetBalance1 = accountData.Balances.Where(x => x.Asset == quoteAsset).Select(x => x.Free).FirstOrDefault();

                        //it is assumed only a single symbol is being traded if asset balance is being limited to 100, this will be a USDT balance as well
                        if (RESTRICTED_TRADE_VALUE > 0)
                        {
                            assetBalance1 = RESTRICTED_TRADE_VALUE;
                        }

                        //the symbol price is the ratio between the base and quote assets
                        var symbolPrice = quoteData.Price;

                        if (symbolPrice > 0 && MAX_CONCURRENT_TRADES > 0)
                        {
                            var displaySymbolName = Entry.SymbolName;

                            var numLiveQuoteAssetEntries = DataContext.Trades.Count(x =>
                                    x.Id != Entry.Id && //not really necessary, since the trade to exclude would have an exchange method script anyway
                                    x.Action == TradeAction.ENTRY &&

                                    x.Type != TradeType.STUB_ORDER && //this is only on Fiat, so would not be an issue in this context anyway but doesn't hurt to leave it in for future reference
                                    (
                                        (x.ItemStatus == TradeItemStatus.OPEN && x.GlobalStatus == TradeGlobalStatus.ACTIVE) &&
                                        x.ExchangeMethodScript == null
                                    )
                                );

                            if (numLiveQuoteAssetEntries >= MAX_CONCURRENT_TRADES)
                            {
                                //this should not happen, but just in case...
                                KillEntryOrCloseOrder();
                                MainObj.DrawCurrentDispatcher();
                                LogTrades.WriteLineOrder($"Could not enter trade as there are already {numLiveQuoteAssetEntries} entries running at {DateTime.UtcNow}");
                                LogTrades.WriteLine($"Maximum number of concurrent trades exceded for {displaySymbolName}, not completing trade (Get Quote Price).");
                                return;
                            }

                            //the fractional trade amount is the amount of e.g. ADABTC which must be purchased relative to the available equity
                            //this will be 0.25, 0.33, 0.5, 1 for a max. of 4 trades
                            double baseAssetTradeAmount = 1 / (double)(TradeEngine.MAX_CONCURRENT_TRADES - numLiveQuoteAssetEntries);

                            var coinBalance = assetBalance1 / symbolPrice;
                            coinBalance *= baseAssetTradeAmount;

                            //it is possible that the asset price will rise slightly during the order execution
                            //this will only affect the final trade, as there will not be enough quote asset to fill the order.
                            //Easiest way to avoid this is to leave 0.5% quote asset, this will aggregate up to 2% for the final trade.
                            //So this would always leave a small floating balance of the quote asset (i.e. BTC) but would sell out 98%
                            coinBalance *= 0.995;

                            (var amount, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                            if (amount > 0)
                            {
                                LogTrades.WriteLine($"Buying {amount} of {baseAsset1}");

                                var query = new Dictionary<string, string> {
                                    {SYMBOL, symbolName},
                                    {SIDE, BUY},
                                    {ORDER_TYPE, MARKET},
                                    {QUANTITY, amount.ToString()},
                                    {TRADE_ID_NEW, Entry.ItemId }
                                };

                                response = binanceRest.NewOrderAsync(query).Result;
                            }
                            else
                            {
                                LogTrades.WriteLine($"Quote asset balance was zero so could not enter trade for {symbolName}");
                            }
                        }

                        /*********************/
                        // Set order on system
                        /*********************/

                        LogTrades.WriteLine("Setting order on system");
                        var dataEntry = JsonConvert.DeserializeObject<OrderData>(response.ToString());
                        if (dataEntry == null)
                        {
                            LogTrades.WriteLine($"Order data was null for {symbolName}, exiting OnExchangeDataReceived");
                            KillEntryOrCloseOrder();
                            return;
                        }

                        if (Entry.ItemId != dataEntry.ClientOrderId)
                        {
                            LogTrades.WriteLine($"Mismatched ItemId and ClientOrderId for {symbolName} in NEW_ORDER_SCRIPT. ItemId has been changed to {dataEntry.ClientOrderId}.");
                            Entry.ItemId = dataEntry.ClientOrderId;
                            SetUpdateRequest(Entry);
                        }

                        switch (dataEntry.Status)
                        {
                            case "FILLED":
                                LogTrades.WriteLineOrder($"\nOrder FILLED for {Entry.SymbolName} at {DateTime.UtcNow}");

                                //this will be either the second phase from a QTB buy, or the first in a regular buy
                                // - enter the trade on the system

                                //must find stops, etc. here so the minimum values can be set if they are wrong side of entry
                                //NOTE - UNTESTED, THIS IS JUST FROM THE BITMEX SIDE
                                var trades = AllTrades.Where(x => x.TradeReference == Order.TradeReference);
                                Ceiling = trades.FirstOrDefault(x => x.Action == TradeAction.CEILING);
                                TakeProfit = trades.FirstOrDefault(x => x.Action == TradeAction.TAKE_PROFIT);
                                Floor = trades.FirstOrDefault(x => x.Action == TradeAction.FLOOR);
                                Stop = trades.FirstOrDefault(x => x.Action == TradeAction.STOP);

                                //price will be zero on testnet, so use the current price instead
                                dataEntry.Price = dataEntry.Price == 0 ? (decimal)currentDataItem.Close : dataEntry.Price;
                                EnterTradeOnSystem_synchronised(dataEntry.Price, dataEntry.ExecutedQty, TradeExchangeStatus.ENTERED_ON_EXCHANGE);

                                EndEntryOrCloseScript();

                                break;
                            case "CANCELED":
                            case "REJECTED":
                            case "EXPIRED":
                                LogTrades.WriteLineOrder($"Order was rejected for {Entry.SymbolName} as {dataEntry.Status} at {DateTime.UtcNow}");
                                KillEntryOrCloseOrder();
                                break;
                            default:
                                LogTrades.WriteLine($"dataEntry.Status was {dataEntry.Status}");
                                break;
                        }

                        tradeStatusChanged = true;
                        break;

                    #endregion

                    #region Close order
                    case CLOSE_ORDER_SCRIPT:

                        entryTimeStamp = DateTime.UtcNow;
                        if (Close == null)
                        {
                            LogTrades.WriteLine($"No Close set in CLOSE_ORDER_SCRIPT for {symbolName}, cannot close trade, exiting OnExchangeDataReceived");
                            KillEntryOrCloseOrder(true);
                            return;
                        }
                        /*********************/
                        //get account balances
                        /*********************/

                        LogTrades.WriteLine("Cancelling stops: fetching account data...");
                        if (!GetAccountInfo(binanceRest))
                        {
                            LogTrades.WriteLine($"Could not get account info for {symbolName}, exiting CLOSE_ORDER_SCRIPT");
                            KillEntryOrCloseOrder(true);
                            return;
                        }
                        /****************************************************************************/
                        //cancel stops if they are still present, but if trade has closed, we are done
                        /****************************************************************************/

                        LogTrades.WriteLine("Cancelling stops: account data fetched");

                        //if the base asset balance is very small, assume the trade has exited on the exchange, so go no further

                        (bool _noLockedAsset, bool _noFreeAsset) = IsTradeClosed();
                        if (_noLockedAsset && _noFreeAsset) //i.e there is no BTC
                        {
                            LogTrades.WriteLine("No BTC, closing trade locally.");
                            CloseTradeOnSystem_synchronised((decimal)currentDataItem.Close);
                        }
                        else
                        {
                            if (_noLockedAsset)
                            {
                                //if there is no locked balance, assume the stop has been removed from the exchange
                                //in which case, we already have the balances so can move on to closing the trade
                                //Entry.ExchangeMethodIndex = 3; //because it will get incremented again to 4
                            }
                            else
                            {
                                if (Stop != null && Stop.ItemId != null)
                                {
                                    LogTrades.WriteLine("Locked balance on exchange, cancelling stops...");

                                    var query = new Dictionary<string, string>
                                    {
                                        {SYMBOL, symbolName},
                                        {ORIG_CLIENT_ORDER_ID, Stop.ItemId }
                                    };
                                    binanceRest.CancelOrderAsync(query).Wait();
                                }
                            }

                        }
                        /**************************/
                        //get account balances again
                        /**************************/

                        LogTrades.WriteLine("Cancelling stops: fetching account data second time...");
                        if (!GetAccountInfo(binanceRest))
                        {
                            LogTrades.WriteLine($"Could not get account info for {symbolName}, exiting CLOSE_ORDER_SCRIPT");
                            KillEntryOrCloseOrder(true);
                            return;
                        }

                        /*********************/
                        //generate close order
                        /*********************/

                        //a QUANTITY order specifies the amount of base asset to buy or sell, so can go directly from here

                        LogTrades.WriteLine($"Closing order for {symbolName}...");
                        var _baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                        if (_baseAsset != null)
                        {
                            var coinBalance = accountData.Balances.Where(x => x.Asset == _baseAsset).Select(x => x.Free).FirstOrDefault();

                            LogTrades.WriteLine($"Using received balance of {coinBalance} {_baseAsset}");

                            /***************************************/
                            //retain some BNB to reduce trading fees
                            /***************************************/

                            if (_baseAsset == "BNB" && RETAINED_BNB_PERCENTAGE > 0)
                            {
                                coinBalance *= (1 - RETAINED_BNB_PERCENTAGE / 100);
                                LogTrades.WriteLine($"Retaining {RETAINED_BNB_PERCENTAGE}% of BNB");
                            }

                            (var amount1, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                            if (amount1 > 0)
                            {
                                Close.Quantity = amount1;

                                LogTrades.WriteLine($"Placing sell order of {coinBalance}, adjusted for step size, at {DateTime.Now}");
                                SetUpdateRequest(Close);

                                var query = new Dictionary<string, string> {
                                    {SYMBOL, symbolName},
                                    {SIDE, SELL},
                                    {ORDER_TYPE, MARKET},
                                    {QUANTITY, amount1.ToString()},
                                    {TRADE_ID_NEW, Close.ItemId }
                                };

                                response = binanceRest.NewOrderAsync(query).Result;

                                /*********************/
                                //Set order on system
                                /*********************/

                                LogTrades.WriteLine($"Received close balance at {DateTime.Now}");
                                //if status is 'NEW' then order is accepted but not yet filled
                                //status could be 'PARTIALLY_FILLED' (limit orders only??)
                                var dataClose = JsonConvert.DeserializeObject<NewOrderData>(response.ToString());
                                if (dataClose == null)
                                {
                                    LogTrades.WriteLine($"Order data was null in CLOSE_ORDER_SCRIPT for {symbolName}, exiting OnExchangeDataReceived");
                                    KillEntryOrCloseOrder(true);
                                    return;
                                }

                                switch (dataClose.Status)
                                {
                                    case "FILLED":
                                        //price will be zero on testnet, so use the current price instead
                                        dataClose.Price = dataClose.Price == 0 ? (decimal)currentDataItem.Close : dataClose.Price;

                                        LogTrades.WriteLineOrder($"Close order filled, closing trade on system at {DateTime.UtcNow}");
                                        CloseTradeOnSystem_synchronised(dataClose.Price);
                                        EndEntryOrCloseScript();
                                        LogTrades.WriteSpacer();


                                        break;

                                    case "CANCELED":
                                    case "REJECTED":
                                    case "EXPIRED":
                                        //can try again
                                        LogTrades.WriteLineOrder($"Trade was {dataClose.Status} in CLOSE_ORDER_SCRIPT for {Close.SymbolName} at {DateTime.UtcNow}");
                                        KillEntryOrCloseOrder(true);

                                        break;
                                }

                                tradeStatusChanged = true;

                                ShowAccountBalances(); //will log balance
                            }
                            else
                            {
                                LogTrades.WriteLine($"Quote asset balance was zero so could not close trade for {symbolName}"); //this can happen multiple times if unsyncronised with server
                            }
                        }

                        break;
                    #endregion

                    #region Update live state
                    case UPDATE_LIVE_STATE_SCRIPT:

                        if (!GetAccountInfo(binanceRest))
                        {
                            LogTrades.WriteLine($"Could not get account info for {symbolName}, exiting UPDATE_LIVE_STATE_SCRIPT");
                            return;
                        }

                        var baseAsset = SymbolNames.GetBaseAssetForSymbol(Entry.SymbolName); //i.e. BTC
                        if (baseAsset == null) //this would never happen anyway
                        {
                            LogTrades.WriteLine("Could not update live state, base asset was null");
                            Entry.ExchangeMethodScript = null;
                            SetUpdateRequest(Entry);
                            break;
                        }

                        //if the base asset balance is very small, assume the trade has exited

                        //todo - get the correct minimum trade value for the asset and calculate an appropriate small value
                        (bool noLockedAsset, bool noFreeAsset) = IsTradeClosed();
                        if (noLockedAsset && noFreeAsset) //i.e there is no BTC
                        {
                            //this is not reliable so cannot kill the order here. It may be that the data lags the order slightly, or the stop removes the free
                            // balance but does not register a locked balance immediately. Either way, this is killing the order sometimes
                            LogTrades.WriteLine("Order balance is (close to) zero, probably stopped on exchange.");
                            (var lastValue, var _) = GetValueForSymbol(symbolName);
                            //CloseTradeOnSystem_synchronised((decimal)lastValue);

                            //also, mark as not synched (same as with Bitmex) in case this data is not returned correctly from the exchange
                            // NOTE - if this is a problem, just don't close the order here.

                            Entry.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                            SetUpdateRequest(Entry);
                        }

                        //if there is no locked balance, assume the stop has been removed from the exchange
                        else if (noLockedAsset)
                        {
                            if (Stop != null && Stop.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE && Stop.ExchangeMethodScript != EXCHANGE_NOT_SYNCHED)
                            {
                                //var st = Stop;
                                //RemoveTradeFromLine_UI_synchronised(st, false);
                                //SetUpdateRequest(st, TradeUpdateRequest.DELETE);

                                //stop may get removed from exchange but not replaced, in which case must not remove local stop as well!!
                                Stop.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                                SetUpdateRequest(Stop);
                                LogTrades.WriteLine("No locked balance on exchange, assume stop cancelled, check exchange!!");
                            }
                        }
                        else
                        {
                            Entry.ExchangeMethodScript = null;
                            SetUpdateRequest(Entry);
                        }


                        break;
                        #endregion

                }
                if (Stop != null && Stop.ExchangeMethodScript != null)//  || (TempStop != null && TempStop.ExchangeMethodScript != null))
                {

                    switch (Stop.ExchangeMethodScript)
                    {
                        #region Set stop
                        case SET_STOP_SCRIPT:

                            //Stop will not be null here

                            //process is:
                            //1) remove any existing stop
                            //2) set a new stop to the current order value

                            /*********************/
                            //remove existing stop
                            /*********************/

                            if (!string.IsNullOrEmpty(Stop.ItemId))
                            {
                                response = binanceRest.CancelOrderAsync(new Dictionary<string, string>
                                {
                                    {SYMBOL, symbolName},
                                    {ORIG_CLIENT_ORDER_ID, Stop.ItemId }
                                }).Result;

                                //kill the stop ItemId - 
                                // if the stops are cancelled on the exchange, but the confirmation is not received, as seems to happen quite often,
                                // then the original ItemId will persist here and next time, an attempt to remove this stop from the exchange will fail
                                // so the stop will never get put on the exchange.
                                Stop.ItemId = null;
                                SetUpdateRequest(Stop);
                            }

                            if (!GetAccountInfo(binanceRest))
                            {
                                LogTrades.WriteLine($"Could not get account info for {symbolName}, exiting SET_STOP_SCRIPT");
                                KillEntryOrCloseOrder(true);
                                return;
                            }

                            /*********************/
                            //set new stop
                            /*********************/

                            //don't want to pull the quote data each time a stop gets updated, so will just use the curent ticker price
                            (var lastValue, var _) = GetValueForSymbol(symbolName);

                            Stop.ItemId = Guid.NewGuid().ToString();
                            SetUpdateRequest(Stop);
                            var baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                            var coinBalance = accountData.Balances.Where(x => x.Asset == baseAsset).Select(x => x.Free).FirstOrDefault();

                            (var amount, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                            /*
                            An OCO has 2 legs called the above leg and below leg.
                            One of the legs must be a LIMIT_MAKER order and the other leg must be STOP_LOSS or STOP_LOSS_LIMIT order.
                            Price restrictions:
                            If the OCO is on the SELL side: LIMIT_MAKER price > Last Traded Price > stopPrice
                            If the OCO is on the BUY side: LIMIT_MAKER price < Last Traded Price < stopPrice
                            */

                            //this is calling the rest.js method correctly, so the error is within this data
                            //(-1104 "not all sent parameters were read")
                            //(-1106 "a parameter was sent when not required")
                            //(-1102 "A mandatory parameter was not sent, was empty/null, or malformed")
                            //(-1128 "Combination of optional parameters invalid.")
                            //(-2011 "Unknown order sent")
                            //(-1013 - this was "Stop loss orders are not supported for this symbol." but is not documented as an error code (only 1014 and beyond)

                            //can swap prices around, still get the same -1013 error
                            //"Stop loss orders are not supported for this symbol."


                            float abovePrice = 0;
                            if (TakeProfit != null)
                            {
                                abovePrice = (float)TakeProfit.Price;
                            }
                            else
                            {
                                //must have an initial t/p for the OCO. In case the auto-set ceiling has been removed for some reason, make the calculation here
                                var initialTP = 0.1; //10%
                                abovePrice = (float)(
                                    Entry.Position == TradePosition.LONG
                                        ? lastValue * (1 + initialTP)
                                        : lastValue * (1 - initialTP)
                                ); //same calculation as would be used for ceiling
                            }
                            float triggerPrice = (float)Stop.Price;

                            //this is just to ensure the stop executes immediately, not as a limit order
                            //but if the belowPrice is too low (like half the trigger price) a small order (test order) may not have a large enough notional value
                            float belowPrice = (float)(triggerPrice * 0.9);
                            //var symbol = exchangeInfo.Symbols.FirstOrDefault(x => x.Symbol == symbolName);
                            //var minSize = symbol.Filters.Where(x => x.FilterType == "NOTIONAL").FirstOrDefault();

                            /*************************************************************/
                            /* THIS IS DEPRECATED, DIFFERENT PARAMETERS NOW */

                            response = binanceRest.NewOcoAsync(new Dictionary<string, string>
                            {
                                {SYMBOL, Stop.SymbolName},
                                {SIDE, SELL},
                                {QUANTITY, coinBalance.ToString()},         //generates -1013 for btc or usdt value - minimum trade value is 0.0001 BTC
                                {ABOVE_TYPE, LIMIT_MAKER},
                                {ABOVE_PRICE, abovePrice.ToString()},       //generates -1102 if missing
                                {BELOW_TYPE, STOP_LOSS_LIMIT},
                                {BELOW_STOP_PRICE, triggerPrice.ToString()},//generates -1128 if missing
                                {BELOW_PRICE, belowPrice.ToString()},       //this is needed also
                                {BELOW_TIMEINFORCE, "GTC"}
                                //{ABOVE_STOP_PRICE, 70000}, //so presumably this would be for a buy, not a sell
                                //{ABOVE_TIMEINFORCE, 1000},
                                //{TRADE_ID_NEW, Stop.ItemId} //generates -1104 if sent
                                //{RESPONSE_TYPE, FULL}
                                //{TIME_IN_FORCE, GTC}- default anyway
                            }).Result;

                            var data = JsonConvert.DeserializeObject<NewOCOData>(response.ToString());
                            if (data != null)
                            {
                                var id = data.OrderReports
                                    .Where(x => x.Type == "STOP_LOSS_LIMIT")
                                    .Select(x => x.ClientOrderId)
                                    .FirstOrDefault(); //if this is set, then this stop can be updated next time. Not sure if you can also use the list id instead of this
                                Stop.ItemId = id;
                                Stop.ExchangeMethodScript = null;
                                Stop.ExchangeMethodIndex = 0;
                                Stop.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                                SetUpdateRequest(Stop);

                                if (isManualSynch)
                                {
                                    LogTrades.WriteLine("OCO synchronised");
                                    isManualSynch = false;
                                }
                            }

                            tradeStatusChanged = true;
                            break;

                        #endregion

                        #region Cancel stop
                        case CANCEL_STOP_SCRIPT:

                            response = binanceRest.CancelOrderAsync(new Dictionary<string, string>
                            {
                                {SYMBOL, symbolName},
                                {ORIG_CLIENT_ORDER_ID, Stop.ItemId}
                            }).Result;

                            //todo - needs error handling
                            RemoveTradeFromLine_UI_synchronised(Stop);
                            SetUpdateRequest(Stop, TradeUpdateRequest.DELETE);
                            break;

                            #endregion
                    }
                }
            }
            MainObj.DrawCurrentDispatcher();
        }
        _Trade GetOppositeOrder(TradePosition position)
        {
            var oppositeSide = position == TradePosition.LONG ? TradePosition.SHORT : TradePosition.LONG;
            return AllTrades.FirstOrDefault(x =>
                                            x.Action == TradeAction.ORDER &&
                                            x.Position == oppositeSide &&
                                            x.ItemStatus == TradeItemStatus.OPEN &&
                                            x.GlobalStatus == TradeGlobalStatus.ACTIVE);
        }

        protected override void EnterTradeOnSystem_synchronised(decimal price, double quantity, TradeExchangeStatus orderType)
        {

            //note, Quantity is double not decimal, as decimal has 18 digits and 2 decimal places only in db
            try
            {
                hasSpiked = false;

                var currentTradeIndicator = GetCurrentTradeIndicator(Order);

                //get the EF tracked trade - if this is a live entry, will be using a different data context to the one the parameter is running on

                Order.ExchangeStatus = orderType;
                if (MAX_CONCURRENT_TRADES > 0) //obviously it cannot be zero but doesn't hurt to check
                {
                    Order.ActionValue = 1.0 / (double)MAX_CONCURRENT_TRADES; //set this as a record of the equity fraction traded, so this will be copied over to Entry and Close
                }
                SetUpdateRequest(Order);

                //set trade time, frame and price to entry point (line entry will be different from order position)
                //enter trade on system here, but not active yet
                if (Entry == null)
                {
                    //if not trading live, will bypass exchange (if not using simulator) and come straight here, so no entry yet
                    Entry = Order.Clone();
                    Entry.Action = TradeAction.ENTRY;
                    Entry.Timestamp = GetTimeStamp();
                    SetUpdateRequest(Entry);
                }

                LogTrades.WriteLine(string.Format("{0} {1} entered at {2}.\nTrade complete.", Order.SymbolName, Order.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE ? "LIVE" : "TEST", DateTime.UtcNow));
                LogTrades.WriteSpacer();
                Entry.Price = Order.Price = (double)price;
                Entry.Quantity = Order.Quantity = quantity; //set both the entry and order quantity, as either may be used when closing, etc.
                Entry.ExchangeStatus = orderType;
                SetUpdateRequest(Entry);

                Order.ItemStatus = TradeItemStatus.CLOSED; //close the order, this is now an active trade
                SetUpdateRequest(Order);


                #region Set flip from opposing trade
                //if there is a stop or floor set, then use this and ignore any opposite trade entirely
                // 
                //otherwise, if there is an opposing trade:
                // 1) convert any opposing trade stop to waiting stop
                // 2) convert opposing trade to flip order

                var stop = AllTrades.FirstOrDefault(x =>
                        x.Action == TradeAction.STOP &&
                        x.TradeReference == Order.TradeReference);
                var floor = AllTrades.FirstOrDefault(x =>
                        x.Action == TradeAction.FLOOR &&
                        x.TradeReference == Order.TradeReference);
                if (stop == null && floor == null)
                {

                    var firstOppositeOrder = GetOppositeOrder(Order.Position);
                    if (firstOppositeOrder != null)
                    {

                        // remove any stop and floor from this trade since the opposing order will become a flip order stop and any other stop will either 
                        // invalidate this or be left orphaned when this triggers

                        //if (stop != null)
                        //{
                        //    //remove any line
                        //    var stopLine = AllLinesForSymbol.FirstOrDefault(x => x.TradeId == stop.Id);
                        //    if (stopLine != null)
                        //    {
                        //        SetUpdateRequest(stopLine, TradeUpdateRequest.FORCE_DELETE);
                        //    }

                        //    SetUpdateRequest(stop, TradeUpdateRequest.FORCE_DELETE);
                        //}

                        //if (floor != null)
                        //{
                        //    SetUpdateRequest(floor, TradeUpdateRequest.FORCE_DELETE);
                        //}

                        // 1) convert any opposing trade stop to waiting stop and remove any opposite take profit, ceiling or break even trades
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


                        // 2) convert opposing trade to flip order (which is also a stop)
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
                }

                #endregion


                //note, will set floor, ceiling, stop and tp quantities here, they are not needed but this is consistent with stops etc. which
                // are added after the trade goes live, as the quantity will then be copied over

                #region Initial take profit

                if (INITIAL_TAKE_PROFIT_PERCENT_MARKET > 0)
                {
                    bool canResetTP = true;

                    //for debugging, will not have a price from the simulated exchange so will use the original order price
                    var entryPrice = Entry.Price != 0 ? Entry.Price : Order.Price;
                    //take profit trade is just a permanent trigger so it does not use Price, only ActionValue 
                    var tpPrice = Order.Position == TradePosition.LONG
                        ? entryPrice * (1 + INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100)
                        : entryPrice * (1 - INITIAL_TAKE_PROFIT_PERCENT_MARKET / 100);

                    if (Ceiling != null)
                    {
                        //check it is within a minimum threshold, if not then reset
                        //untested
                        canResetTP = Ceiling.Position == TradePosition.LONG ? Ceiling.Price < tpPrice : Ceiling.Price > tpPrice;
                    }
                    else
                    {
                        Ceiling = Order.Clone();
                    }
                    if (canResetTP)
                    {
                        Ceiling.Action = TradeAction.CEILING;
                        Ceiling.Timestamp = GetTimeStamp();
                        Ceiling.ActionValue = Ceiling.Price = tpPrice;
                        SetUpdateRequest(Ceiling);
                    }
                }

                if (Ceiling != null)
                {
                    Ceiling.Quantity = Entry.Quantity;
                    SetUpdateRequest(Ceiling);
                }
                #endregion

                // This will ensure stop or floor is not too close.
                void SetInitialStopFloor(_Trade _floorStop)
                {
                    _floorStop.Quantity = -Entry.Quantity; //although this is also set later
                    SetUpdateRequest(_floorStop);

                    //if (_floorStop.)
                    //make sure it is not too close to price
                    AdjustStopPriceToMinimum_Synchronised(Entry, _floorStop, STOP_MIN_VALUE);
                }

                #region Initial floor

                if (Floor != null)
                {
                    SetInitialStopFloor(Floor);
                }

                #endregion

                #region Set stop price to minimum value

                if (Stop != null)
                {
                    SetInitialStopFloor(Stop);
                }

                if (TakeProfit != null)
                {
                    TakeProfit.Quantity = Entry.Quantity;
                    SetUpdateRequest(TakeProfit);
                }

                #endregion

                if (Stop == null && Floor == null)
                {
                    //var entryPrice = Entry.Price != 0 ? Entry.Price : Order.Price;
                    ////take profit trade is just a permanent trigger so it does not use Price, only ActionValue 
                    //var floorPrice = Order.Position == TradePosition.LONG
                    //    ? entryPrice * (1 - STOP_MIN_VALUE / 100)
                    //    : entryPrice * (1 + STOP_MIN_VALUE / 100);


                    //var _floor = Entry.Clone();
                    //Floor = _floor;
                    //_floor.Price = floorPrice;
                    //_floor.Action = TradeAction.FLOOR;
                    //SetUpdateRequest(Floor);
                }
                if (Entry.Price == 0)
                {
                    LogTrades.WriteLine($"New entry price was 0 for {Order.SymbolName} in EnterTradeOnSystem()");
                }
            }
            catch (Exception e)
            {
                LogTrades.WriteLine($"Exception in EnterTradeOnSystem  at {DateTime.UtcNow} (1): {e}");
            }

            if (!MainObj.IsBacktesting)
            {
                //MainObj.PlaySound(MainObj.ALERT_SOUND);
            }
        }

        protected override void KillEntryOrCloseOrder(bool interrupted = false)
        {
            base.KillEntryOrCloseOrder(interrupted);
            accountData = null;
        }

        protected override void EndEntryOrCloseScript()
        {
            base.EndEntryOrCloseScript();
            accountData = null;
        }

        #endregion

        (bool, bool) IsTradeClosed()
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
                        var _minValue = lotSize.MinQty * 2; //it should always use the lot size notional value, same as MinQty, to calculate trade quantity so would only leave < MinQty but double this to be safe
                        var _baseAssetBalanceFree = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Free).FirstOrDefault();
                        var _baseAssetBalanceLocked = accountData.Balances.Where(x => x.Asset == __baseAsset).Select(x => x.Locked).FirstOrDefault();
                        return (_baseAssetBalanceLocked < _minValue, _baseAssetBalanceFree < _minValue); //i.e there is no BTC
                    }
                }
            }
            return (false, false);
        }

        #region Stop 
        public override void SynchStopsWithExchange_synchronised()
        {
            InitForDataContext(() =>
            {
                if (Entry != null)
                {
                    if (Stop != null)
                    {
                        isManualSynch = true;
                        SetStopOnExchange_Synchronised(Stop);

                        //do a timed check to make sure this has synchronised correctly
                        var timer = new System.Timers.Timer(2000)
                        {
                            AutoReset = false
                        };
                        timer.Start();
                        timer.Elapsed += (o, e) =>
                        {
                            if (isManualSynch)
                            {
                                LogTrades.WriteLine("Failed to synchronise OCO with exchange");
                                isManualSynch = false;
                            }
                        };
                    }
                }
                else
                {
                    LogTrades.WriteLine("Not entered on exchange - cannot synchronise stops with exchange.");
                }
            });
        }

        public override async void SetStopOnExchange_Synchronised(_Trade stop)
        {
            if (SET_STOPS_ON_EXCHANGE == false)
            {
                return;
            }

            if (Entry.ExchangeStatus != TradeExchangeStatus.ENTERED_ON_EXCHANGE)
            {
                return; //no warning, this will just repeat
            }
            base.SetStopOnExchange_Synchronised(stop);

            if (stop.Action == TradeAction.STOP || stop.Stop == TradeStop.TAKE_PROFIT_MA_CROSS) //floors are never set on the exchange in Binance, but a stop will be replaced by a MA floor so use this as well
            {
                if (stop.ExchangeStatus == TradeExchangeStatus.ENTERED_OFF_EXCHANGE)
                {

                    LogTrades.WriteLine($"Did not set stop on exchange, stop exchange status was {stop.ExchangeStatus} - you may need to add an OCO manually.");
                    stop.ExchangeStatus = TradeExchangeStatus.NO_WARNING;
                    SetUpdateRequest(stop);
                    return;
                }

                stop.ExchangeMethodScript = SET_STOP_SCRIPT;
                stop.ExchangeMethodIndex = !string.IsNullOrEmpty(stop.ItemId) ? 0 : 1;
                StartEntryOrClose_Synchronised();
            }
            return;
        }

        protected override void RemoveStopFromExchange_Synchronised(_Trade stop)
        {
            if (SET_STOPS_ON_EXCHANGE == false)
            {
                return;
            }
            stop.ExchangeMethodScript = CANCEL_STOP_SCRIPT;
            stop.ExchangeMethodIndex = 0;
            StartEntryOrClose_Synchronised();
        }

        protected override void RemoveTradeBeforeExchangeRemoval_Synchronised(_Trade trade, bool removeLine, bool allowForceRemove)
        {
            if (!allowForceRemove && trade.Action == TradeAction.STOP)
            {
                return;
            }
            RemoveTradeFromLine_UI_synchronised(trade, removeLine);
            SetUpdateRequest(trade, TradeUpdateRequest.DELETE);
        }

        #endregion

        #region Update state from server
        protected override async void UpdateLiveState()
        {

            return;

            if (USE_SIMULATED_EXCHANGE)
            {
                return;
            }

            InitForDataContext(() =>
            {

                //in the event there is a pending entry or stop operation on the exchange, do not do anything. This is unlikely but it would mess things up.
                if (Stop == null || Stop != null && Stop.ExchangeMethodScript == null)
                {
                    //UpdateLiveState() only checks balances, to see if the trade is on the exchange. Therefore, only do this if the trade is not entering, etc.
                    //The timeout function will deal with orders which timeout, but not very well - if the trade has not entered, it should be removed 
                    // - BUT - that is not reliable, so cannot really close or remove orders ever. Just mark as not synchronised.
                    if (Entry != null && Entry.ExchangeMethodScript == null && Entry.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE)
                    {
                        //this is tricky with Binance, because there is no live running trade - only account balances
                        //so if there is a quote asset balance, the trade is still running. Otherwise, close it entirely.
                        Entry.ExchangeMethodIndex = 0;
                        Entry.ExchangeMethodScript = UPDATE_LIVE_STATE_SCRIPT;
                        //LogTrades.WriteLine("Updating live state...");
                        StartEntryOrClose_Synchronised();
                    }
                }
            });
        }

        #endregion

        #region Utility methods  

        private (double, double) GetCorrectPrecisionForTrade(string symbolName, double balanceToTrade)
        {
            if (balanceToTrade > 0) //just in case there are too many active trades for some reason, which would present a negative value
            {
                if (USE_SIMULATED_EXCHANGE)
                {
                    return (balanceToTrade, 1);
                }
                if (exchangeInfo != null)
                {
                    var symbol = exchangeInfo.Symbols.Where(x => x.Symbol == symbolName).FirstOrDefault();
                    if (symbol == null)
                    {
                        LogTrades.WriteLine($"Could not find symbol in ExchangeInfo for {symbolName}");
                        return (-1, 0);
                    }

                    var lotSize = symbol.Filters.Where(x => x.FilterType == "LOT_SIZE").FirstOrDefault();
                    if (lotSize == null)
                    {
                        LogTrades.WriteLine($"Could not find lot size in ExchangeInfo for {symbolName}");
                        return (-1, 0);
                    }
                    var stepSize = lotSize.StepSize;
                    var ret = (int)(balanceToTrade / stepSize) * stepSize;
                    var p = (int)Math.Log10(stepSize);
                    ret = Math.Round(ret, -p);
                    return (ret, stepSize);
                }
            }
            return (-1, 0);
        }

        //protected override void Binance_StartSimulation()
        //{
        //    exchangeInfo = new ExchangeInfoData();
        //    if (!connectionInitialised)
        //    {
        //        connectionInitialised = true;
        //        //Binance_InitEvents();
        //    }
        //    dataReady = true;
        //}
        #endregion

        #region Node.js process management (deprecated)
        //entire method to be replaced with API calls in StartEntryOrClose_Synchronised(...)
        /*
        internal void OnDataReceivedForTrade_synchronised(object result)
        {
            //this method is called during trade execution, the first steps are always to obtain exchange and account data
            // - exchange data may be persistant from a previous call

            void _RunExchangeTradeRequest(ExchangeRequestData pendingRequest)
            {
                if (!USE_SIMULATED_EXCHANGE)
                {
                    MainObj.exchangeConnector.AddRequest(
                        pendingRequest.exchangeRequestType,
                        ExchangeDataReceivedEventHandler,
                        (QueryParams.SYMBOL, pendingRequest.symbol),
                        (QueryParams.SIDE, pendingRequest.side),
                        (QueryParams.ORDER_TYPE, pendingRequest.orderType),
                        (QueryParams.QUANTITY, pendingRequest.quantity),
                        (QueryParams.TRADE_ID_NEW, pendingRequest.tradeIdNew),
                        (QueryParams.RESPONSE_TYPE, QueryParams.FULL)
                    //(ApiParamNames.TIME_IN_FORCE,   "GTC") - default anyway
                        );

                }
                else
                {
                    MainObj.exchangeConnector.AddRequest(
                        pendingRequest.exchangeRequestType,
                        ExchangeDataReceivedEventHandler,
                        (QueryParams.SYMBOL, pendingRequest.symbol),
                        (QueryParams.SIDE, pendingRequest.side),
                        (QueryParams.ORDER_TYPE, pendingRequest.orderType),
                        (QueryParams.QUANTITY, pendingRequest.quantity),
                        (QueryParams.TRADE_ID_NEW, pendingRequest.tradeIdNew),
                        (QueryParams.RESPONSE_TYPE, QueryParams.FULL),
                        //(ApiParamNames.TIME_IN_FORCE, "GTC"),
                        (QueryParams.PRICE, currentDataItem.Close) //add in price so this can be returned in the trade data
                        );
                }
            }

            void _CancelStopTradeRequest(ExchangeRequestData pendingRequest)
            {
                MainObj.exchangeConnector.AddRequest(
                       pendingRequest.exchangeRequestType,
                       ExchangeDataReceivedEventHandler,
                       (QueryParams.SYMBOL, pendingRequest.symbol),
                       //(ApiParamNames.ORDER_TYPE, pendingRequest.orderType),
                       (QueryParams.ORIG_CLIENT_ORDER_ID, pendingRequest.origClientOrderId)
                       //(ApiParamNames.RESPONSE_TYPE, ApiParamNames.FULL)
                       //(ApiParamNames.TIME_IN_FORCE,   "GTC") - default anyway
                       );
            }

            InitForDataContext(() =>
            {
                if (result is ExchangeInfoData)
                {
                    exchangeInfo = result as ExchangeInfoData;
                }
                else if (result is AccountDataFiltered)
                {
                    accountData = result as AccountDataFiltered;
                }


                //result may be OrderData, OpenOrdersData, QuoteData, NewOrderData, MyTradesData, AllOrderData
                // - these will be processed by the appropriate script section
                //note, NewOrderData is sent as OrderData, because only the Price is needed here, not the fills list etc.

                if (exchangeInfo == null || accountData == null)
                {
                    return; //still waiting for these to be returned
                }

                if (Entry != null && Entry.ExchangeMethodScript != null)
                {
                    switch (Entry.ExchangeMethodScript)
                    {
                        #region New order
                        case NEW_ORDER_SCRIPT:

                            entryTimeStamp = DateTime.UtcNow;
                            //process is 
                            //1) get the account data, for the amount of BTC available
                            //2) get the price of all assets, and use account data to calculate a purchase amount

                            switch (Entry.ExchangeMethodIndex)
                            {
                                case 0:
                                    // Get quote price
                                    LogTrades.WriteLine("Getting quote price");
                                    MainObj.exchangeConnector.AddRequest(ExchangeRequest.QUOTE_PRICE, ExchangeDataReceivedEventHandler, (QueryParams.SYMBOL, symbolName));
                                    SetUpdateRequest(Entry);
                                    break;

                                case 1:
                                    // Generate Entry order
                                    LogTrades.WriteLine("Generating entry order");
                                    var quoteData = result as QuoteData;
                                    if (quoteData == null)
                                    {
                                        LogTrades.WriteLine($"Quote data was null in NEW_ORDER_SCRIPT for {symbolName}, exiting OnExchangeDataReceived");
                                        KillEntryOrCloseOrder();
                                        return;
                                    }

                                    ShowAccountBalances(); //will log the balance

                                    //From the API docs:
                                    // base asset refers to the asset that is the quantity of a symbol. For the symbol BTCUSDT, BTC would be the base asset.
                                    // quote asset refers to the asset that is the price of a symbol. For the symbol BTCUSDT, USDT would be the quote asset.

                                    (var baseAsset1, var quoteAsset) = SymbolNames.GetAssetsForSymbol(quoteData.Symbol);

                                    if (quoteAsset == null)
                                    {
                                        LogTrades.WriteLine($"Quote asset was NULL for {quoteData.Symbol}, exiting OnExchangeDataReceived");
                                        KillEntryOrCloseOrder();
                                        return;
                                    }
                                    var assetBalance1 = accountData.Balances.Where(x => x.Asset == quoteAsset).Select(x => x.Free).FirstOrDefault();

                                    //it is assumed only a single symbol is being traded if asset balance is being limited to 100, this will be a USDT balance as well
                                    if (RESTRICTED_TRADE_VALUE > 0)
                                    {
                                        assetBalance1 = RESTRICTED_TRADE_VALUE;
                                    }

                                    //the symbol price is the ratio between the base and quote assets
                                    var symbolPrice = quoteData.Price;

                                    if (symbolPrice > 0 && MAX_CONCURRENT_TRADES > 0)
                                    {
                                        var displaySymbolName = Entry.SymbolName;

                                        var numLiveQuoteAssetEntries = DataContext.Trades.Count(x =>
                                                x.Id != Entry.Id && //not really necessary, since the trade to exclude would have an exchange method script anyway
                                                x.Action == TradeAction.ENTRY &&

                                                x.Type != TradeType.STUB_ORDER && //this is only on Fiat, so would not be an issue in this context anyway but doesn't hurt to leave it in for future reference
                                                (
                                                    (x.ItemStatus == TradeItemStatus.OPEN && x.GlobalStatus == TradeGlobalStatus.ACTIVE) &&
                                                    x.ExchangeMethodScript == null
                                                )
                                            );

                                        if (numLiveQuoteAssetEntries >= MAX_CONCURRENT_TRADES)
                                        {
                                            //this should not happen, but just in case...
                                            KillEntryOrCloseOrder();
                                            MainObj.DrawCurrentDispatcher();
                                            LogTrades.WriteLineOrder($"Could not enter trade as there are already {numLiveQuoteAssetEntries} entries running at {DateTime.UtcNow}");
                                            LogTrades.WriteLine($"Maximum number of concurrent trades exceded for {displaySymbolName}, not completing trade (Get Quote Price).");
                                            return;
                                        }

                                        //the fractional trade amount is the amount of e.g. ADABTC which must be purchased relative to the available equity
                                        //this will be 0.25, 0.33, 0.5, 1 for a max. of 4 trades
                                        double baseAssetTradeAmount = 1 / (double)(TradeEngine.MAX_CONCURRENT_TRADES - numLiveQuoteAssetEntries);

                                        var coinBalance = assetBalance1 / symbolPrice;
                                        coinBalance *= baseAssetTradeAmount;

                                        //it is possible that the asset price will rise slightly during the order execution
                                        //this will only affect the final trade, as there will not be enough quote asset to fill the order.
                                        //Easiest way to avoid this is to leave 0.5% quote asset, this will aggregate up to 2% for the final trade.
                                        //So this would always leave a small floating balance of the quote asset (i.e. BTC) but would sell out 98%
                                        coinBalance *= 0.995;

                                        (var amount, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                                        if (amount > 0)
                                        {
                                            LogTrades.WriteLine($"Buying {amount} of {baseAsset1}");

                                            _RunExchangeTradeRequest(new ExchangeRequestData
                                            {
                                                tradeId = Entry.Id,
                                                exchangeRequestType = ExchangeRequest.NEW_ORDER,
                                                symbol = symbolName,
                                                side = QueryParams.BUY,
                                                orderType = QueryParams.MARKET,
                                                quantity = amount,
                                                tradeIdNew = Entry.ItemId
                                            });
                                        }
                                        else
                                        {
                                            LogTrades.WriteLine($"Quote asset balance was zero so could not enter trade for {symbolName}");
                                        }
                                    }
                                    break;

                                case 2:
                                    // Set order on system
                                    //this will be hit on confirmation of order

                                    LogTrades.WriteLine("Setting order on system");
                                    var dataEntry = result as OrderData;
                                    if (dataEntry == null)
                                    {
                                        LogTrades.WriteLine($"Order data was null for {symbolName}, exiting OnExchangeDataReceived");
                                        KillEntryOrCloseOrder();
                                        return;
                                    }

                                    if (Entry.ItemId != dataEntry.ClientOrderId)
                                    {
                                        LogTrades.WriteLine($"Mismatched ItemId and ClientOrderId for {symbolName} in NEW_ORDER_SCRIPT. ItemId has been changed to {dataEntry.ClientOrderId}.");
                                        Entry.ItemId = dataEntry.ClientOrderId;
                                        SetUpdateRequest(Entry);
                                    }

                                    switch (dataEntry.Status)
                                    {
                                        case "FILLED":
                                            LogTrades.WriteLineOrder($"\nOrder FILLED for {Entry.SymbolName} at {DateTime.UtcNow}");

                                            //this will be either the second phase from a QTB buy, or the first in a regular buy
                                            // - enter the trade on the system

                                            //must find stops, etc. here so the minimum values can be set if they are wrong side of entry
                                            //NOTE - UNTESTED, THIS IS JUST FROM THE BITMEX SIDE
                                            var trades = AllTrades.Where(x => x.TradeReference == Order.TradeReference);
                                            Ceiling = trades.FirstOrDefault(x => x.Action == TradeAction.CEILING);
                                            TakeProfit = trades.FirstOrDefault(x => x.Action == TradeAction.TAKE_PROFIT);
                                            Floor = trades.FirstOrDefault(x => x.Action == TradeAction.FLOOR);
                                            Stop = trades.FirstOrDefault(x => x.Action == TradeAction.STOP);

                                            EnterTradeOnSystem_synchronised(dataEntry.Price, dataEntry.ExecutedQty, TradeExchangeStatus.ENTERED_ON_EXCHANGE);
                                            
                                            EndEntryOrCloseScript();

                                            break;
                                        case "CANCELED":
                                        case "REJECTED":
                                        case "EXPIRED":
                                            LogTrades.WriteLineOrder($"Order was rejected for {Entry.SymbolName} as {dataEntry.Status} at {DateTime.UtcNow}");
                                            KillEntryOrCloseOrder();
                                            break;
                                        default:
                                            LogTrades.WriteLine($"dataEntry.Status was {dataEntry.Status}");
                                            break;
                                    }

                                    tradeStatusChanged = true;
                                    break;
                            }
                            break;
                        #endregion

                        #region Close order
                        case CLOSE_ORDER_SCRIPT:

                            entryTimeStamp = DateTime.UtcNow;
                            if (Close == null)
                            {
                                LogTrades.WriteLine($"No Close set in CLOSE_ORDER_SCRIPT for {symbolName}, cannot close trade, exiting OnExchangeDataReceived");
                                KillEntryOrCloseOrder(true);
                                return;
                            }


                            switch (Entry.ExchangeMethodIndex)
                            {
                                case 0: //get account balances

                                    LogTrades.WriteLine("Cancelling stops: fetching account data...");
                                    accountData = null;
                                    MainObj.exchangeConnector.AddRequest(ExchangeRequest.ACCOUNT, ExchangeDataReceivedEventHandler, null);
                                    break;

                                case 1: //cancel stops if they are still present, but if trade has closed, we are done

                                    LogTrades.WriteLine("Cancelling stops: account data fetched");

                                    //if the base asset balance is very small, assume the trade has exited on the exchange, so go no further

                                    (bool _noLockedAsset, bool _noFreeAsset) = IsTradeClosed();
                                    if (_noLockedAsset && _noFreeAsset) //i.e there is no BTC
                                    {
                                        LogTrades.WriteLine("No BTC, closing trade locally.");
                                        CloseTradeOnSystem_synchronised((decimal)currentDataItem.Close);
                                    }
                                    else
                                    {
                                        if (_noLockedAsset)
                                        {
                                            //if there is no locked balance, assume the stop has been removed from the exchange
                                            //in which case, we already have the balances so can move on to closing the trade
                                            Entry.ExchangeMethodIndex = 3; //because it will get incremented again to 4
                                        }
                                        else
                                        {
                                            if (Stop != null && Stop.ItemId != null)
                                            {
                                                LogTrades.WriteLine("Locked balance on exchange, cancelling stops...");

                                                _CancelStopTradeRequest(new ExchangeRequestData
                                                {
                                                    exchangeRequestType = ExchangeRequest.CANCEL_ORDER,
                                                    symbol = symbolName,
                                                    origClientOrderId = Stop.ItemId,
                                                });
                                            }
                                        }

                                    }

                                    break;
                                case 2://get account balances again

                                    LogTrades.WriteLine("Cancelling stops: fetching account data second time...");
                                    accountData = null;
                                    MainObj.exchangeConnector.AddRequest(ExchangeRequest.ACCOUNT, ExchangeDataReceivedEventHandler, null);
                                    break;

                                case 3:
                                    #region Generate Close order

                                    //a QUANTITY order specifies the amount of base asset to buy or sell, so can go directly from here

                                    LogTrades.WriteLine($"Closing order for {symbolName}...");
                                    var _baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                                    if (_baseAsset != null)
                                    {
                                        var coinBalance = accountData.Balances.Where(x => x.Asset == _baseAsset).Select(x => x.Free).FirstOrDefault();

                                        LogTrades.WriteLine($"Using received balance of {coinBalance} {_baseAsset}");

                                        //retain some BNB to reduce trading fees
                                        if (_baseAsset == "BNB" && RETAINED_BNB_PERCENTAGE > 0)
                                        {
                                            coinBalance *= (1 - RETAINED_BNB_PERCENTAGE / 100);
                                            LogTrades.WriteLine($"Retaining {RETAINED_BNB_PERCENTAGE}% of BNB");
                                        }

                                        (var amount1, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                                        if (amount1 > 0)
                                        {
                                            Close.Quantity = amount1;

                                            LogTrades.WriteLine($"Placing sell order of {coinBalance}, adjusted for step size, at {DateTime.Now}");
                                            SetUpdateRequest(Close);

                                            _RunExchangeTradeRequest(new ExchangeRequestData
                                            {
                                                tradeId = Close.Id,
                                                exchangeRequestType = ExchangeRequest.NEW_ORDER,
                                                symbol = symbolName,
                                                side = QueryParams.SELL,
                                                orderType = QueryParams.MARKET,
                                                quantity = amount1,
                                                tradeIdNew = Close.ItemId,
                                            });
                                        }
                                        else
                                        {
                                            LogTrades.WriteLine($"Quote asset balance was zero so could not close trade for {symbolName}"); //this can happen multiple times if unsyncronised with server
                                        }
                                    }

                                    #endregion
                                    break;

                                case 4:
                                    #region Set order on system

                                    LogTrades.WriteLine($"Received close balance at {DateTime.Now}");
                                    //if status is 'NEW' then order is accepted but not yet filled
                                    //status could be 'PARTIALLY_FILLED' (limit orders only??)
                                    var dataClose = result as NewOrderData;
                                    if (dataClose == null)
                                    {
                                        LogTrades.WriteLine($"Order data was null in CLOSE_ORDER_SCRIPT for {symbolName}, exiting OnExchangeDataReceived");
                                        KillEntryOrCloseOrder(true);
                                        return;
                                    }

                                    switch (dataClose.Status)
                                    {
                                        case "FILLED":

                                            LogTrades.WriteLineOrder($"Close order filled, closing trade on system at {DateTime.UtcNow}");
                                            CloseTradeOnSystem_synchronised(dataClose.Price);
                                            EndEntryOrCloseScript();
                                            LogTrades.WriteSpacer();


                                            break;

                                        case "CANCELED":
                                        case "REJECTED":
                                        case "EXPIRED":
                                            //can try again
                                            LogTrades.WriteLineOrder($"Trade was {dataClose.Status} in CLOSE_ORDER_SCRIPT for {Close.SymbolName} at {DateTime.UtcNow}");
                                            KillEntryOrCloseOrder(true);

                                            break;
                                    }

                                    tradeStatusChanged = true;

                                    ShowAccountBalances(); //will log balance
                                    #endregion
                                    break;
                            }
                            break;
                        #endregion

                        #region Update live state
                        case UPDATE_LIVE_STATE_SCRIPT:

                            var baseAsset = SymbolNames.GetBaseAssetForSymbol(Entry.SymbolName); //i.e. BTC
                            if (baseAsset == null) //this would never happen anyway
                            {
                                LogTrades.WriteLine("Could not update live state, base asset was null");
                                Entry.ExchangeMethodScript = null;
                                Entry.ExchangeMethodIndex = 0;
                                SetUpdateRequest(Entry);
                                break;
                            }

                            //if the base asset balance is very small, assume the trade has exited

                            //todo - get the correct minimum trade value for the asset and calculate an appropriate small value
                            (bool noLockedAsset, bool noFreeAsset) = IsTradeClosed();
                            if (noLockedAsset && noFreeAsset) //i.e there is no BTC
                            {
                                //this is not reliable so cannot kill the order here. It may be that the data lags the order slightly, or the stop removes the free
                                // balance but does not register a locked balance immediately. Either way, this is killing the order sometimes
                                LogTrades.WriteLine("Order balance is (close to) zero, probably stopped on exchange.");
                                (var lastValue, var _) = GetValueForSymbol(symbolName);
                                //CloseTradeOnSystem_synchronised((decimal)lastValue);

                                //also, mark as not synched (same as with Bitmex) in case this data is not returned correctly from the exchange
                                // NOTE - if this is a problem, just don't close the order here.

                                Entry.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                                SetUpdateRequest(Entry);
                            }

                            //if there is no locked balance, assume the stop has been removed from the exchange
                            else if (noLockedAsset)
                            {
                                if (Stop != null && Stop.ExchangeStatus == TradeExchangeStatus.ENTERED_ON_EXCHANGE && Stop.ExchangeMethodScript != EXCHANGE_NOT_SYNCHED)
                                {
                                    //var st = Stop;
                                    //RemoveTradeFromLine_UI_synchronised(st, false);
                                    //SetUpdateRequest(st, TradeUpdateRequest.DELETE);

                                    //stop may get removed from exchange but not replaced, in which case must not remove local stop as well!!
                                    Stop.ExchangeMethodScript = EXCHANGE_NOT_SYNCHED;
                                    SetUpdateRequest(Stop);
                                    LogTrades.WriteLine("No locked balance on exchange, assume stop cancelled, check exchange!!");
                                }
                            }
                            else
                            {
                                Entry.ExchangeMethodScript = null;
                                Entry.ExchangeMethodIndex = 0;
                                SetUpdateRequest(Entry);
                            }


                            break;
                            #endregion
                    }

                    if (Entry.ExchangeMethodScript != null) //if script ended, this will be null
                    {
                        if (Entry.ExchangeMethodScript != EXCHANGE_NOT_SYNCHED) //because a stop may update, and this is the final state which should not be changed
                        {
                            Entry.ExchangeMethodIndex++;
                            SetUpdateRequest(Entry);
                        }
                    }
                }

                if (Stop != null && Stop.ExchangeMethodScript != null)//  || (TempStop != null && TempStop.ExchangeMethodScript != null))
                {

                    switch (Stop.ExchangeMethodScript)
                    {
                        #region Set stop
                        case SET_STOP_SCRIPT:

                            //Stop will not be null here

                            //process is:
                            //1) remove any existing stop
                            //2) set a new stop to the current order value

                            //stop may not already be set. If it is, cancel first and wait for next repsonse
                            //if (string.IsNullOrEmpty(Stop.ItemId))
                            //{
                            //    Stop.ExchangeMethodIndex = 2;
                            //}

                            switch (Stop.ExchangeMethodIndex)
                            {
                                case 0: //remove existing stop

                                    if (!string.IsNullOrEmpty(Stop.ItemId))
                                    {
                                        _CancelStopTradeRequest(new ExchangeRequestData
                                        {
                                            exchangeRequestType = ExchangeRequest.CANCEL_ORDER,
                                            symbol = symbolName,
                                            origClientOrderId = Stop.ItemId
                                            //orderId = 28009071367 //makes no difference
                                        });

                                        //kill the stop ItemId - 
                                        // if the stops are cancelled on the exchange, but the confirmation is not received, as seems to happen quite often,
                                        // then the original ItemId will persist here and next time, an attempt to remove this stop from the exchange will fail
                                        // so the stop will never get put on the exchange.
                                        Stop.ItemId = null;
                                        SetUpdateRequest(Stop);
                                    }
                                    break;
                                case 1:
                                    accountData = null;
                                    MainObj.exchangeConnector.AddRequest(ExchangeRequest.ACCOUNT, ExchangeDataReceivedEventHandler, null);
                                    break;
                                case 2: //set new stop


                                    //don't want to pull the quote data each time a stop gets updated, so will just use the curent ticker price
                                    (var lastValue, var _) = GetValueForSymbol(symbolName);

                                    Stop.ItemId = Guid.NewGuid().ToString();
                                    SetUpdateRequest(Stop);
                                    var baseAsset = SymbolNames.GetBaseAssetForSymbol(symbolName);
                                    var coinBalance = accountData.Balances.Where(x => x.Asset == baseAsset).Select(x => x.Free).FirstOrDefault();

                                    (var amount, var stepSize) = GetCorrectPrecisionForTrade(symbolName, coinBalance);

                                    
                                    //An OCO has 2 legs called the above leg and below leg.
                                    //One of the legs must be a LIMIT_MAKER order and the other leg must be STOP_LOSS or STOP_LOSS_LIMIT order.
                                    //Price restrictions:
                                    //If the OCO is on the SELL side: LIMIT_MAKER price > Last Traded Price > stopPrice
                                    //If the OCO is on the BUY side: LIMIT_MAKER price < Last Traded Price < stopPrice
                                    

            //this is calling the rest.js method correctly, so the error is within this data
            //(-1104 "not all sent parameters were read")
            //(-1106 "a parameter was sent when not required")
            //(-1102 "A mandatory parameter was not sent, was empty/null, or malformed")
            //(-1128 "Combination of optional parameters invalid.")
            //(-2011 "Unknown order sent")
            //(-1013 - this was "Stop loss orders are not supported for this symbol." but is not documented as an error code (only 1014 and beyond)

            //can swap prices around, still get the same -1013 error
            //"Stop loss orders are not supported for this symbol."


                                    float abovePrice = 0;
                                    if (TakeProfit != null)
                                    {
                                        abovePrice = (float)TakeProfit.Price;
                                    }
                                    else
                                    {
                                        //must have an initial t/p for the OCO. In case the auto-set ceiling has been removed for some reason, make the calculation here
                                        var initialTP = 0.1; //10%
                                        abovePrice = (float)(
                                            Entry.Position == TradePosition.LONG
                                                ? lastValue * (1 + initialTP)
                                                : lastValue * (1 - initialTP)
                                        ); //same calculation as would be used for ceiling
                                    }
                                    float triggerPrice = (float)Stop.Price;

                                    //this is just to ensure the stop executes immediately, not as a limit order
                                    //but if the belowPrice is too low (like half the trigger price) a small order (test order) may not have a large enough notional value
                                    float belowPrice = (float)(triggerPrice * 0.9);
                                    //var symbol = exchangeInfo.Symbols.FirstOrDefault(x => x.Symbol == symbolName);
                                    //var minSize = symbol.Filters.Where(x => x.FilterType == "NOTIONAL").FirstOrDefault();
                                   
  
                                    
                                    MainObj.exchangeConnector.AddRequest(
                                                ExchangeRequest.NEW_OCO,
                                                ExchangeDataReceivedEventHandler,
                                                (QueryParams.SYMBOL, Stop.SymbolName),
                                                (QueryParams.SIDE, QueryParams.SELL),
                                                (QueryParams.QUANTITY, coinBalance), //generates -1013 for btc or usdt value - minimum trade value is 0.0001 BTC
                                                (QueryParams.ABOVE_TYPE, QueryParams.LIMIT_MAKER),
                                                //(ApiParamNames.ABOVE_STOP_PRICE, 70000), //so presumably this would be for a buy, not a sell
                                                //(ApiParamNames.ABOVE_TIMEINFORCE, 1000),
                                                (QueryParams.ABOVE_PRICE, abovePrice), //generates -1102 if missing
                                                (QueryParams.BELOW_TYPE, QueryParams.STOP_LOSS_LIMIT),
                                                (QueryParams.BELOW_STOP_PRICE, triggerPrice), //generates -1128 if missing
                                                (QueryParams.BELOW_PRICE, belowPrice), //this is needed also
                                                (QueryParams.BELOW_TIMEINFORCE, "GTC")
                                                //(ApiParamNames.TRADE_ID_NEW, Stop.ItemId) //generates -1104 if sent
                                                //(ApiParamNames.RESPONSE_TYPE, ApiParamNames.FULL)
                                                //(ApiParamNames.TIME_IN_FORCE,   "GTC") - default anyway
                                                );

                                    break;

                                case 3:
                                    var dataEntry = result as NewOCOData;
                                    if (result != null)
                                    {
                                        var id = dataEntry.OrderReports
                                            .Where(x => x.Type == "STOP_LOSS_LIMIT")
                                            .Select(x => x.ClientOrderId)
                                            .FirstOrDefault(); //if this is set, then this stop can be updated next time. Not sure if you can also use the list id instead of this
                                        Stop.ItemId = id;
                                        Stop.ExchangeMethodScript = null;
                                        Stop.ExchangeMethodIndex = 0;
                                        Stop.ExchangeStatus = TradeExchangeStatus.ENTERED_ON_EXCHANGE;
                                        SetUpdateRequest(Stop);

                                        if (isManualSynch)
                                        {
                                            LogTrades.WriteLine("OCO synchronised");
                                            isManualSynch = false;
                                        }
                                    }
                                    break;
                            }


                            tradeStatusChanged = true;
                            break;
                        #endregion

                        #region Cancel stop
                        case CANCEL_STOP_SCRIPT:

                            switch (Stop.ExchangeMethodIndex)
                            {
                                case 0:
                                    _CancelStopTradeRequest(new ExchangeRequestData
                                    {
                                        exchangeRequestType = ExchangeRequest.CANCEL_ORDER,
                                        symbol = symbolName,
                                        origClientOrderId = Stop.ItemId// TempStop.ItemId
                                    });
                                    break;

                                case 1:
                                    RemoveTradeFromLine_UI_synchronised(Stop);
                                    SetUpdateRequest(Stop, TradeUpdateRequest.DELETE);
                                    break;
                            }
                            break;
                            #endregion
                    }

                    if (Stop != null && Stop.ExchangeMethodScript != null) //if script ended, this will be null
                    {
                        Stop.ExchangeMethodIndex++;
                        SetUpdateRequest(Stop);
                    }
                }
            });

            MainObj.DrawCurrentDispatcher();
        }
        #endregion
        */
        #endregion
        #region Enqueue and dequeue orders (unused)
        //no idea why this is here, it seems pointless so has been abandoned (but not tested like this)

        /*
        static List<PendingTradeRequest> pendingTradeRequests = new List<PendingTradeRequest>();
        internal override void EnqueueOrder_synchronised(TradeAction tradeAction)
        {
            if (!pendingTradeRequests.Any(x => x.tradeEngine == tradeEngine))
            {
                pendingTradeRequests.Add(new PendingTradeRequest { tradeEngine = this, timeStamp = DateTime.UtcNow, action = tradeAction });
                _RunNextOrder();
            }
        }

        internal override void DequeueOrder_synchronised()
        {
            pendingTradeRequests.RemoveAll((PendingTradeRequest pendingRequest) => { return pendingRequest.tradeEngine == this; });
            _RunNextOrder();
        }

        void _RunNextOrder()
        {
            //probably not important, but look for requests by earliest first
            PendingTradeRequest pendingRequest = pendingTradeRequests.OrderBy(x => x.timeStamp).FirstOrDefault(x => x.inProgress == false && x.action == TradeAction.CLOSE);
            if (pendingRequest == null)
            {
                pendingRequest = pendingTradeRequests.OrderBy(x => x.timeStamp).FirstOrDefault(x => x.inProgress == false && x.action == TradeAction.ENTRY);
            }
            if (pendingRequest != null)
            {
                pendingRequest.inProgress = true;
                pendingRequest.tradeEngine.StartEntryOrClose();
            }
        }

        protected override void Binance_CheckForPendingTradeRequestTimeout() 
        {
            if (pendingTradeRequests.Any())
            {
                var p = pendingTradeRequests.First();
                //using a 25 second timeout, enough for a couple of trades to timeout
                var timeOut = USE_SIMULATED_EXCHANGE ? 1000 : 25;
                bool t = DateTime.UtcNow.Subtract(p.timeStamp).TotalSeconds > timeOut;
                if (t)
                {
                    LogTrades.WriteLine($"Pending trade timeout for {p.tradeEngine.symbolName}, at {DateTime.UtcNow:G}");
                    pendingTradeRequests.Remove(p);
                }
            }
        }

        protected override void Binance_RemovePendingTradeRequest()
        {
            var pendingRequest = pendingTradeRequests.FirstOrDefault(x => x.tradeEngine == this);
            if (pendingRequest != null)
            {
                //this can happen when a trade cannot fill, ends up interrupted, then the pending request remains on the system
                pendingTradeRequests.Remove(pendingRequest);
            }
        }
        */
        #endregion

    }
}
