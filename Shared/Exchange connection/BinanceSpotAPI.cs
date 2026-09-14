using AlgoTrader.Shared.Interfaces;//Backtest requires this
using System.Net.Http;
using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{
    public class BinanceSpotAPI : BinanceAPI, IBinanceAPI
    {
        public BinanceSpotAPI(string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "",
            HttpMessageHandler handler = null) : base(key, secret, recvWindow, timeoutMs, handleDrift, baseUrl, handler) { }

        #region Endpoints
        public Task<JsonNode> AccountAsync(CancellationToken cancellationToken = default)
        {
            var q = new Dictionary<string, string>();
            SetTimestamp(q);
            return MakeRequestAsync(q, "api/v3/account", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> CancelOpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }
        public Task<JsonNode> NewOcoAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/orderList/oco", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> OpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> CancelOrderListAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/orderList", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
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

        #region Interface implementations
        public Task<JsonNode> EnableIsolateMarginAsync(string symbol, CancellationToken cancellationToken = default) { return null; }
        public Task<JsonNode> QueryIsolateMarginAccountInfoAsync(CancellationToken cancellationToken = default) { return null; }
        public Task<JsonNode> QueryMaxBorrowAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) { return null; }
        #endregion
    }
}
