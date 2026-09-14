using System.Text.Json.Nodes;
using System.Net.Http;
using AlgoTrader.Shared.Interfaces;//Backtest requires this

namespace AlgoTrader.Shared
{
    public class BinanceMarginAPI : BinanceAPI, IBinanceAPI
    {
        public BinanceMarginAPI(string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "",
            System.Net.Http.HttpMessageHandler handler = null) : base(key, secret, recvWindow, timeoutMs, handleDrift, baseUrl, handler) { }

        #region Endpoints
        public Task<JsonNode> EnableIsolateMarginAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolated/account", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> QueryIsolateMarginAccountInfoAsync(CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolated/account", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> QueryMaxBorrowAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/maxBorrowable", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> CancelOpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/openOrders", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }
        public Task<JsonNode> NewOcoAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/order/oco", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> OpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/openOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> CancelOrderListAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/orderList", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
        }
        #endregion

        #region Interface implementations
        public Task<JsonNode> AccountAsync(CancellationToken cancellationToken = default) { return null; }
        #endregion
    }
}
