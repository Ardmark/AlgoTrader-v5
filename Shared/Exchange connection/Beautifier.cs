using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlgoTrader.Shared
{
    public class Beautifier
    {
        private readonly Dictionary<string, object> _beautifications;

        public Beautifier()
        {
            _beautifications = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["aggTrades"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["a"] = "aggTradeId",
                    ["p"] = "price",
                    ["q"] = "quantity",
                    ["f"] = "firstTradeId",
                    ["l"] = "lastTradeId",
                    ["T"] = "timestamp",
                    ["m"] = "maker",
                    ["M"] = "bestPriceMatch"
                },

                ["klines"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["0"] = "openTime",
                    ["1"] = "open",
                    ["2"] = "high",
                    ["3"] = "low",
                    ["4"] = "close",
                    ["5"] = "volume",
                    ["6"] = "closeTime",
                    ["7"] = "quoteAssetVolume",
                    ["8"] = "trades",
                    ["9"] = "takerBaseAssetVolume",
                    ["10"] = "takerQuoteAssetVolume",
                    ["11"] = "ignored"
                },

                ["bids"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["0"] = "price",
                        ["1"] = "quantity",
                        ["2"] = "ignored"
                    }
                },

                ["asks"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["0"] = "price",
                        ["1"] = "quantity",
                        ["2"] = "ignored"
                    }
                },

                ["depthUpdateEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["U"] = "firstUpdateId",
                    ["u"] = "lastUpdateId",
                    ["b"] = "bidDepthDelta",
                    ["a"] = "askDepthDelta"
                },

                ["bidDepthDelta"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["0"] = "price",
                        ["1"] = "quantity",
                        ["2"] = "ignored"
                    }
                },

                ["askDepthDelta"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["0"] = "price",
                        ["1"] = "quantity",
                        ["2"] = "ignored"
                    }
                },

                ["klineEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["k"] = "kline"
                },

                ["kline"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["t"] = "startTime",
                    ["T"] = "endTime",
                    ["s"] = "symbol",
                    ["i"] = "interval",
                    ["f"] = "firstTradeId",
                    ["L"] = "lastTradeId",
                    ["o"] = "open",
                    ["c"] = "close",
                    ["h"] = "high",
                    ["l"] = "low",
                    ["v"] = "volume",
                    ["n"] = "trades",
                    ["x"] = "final",
                    ["q"] = "quoteVolume",
                    ["V"] = "volumeActive",
                    ["Q"] = "quoteVolumeActive",
                    ["B"] = "ignored"
                },

                ["aggTradeEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["a"] = "tradeId",
                    ["p"] = "price",
                    ["q"] = "quantity",
                    ["f"] = "firstTradeId",
                    ["l"] = "lastTradeId",
                    ["T"] = "time",
                    ["m"] = "maker",
                    ["M"] = "ignored"
                },

                ["outboundAccountInfoEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["m"] = "makerCommission",
                    ["t"] = "takerCommission",
                    ["b"] = "buyerCommission",
                    ["s"] = "sellerCommission",
                    ["T"] = "canTrade",
                    ["W"] = "canWithdraw",
                    ["D"] = "canDeposit",
                    ["B"] = "balances",
                    ["u"] = "lastUpdateTime"
                },

                ["balances"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["a"] = "asset",
                        ["f"] = "availableBalance",
                        ["l"] = "onOrderBalance"
                    }
                },

                ["executionReportEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["c"] = "newClientOrderId",
                    ["S"] = "side",
                    ["o"] = "orderType",
                    ["f"] = "cancelType",
                    ["q"] = "quantity",
                    ["p"] = "price",
                    ["P"] = "stopPrice",
                    ["F"] = "icebergQuantity",
                    ["C"] = "originalClientOrderId",
                    ["x"] = "executionType",
                    ["X"] = "orderStatus",
                    ["r"] = "rejectReason",
                    ["i"] = "orderId",
                    ["l"] = "lastTradeQuantity",
                    ["z"] = "accumulatedQuantity",
                    ["L"] = "lastTradePrice",
                    ["n"] = "commission",
                    ["N"] = "commissionAsset",
                    ["m"] = "maker",
                    ["T"] = "tradeTime",
                    ["t"] = "tradeId"
                },

                ["tradeEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["t"] = "tradeId",
                    ["p"] = "price",
                    ["q"] = "quantity",
                    ["b"] = "buyerOrderId",
                    ["a"] = "sellerOrderId",
                    ["T"] = "time",
                    ["m"] = "maker",
                    ["M"] = "ignored"
                },

                ["24hrTickerEvent"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["e"] = "eventType",
                    ["E"] = "eventTime",
                    ["s"] = "symbol",
                    ["p"] = "priceChange",
                    ["P"] = "priceChangePercent",
                    ["w"] = "weightedAveragePrice",
                    ["x"] = "previousClose",
                    ["c"] = "currentClose",
                    ["Q"] = "closeQuantity",
                    ["b"] = "bestBid",
                    ["B"] = "bestBidQuantity",
                    ["a"] = "bestAskPrice",
                    ["A"] = "bestAskQuantity",
                    ["o"] = "open",
                    ["h"] = "high",
                    ["l"] = "low",
                    ["v"] = "baseAssetVolume",
                    ["q"] = "quoteAssetVolume",
                    ["O"] = "openTime",
                    ["C"] = "closeTime",
                    ["F"] = "firstTradeId",
                    ["L"] = "lastTradeId",
                    ["n"] = "trades"
                }
            };
        }

        public JsonNode? Beautify(JsonNode? input, object? type = null)
        {
            if (input is null)
                return null;

            // If input is an array and the top-level mapping for the provided `type`
            // is an array mapping, apply mapping to each element.
            if (input is JsonArray arr && TryResolveBeautifications(type, out var arrMappingObj) && arrMappingObj is List<Dictionary<string, object>> arrMapping)
            {
                var outArr = new JsonArray();
                foreach (var element in arr)
                {
                    outArr.Add(Beautify(element, arrMapping[0]));
                }
                return outArr;
            }

            // Only beautify JSON objects here; primitives are returned as-is.
            if (input is not JsonObject obj)
                return input;

            var beautifications = ResolveBeautifications(type);
            if (beautifications == null)
            {
                // nothing to do
                return input;
            }

            var result = new JsonObject();
            foreach (var kv in obj)
            {
                var key = kv.Key;
                var value = kv.Value;

                // Determine mapping for this key (could be string name, object mapping or list mapping)
                beautifications.TryGetValue(key, out var mappingForKey);

                // Resolve target property name
                string newKey = key;
                if (mappingForKey is string mappedName)
                {
                    newKey = mappedName;
                }

                // If value is array, handle array element beautification
                if (value is JsonArray valueArr)
                {
                    JsonArray newArray = new JsonArray();

                    // Case A: mappingForKey is string that names another top-level beautification that is an array mapping
                    if (mappingForKey is string s && _beautifications.TryGetValue(s, out var referenced) && referenced is List<Dictionary<string, object>> referencedList)
                    {
                        foreach (var element in valueArr)
                        {
                            newArray.Add(Beautify(element, referencedList[0]));
                        }
                        result[newKey] = newArray;
                        continue;
                    }

                    // Case B: mappingForKey itself is a list mapping (direct array mapping)
                    if (mappingForKey is List<Dictionary<string, object>> listMapping)
                    {
                        foreach (var element in valueArr)
                        {
                            newArray.Add(Beautify(element, listMapping[0]));
                        }
                        result[newKey] = newArray;
                        continue;
                    }

                    // Case C: mappingForKey is an object mapping for each array element
                    if (mappingForKey is Dictionary<string, object> elementMapping)
                    {
                        foreach (var element in valueArr)
                        {
                            newArray.Add(Beautify(element, elementMapping));
                        }
                        result[newKey] = newArray;
                        continue;
                    }

                    // Fallback: beautify elements without specific mapping
                    foreach (var element in valueArr)
                    {
                        newArray.Add(Beautify(element, null));
                    }
                    result[newKey] = newArray;
                    continue;
                }

                // If value is object, recursively beautify with mappingForKey (which may be string or object)
                if (value is JsonObject nestedObj)
                {
                    result[newKey] = Beautify(nestedObj, mappingForKey ?? null);
                    continue;
                }

                // Primitive values: copy as-is
                result[newKey] = value;
            }

            return result;
        }

        private Dictionary<string, object>? ResolveBeautifications(object? type)
        {
            // If user passed a mapping dictionary directly
            if (type is Dictionary<string, object> dict)
                return dict;

            // If user passed a JsonObject as type (not common from rest code), try to convert
            if (type is JsonObject jsonObj)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in jsonObj)
                {
                    if (kv.Value is JsonValue jv)
                    {
                        map[kv.Key] = jv.ToString();
                    }
                    else if (kv.Value is JsonObject jo)
                    {
                        // not converting nested json->dictionary here; fallback to null
                        // callers typically pass string types, so this path will rarely be used
                    }
                }
                return map;
            }

            // If user passed a string type look up top-level mapping
            if (type is string typeName && _beautifications.TryGetValue(typeName, out var found) && found is Dictionary<string, object> d)
                return d;

            return null;
        }

        private bool TryResolveBeautifications(object? type, out object? mapping)
        {
            mapping = null;
            if (type is List<Dictionary<string, object>> list)
            {
                mapping = list;
                return true;
            }

            if (type is Dictionary<string, object> dict)
            {
                mapping = dict;
                return true;
            }

            if (type is string typeName && _beautifications.TryGetValue(typeName, out var found))
            {
                mapping = found;
                return true;
            }

            return false;
        }
    }
}