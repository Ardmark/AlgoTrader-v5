using System.Text.Json.Nodes;
using System.Windows.Input;

namespace AlgoTrader.Shared.Interfaces
{
    /*
     * This interface allows for instantiation of either spot or margin API depending on Constants settings
     * Otherwise because the binanceAPI member is strongly typed you can't do that, it must be either BinanceSpotAPI or BinanceMarginAPI
     * You need to implement every method from either class in both classes, and put them all here as well.
     * */
    public interface IBinanceAPI
    {
        //public Task<JsonNode> MyTradesAsync(string symbol, CancellationToken cancellationToken = default);
        public Task<JsonNode> AccountAsync(CancellationToken cancellationToken = default);
        public Task<JsonNode> EnableIsolateMarginAsync(string symbol, CancellationToken cancellationToken = default);
        public Task<JsonNode> QueryIsolateMarginAccountInfoAsync(CancellationToken cancellationToken = default);
        public Task<JsonNode> QueryMaxBorrowAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> NewOrderAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> CancelOpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> NewOcoAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> OpenOrdersAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> CancelOrderListAsync(IDictionary<string, string> query, CancellationToken cancellationToken = default);
        public Task<JsonNode> ExchangeInfoAsync(CancellationToken cancellationToken = default);
        public Task<JsonNode> TickerPriceAsync(string symbol = null, CancellationToken cancellationToken = default);
    }
}
