using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{
    public class BinanceAPI : IDisposable
    {

        #region Strings
        public const string TRUE = "true";
        public const string FALSE = "false";
        public const string SYMBOL = "symbol";
        public const string BTCUSDT = "BTCUSDT";
        public const string CLIENT_ORDER_ID_ORIGINAL = "origClientOrderId";
        public const string CLIENT_ORDER_ID_NEW = "newClientOrderId";
        public const string LIST_CLIENT_ORDER_ID = "listClientOrderId";
        public const string EXCHANGE_ORDER_ID = "orderId";
        public const string SIDE = "side";
        public const string ORDER_TYPE = "type"; //LIMIT, MARKET, STOP_LOSS, STOP_LOSS_LIMIT, TAKE_PROFIT, TAKE_PROFIT_LIMIT, LIMIT_MAKER
        public const string QUOTE_ORDER_QTY = "quoteOrderQty";
        public const string QUANTITY = "quantity";
        public const string ORIG_QUANTITY = "origQty";
        public const string ICEBERG_QUANTITY = "icebergQty";
        public const string PRICE = "price";
        public const string STOP_PRICE = "stopPrice";
        public const string RESPONSE_TYPE = "newOrderRespType";
        public const string TIME_IN_FORCE = "timeInForce";
        public const string GOOD_TILL_CANCELLED = "GTC";
        public const string FILL_OR_KILL = "FOK";
        public const string IMMEDIATE_OR_CANCEL = "IOC";
        public const string TIMEOUT_MS = "recvWindow"; //this defaults to 5000ms. It is recommended to use this value or less, cannot go beyond 60000
        public const string ORIG_CLIENT_ORDER_ID = "origClientOrderId";
        public const string BUY = "BUY";
        public const string SELL = "SELL";
        public const string LIMIT = "LIMIT";
        public const string MARKET = "MARKET";
        public const string STOP_LOSS = "STOP_LOSS";
        public const string STOP_LOSS_LIMIT = "STOP_LOSS_LIMIT";
        public const string LIMIT_MAKER = "LIMIT_MAKER";
        public const string TAKE_PROFIT = "TAKE_PROFIT";
        public const string TAKE_PROFIT_LIMIT = "TAKE_PROFIT_LIMIT";
        public const string ABOVE_TYPE = "aboveType"; //STOP_LOSS_LIMIT, STOP_LOSS, LIMIT_MAKER - limit maker should trigger the actual stop loss limit, presumable at the above stop price
        public const string ABOVE_PRICE = "abovePrice";
        public const string ABOVE_STOP_PRICE = "aboveStopPrice";
        public const string ABOVE_CLIENT_ORDER_ID = "aboveClientOrderId";
        public const string ABOVE_TIMEINFORCE = "aboveTimeInForce";
        public const string BELOW_TYPE = "belowType";
        public const string BELOW_PRICE = "belowPrice";
        public const string BELOW_STOP_PRICE = "belowStopPrice";
        public const string BELOW_TIMEINFORCE = "belowTimeInForce";
        public const string BELOW_CLIENT_ORDER_ID = "belowClientOrderId";
        public const string ACK = "ACK";
        public const string RESULT = "RESULT";
        public const string FULL = "FULL";
        public const string CANCEL_REPLACE_MODE = "cancelReplaceMode";
        public const string ALLOW_FAILURE = "ALLOW_FAILURE";
        public const string STOP_ON_FAILURE = "STOP_ON_FAILURE";

        //margin specific
        public const string IS_ISOLATED = "isIsolated";
        public const string AUTO_REPAY_AT_CANCEL = "autoRepayAtCancel";
        public const string SIDE_EFFECT_TYPE = "sideEffectType";
        public const string NO_SIDE_EFFECT = "NO_SIDE_EFFECT";
        public const string MARGIN_BUY = "MARGIN_BUY";
        public const string AUTO_REPAY = "AUTO_REPAY";
        public const string AUTO_BORROW_REPAY = "AUTO_BORROW_REPAY"; //this will repay any liabilitites as well as borrow to buy or sell.

        #endregion

        #region Enums
        public enum OrderTypes
        {
            [Display(Name = "LIMIT")]
            LIMIT,
            [Display(Name = "MARKET")]
            MARKET,
            [Display(Name = "STOP_LOSS")]
            STOP_LOSS,
            [Display(Name = "STOP_LOSS_LIMIT")]
            STOP_LOSS_LIMIT,
            [Display(Name = "TAKE_PROFIT")]
            TAKE_PROFIT,
            [Display(Name = "TAKE_PROFIT_LIMIT")]
            TAKE_PROFIT_LIMIT,
            [Display(Name = "LIMIT_MAKER")]
            LIMIT_MAKER
        }

        public enum Side {
            BUY,
            SELL
        }
        #endregion

        #region Fields
        private readonly string _key;
        private readonly string _secret;
        private readonly long? _recvWindow;
        private readonly TimeSpan _timeout;
        private readonly bool _handleDrift;
        private readonly HttpClient _http;
        private string _baseUrl;
        public long _drift;
        private System.Threading.Timer _syncTimer;
        #endregion

        #region Constructor
        public BinanceAPI(
            string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "https://api.binance.com/",
            HttpMessageHandler handler = null)
        {
            _key = key ?? string.Empty;
            _secret = secret ?? string.Empty;
            _recvWindow = recvWindow;
            _timeout = TimeSpan.FromMilliseconds(timeoutMs);
            _handleDrift = handleDrift;
            _baseUrl = baseUrl;
            if (!_baseUrl.EndsWith("/")) _baseUrl += "/";
            _http = handler is null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = _timeout;
            _drift = 0;

            Task.Run(async () =>
            {
                if (_handleDrift)
                {
                    await StartTimeSyncAsync().ConfigureAwait(false);
                }
            });
        }

        #endregion

        #region Process requests
        public string GetBaseUrl() => _baseUrl;

        private static string BuildQueryString(IDictionary<string, string> query)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var kv in query)
            {
                if (kv.Value is null) continue;
                if (!first) sb.Append('&');
                first = false;
                sb.Append(WebUtility.UrlEncode(kv.Key));
                sb.Append('=');
                sb.Append(WebUtility.UrlEncode(kv.Value));
            }
            return sb.ToString();
        }

        private string Sign(string payload)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_secret);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private long GetTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void SetTimestamp(IDictionary<string, string> query)
        { 
            if (!query.ContainsKey("timestamp") || string.IsNullOrEmpty(query["timestamp"]))
            {
                query["timestamp"] = (GetTimeMillis() + _drift).ToString();
            }
        }

        private async Task<JsonNode> MakeRequestAsync(
            IDictionary<string, string> query,
            string route,
            string security = null,
            HttpMethod method = null,
            int attempt = 0,
            CancellationToken cancellationToken = default)
        {

            // copy query to avoid modifying caller's collection
            var q = new Dictionary<string, string>(query);

            var type = route.TrimEnd('/').Split('/')[^1];
            var url = new StringBuilder();
            url.Append(_baseUrl).Append(route);

            if (security == "SIGNED")
            {
                if (_recvWindow.HasValue)
                {
                    q["recvWindow"] = _recvWindow.Value.ToString();
                }

                var qs = BuildQueryString(q);
                url.Append('?').Append(qs);

                if (!url.ToString().EndsWith("?"))
                {
                    url.Append('&');
                }

                url.Append("signature=").Append(Sign(qs));
            }
            else
            {
                var qs = BuildQueryString(q);
                if (!string.IsNullOrEmpty(qs))
                {
                    url.Append('?').Append(qs);
                }
            }

            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url.ToString());
            if (security == "API-KEY" || security == "SIGNED")
            {
                request.Headers.Add("X-MBX-APIKEY", _key);
            }

            HttpResponseMessage response;
            string content = string.Empty;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // network-level error
                throw new HttpRequestException("Request failed", ex);
            }

            JsonNode? payload = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    payload = JsonNode.Parse(content);
                }
            }
            catch
            {
                // keep payload null; raw content not valid json
            }

            if (!response.IsSuccessStatusCode)
            {
                // Try to detect timestamp drift error from Binance (code -1021)
                var codeNode = payload?["code"];
                int code = 0;
                if (codeNode != null && int.TryParse(codeNode.ToString(), out var parsed)) code = parsed;

                if ((int)response.StatusCode == 400 && code == -1021 && _handleDrift && attempt == 0)
                {
                    await CalculateDriftAsync(cancellationToken).ConfigureAwait(false);
                    // update timestamp and retry once
                    q["timestamp"] = (GetTimeMillis() + _drift).ToString();
                    return await MakeRequestAsync(q, route, security, method, attempt + 1, cancellationToken).ConfigureAwait(false);
                }

                throw new InvalidOperationException($"Response code {(int)response.StatusCode}: {content}");
            }

            if (payload is not null)
            {
                return payload;
                //this does not work, you can't build a JsonNode like this, it will throw an exception
                // if array, beautify each element; otherwise beautify root
                //if (payload is JsonArray arr)
                //{
                //    var outArray = new JsonArray();
                //    foreach (var item in arr)
                //    {
                //        outArray.Add(_disableBeautification ? item : _beautifier.Beautify(item!, type));
                //    }
                //    return outArray;
                //}
                //else
                //{
                //    return _disableBeautification ? payload : _beautifier.Beautify(payload, type);
                //}
            }

            // no JSON body, return null
            return null;
        }
        #endregion
  
        #region Drift / time sync
        public async Task CalculateDriftAsync(CancellationToken cancellationToken = default)
        {
            var systemTime = GetTimeMillis();
            var response = await TimeAsync(cancellationToken).ConfigureAwait(false);
            var serverTimeNode = response?["serverTime"];
            if (serverTimeNode is null)
            {
                // cannot calculate drift if serverTime not present
                return;
            }
            if (!long.TryParse(serverTimeNode.ToString(), out var serverTime)) return;

            var transitTime = (GetTimeMillis() - systemTime) / 2;
            _drift = serverTime - (systemTime + transitTime);
        }


        public Task StartTimeSyncAsync(int intervalMs = 300_000, Action<Task>? onRecalculateDriftCb = null)
        {
            // If timer already running, stop and reset
            if (_syncTimer != null)
            {
                EndTimeSync();
                return Task.CompletedTask;
            }

            // calculate initial drift
            var t = CalculateDriftAsync();
            t.ConfigureAwait(false);

            // schedule periodic sync
            _syncTimer = new System.Threading.Timer(async _ =>
            {
                var promise = CalculateDriftAsync();
                onRecalculateDriftCb?.Invoke(promise);
                try { 
                    await promise.ConfigureAwait(false); 
                    Debug.WriteLine($"[BinanceRest] Time sync completed. Current drift: {_drift} ms");

                } catch { /* swallow */ }
            }, null, intervalMs, intervalMs);

            return Task.CompletedTask;
        }

        public void EndTimeSync()
        {
            _syncTimer?.Dispose();
            _syncTimer = null;
            _drift = 0;
        }
        #endregion

        // --- Public endpoints

        #region General
        public Task<JsonNode> ExchangeInfoAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/exchangeInfo", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> ExecutionRulesAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/executionRules", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> PingAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/ping", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> TimeAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/time", null, HttpMethod.Get, 0, cancellationToken);


        public Task<JsonNode> KlinesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/klines", null, HttpMethod.Get, 0, cancellationToken);

        //Latest price for a symbol or symbols.
        public Task<JsonNode> TickerPriceAsync(string symbol = null, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(string.IsNullOrEmpty(symbol) ? new Dictionary<string, string>() : new Dictionary<string, string> { ["symbol"] = symbol }, "api/v3/ticker/price", null, HttpMethod.Get, 0, cancellationToken);

        //Best price/qty on the order book for a symbol or symbols.
        public Task<JsonNode> BookTickerAsync(string symbol = null, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(string.IsNullOrEmpty(symbol) ? new Dictionary<string, string>() : new Dictionary<string, string> { ["symbol"] = symbol }, "api/v3/ticker/bookTicker", null, HttpMethod.Get, 0, cancellationToken);
        #endregion

        #region not needed
        public Task<JsonNode> OrderBookAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/depth", null, HttpMethod.Get, 0, cancellationToken);

        //returns list of recent trades, up to 1000 (not signed so everyone's trades)
        public Task<JsonNode> TradesAsync(string symbol, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string> { ["symbol"] = symbol }, "api/v1/trades", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> TradesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/trades", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> AggTradesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/aggTrades", null, HttpMethod.Get, 0, cancellationToken);

        //24 hour rolling window price change statistics. Careful when accessing this with no symbol.
        public Task<JsonNode> Ticker24hrAsync(string symbol, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string> { ["symbol"] = symbol }, "api/v1/ticker/24hr", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> Ticker24hrAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/ticker/24hr", null, HttpMethod.Get, 0, cancellationToken);
       
        public Task<JsonNode> HistoricalTradesAsync(string symbol, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string> { ["symbol"] = symbol }, "api/v1/historicalTrades", "API-KEY", HttpMethod.Get, 0, cancellationToken);
        
        public Task<JsonNode> HistoricalTradesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/historicalTrades", "API-KEY", HttpMethod.Get, 0, cancellationToken);


        #endregion

        #region not documented possibly deprecated


        //public Task<JsonNode> AllBookTickersAsync(CancellationToken cancellationToken = default) =>
        //    MakeRequestAsync(new Dictionary<string, string>(), "api/v1/ticker/allBookTickers", null, HttpMethod.Get, 0, cancellationToken);

        //public Task<JsonNode> AllPricesAsync(CancellationToken cancellationToken = default) =>
        //    MakeRequestAsync(new Dictionary<string, string>(), "api/v1/ticker/allPrices", null, HttpMethod.Get, 0, cancellationToken);

        //public Task<JsonNode> DepthAsync(string symbol, CancellationToken cancellationToken = default) =>
        //    MakeRequestAsync(new Dictionary<string, string> { ["symbol"] = symbol }, "api/v1/depth", null, HttpMethod.Get, 0, cancellationToken);


        #endregion


        // --- Account / Signed endpoints ---

        #region Margin
        public Task<JsonNode> EnableIsolateMarginAsync(string symbol, CancellationToken cancellationToken = default)
        {
           var query = new Dictionary<string, string> { ["symbol"] = symbol };
           SetTimestamp(query);
           return MakeRequestAsync(query, "sapi/v1/margin/isolated/account", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> QueryIsolateMarginAccountInfoAsync(CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> {};
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolated/account", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> QueryIsolateMarginAccountFeeAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolatedMarginData", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> QueryMaxBorrowAsync(Dictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/maxBorrowable", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> SetMarginBorrowRepay(Dictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/borrow-repay", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> AdjustCrossMarginMaxLeveradgeAsync(Dictionary<string, string> query, CancellationToken cancellationToken = default)
        {     
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/max-leverage", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> NewMarginOrderAsync(Dictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }

        #region not much use
        public Task<JsonNode> GetSummaryOfMarginAccountAsync(CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/tradeCoeff", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> QueryEnabledIsolatedMarginAccountLimitAsync(CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolated/accountLimit", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        #endregion

        #endregion

        #region Trade
        public Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> CancelOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }

        public Task<JsonNode> CancelReplaceOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order/cancelReplace", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }

        public Task<JsonNode> QueryOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> NewOcoAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/orderList/oco", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }

        public Task<JsonNode> CancelOrderListAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/orderList", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }
        public Task<JsonNode> TestOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order/test", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }        
 
        public Task<JsonNode> OpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> OpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        //cancel all open orders
        public Task<JsonNode> CancelOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }

        public Task<JsonNode> AllOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/allOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> AllOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/allOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        #endregion

        #region Account
        public Task<JsonNode> AccountAsync(CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string>();
            SetTimestamp(q);
            return MakeRequestAsync(q, "api/v3/account", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> AccountAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/account", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> MyTradesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(q);
            return MakeRequestAsync(q, "api/v3/myTrades", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        //get trades for a specific symbol
        public Task<JsonNode> MyTradesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/myTrades", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        #endregion

        #region deposit and withdrawal
        public Task<JsonNode> WithdrawAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "wapi/v3/withdraw.html", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }

        public Task<JsonNode> DepositHistoryAsync(string asset, CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string> { ["asset"] = asset };
            SetTimestamp(q);
            return MakeRequestAsync(q, "wapi/v3/depositHistory.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> DepositHistoryAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "wapi/v3/depositHistory.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> WithdrawHistoryAsync(string asset, CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string> { ["asset"] = asset };
            SetTimestamp(q);
            return MakeRequestAsync(q, "wapi/v3/withdrawHistory.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> WithdrawHistoryAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "wapi/v3/withdrawHistory.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> DepositAddressAsync(string asset, CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string> { ["asset"] = asset };
            SetTimestamp(q);
            return MakeRequestAsync(q, "wapi/v3/depositAddress.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> DepositAddressAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "wapi/v3/depositAddress.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        public Task<JsonNode> AccountStatusAsync(CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string>();
            SetTimestamp(q);
            return MakeRequestAsync(q, "wapi/v3/accountStatus.html", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        #endregion

        #region User data streams
        //these create websockets which then pass account updates back to the user in real-time, for order executions, balance changes etc.
        //this could be useful for trapping stop executions
        public Task<JsonNode> StartUserDataStreamAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/userDataStream", "API-KEY", HttpMethod.Post, 0, cancellationToken);

        public Task<JsonNode> KeepAliveUserDataStreamAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/userDataStream", "API-KEY", HttpMethod.Put, 0, cancellationToken);

        public Task<JsonNode> CloseUserDataStreamAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/userDataStream", "API-KEY", HttpMethod.Delete, 0, cancellationToken);

        #endregion



        public void Dispose()
        {
            _http.Dispose();
            _syncTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}