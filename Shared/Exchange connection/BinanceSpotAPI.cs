using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{
    public class BinanceSpotAPI : BinanceAPI
    {

        public BinanceSpotAPI(string key = null,
            string secret = null,
            long? recvWindow = null, //number of milliseconds after timestamp the request is valid for, defaults to 5000
            int timeoutMs = 15000,
            bool handleDrift = false,
            string baseUrl = "https://api.binance.com/",
            HttpMessageHandler handler = null) : base(key, secret, recvWindow, timeoutMs, handleDrift, baseUrl, handler) { }

        // --- Public endpoints

        #region General
        public Task<JsonNode> ExchangeInfoAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/exchangeInfo", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> ExecutionRulesAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/executionRules", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> PingAsync(CancellationToken cancellationToken = default) =>
            MakeRequestAsync(new Dictionary<string, string>(), "api/v1/ping", null, HttpMethod.Get, 0, cancellationToken);

        public Task<JsonNode> KlinesAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(query, "api/v1/klines", null, HttpMethod.Get, 0, cancellationToken);

        //Latest price for a symbol or symbols.
        public Task<JsonNode> TickerPriceAsync(string symbol = null, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(string.IsNullOrEmpty(symbol) ? [] : new Dictionary<string, string> { ["symbol"] = symbol }, "api/v3/ticker/price", null, HttpMethod.Get, 0, cancellationToken);

        //Best price/qty on the order book for a symbol or symbols.
        public Task<JsonNode> BookTickerAsync(string symbol = null, CancellationToken cancellationToken = default) =>
            MakeRequestAsync(string.IsNullOrEmpty(symbol) ? [] : new Dictionary<string, string> { ["symbol"] = symbol }, "api/v3/ticker/bookTicker", null, HttpMethod.Get, 0, cancellationToken);
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

        #region Trade
        public virtual Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/order", "SIGNED", HttpMethod.Post, 0, cancellationToken);
        }
        //cancel all open orders
        public virtual Task<JsonNode> CancelOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            var query = new Dictionary<string, string> { ["symbol"] = symbol };
            SetTimestamp(query);
            return MakeRequestAsync(query, "api/v3/openOrders", "SIGNED", HttpMethod.Delete, 0, cancellationToken);
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

    }
}
