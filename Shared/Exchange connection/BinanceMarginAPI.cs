using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{
    public class BinanceMarginAPI : BinanceSpotAPI
    {
        public BinanceMarginAPI(string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "https://api.binance.com/",
            HttpMessageHandler handler = null) : base(key, secret, recvWindow, timeoutMs, handleDrift, baseUrl, handler) { }


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
        public Task<JsonNode> QueryIsolateMarginAccountFeeAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/isolatedMarginData", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> QueryMaxBorrowAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/maxBorrowable", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }
        public Task<JsonNode> SetMarginBorrowRepay(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/borrow-repay", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        public Task<JsonNode> AdjustCrossMarginMaxLeveradgeAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/max-leverage", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }

        #region Overridden methods
        public override Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        
        public override Task<JsonNode> CancelOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "sapi/v1/margin/openOrders", "SIGNED", HttpMethod.Get, 0, cancellationToken);
        }

        #endregion

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



    }
}
