using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Http;//Backtest requires this

namespace AlgoTrader.Shared
{
    public class BinanceAPI : IDisposable
    {
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

        #region Shared endpoints

        public Task<JsonNode> ExchangeInfoAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/exchangeInfo", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> TickerPriceAsync(string symbol = null, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(string.IsNullOrEmpty(symbol) ? [] : new Dictionary<string, string> { ["symbol"] = symbol }, "api/v3/ticker/price", null, HttpMethod.Get, 0, cancellationToken);

        #endregion

        #region Constructor
        public BinanceAPI(
            string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "",
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

        protected void SetTimestamp(IDictionary<string, string> query)
        { 
            if (!query.ContainsKey("timestamp") || string.IsNullOrEmpty(query["timestamp"]))
            {
                query["timestamp"] = (GetTimeMillis() + _drift).ToString();
            }
        }

        protected async Task<JsonNode> MakeRequestAsync(
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

        public Task<JsonNode> TimeAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/time", null, HttpMethod.Get, 0, cancellationToken);



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

        public void Dispose()
        {
            _http.Dispose();
            _syncTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}