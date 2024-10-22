//using ServiceStack.Text;
using AlgoTrader.Shared;
using AlgoTrader.Shared.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace BitMEX
{
    //https://testnet.bitmex.com/api/explorer/#/

    public partial class BitMEXApi
    {

        private string apiKey;
        private string apiSecret;
        private int rateLimit;

        public BitMEXApi(string bitmexKey = "", string bitmexSecret = "", int rateLimit = 5000)
        {
            this.apiKey = bitmexKey;
            this.apiSecret = bitmexSecret;
            this.rateLimit = rateLimit;
        }

        #region Query
        private string BuildQueryData(Dictionary<string, string> param)
        {
            if (param == null)
                return "";

            StringBuilder b = new StringBuilder();
            foreach (var item in param)
                b.Append(string.Format("&{0}={1}", item.Key, WebUtility.UrlEncode(item.Value)));

            try { return b.ToString().Substring(1); }
            catch (Exception) { return ""; }
        }

        private string BuildJSON(Dictionary<string, string> param)
        {
            if (param == null)
                return "";

            var entries = new List<string>();
            foreach (var item in param)
                entries.Add(string.Format("\"{0}\":\"{1}\"", item.Key, item.Value));

            return "{" + string.Join(",", entries) + "}";
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private long GetExpires()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; // set expires one hour in the future
        }
        public string PostOrderTest()
        {
            var param = new Dictionary<string, string>();
            param["symbol"] = "XBTUSD";
            //param["side"] = "Buy";
            param["orderQty"] = "100";
            //param["ordType"] = "Market";
            return Query(HttpMethod.Post, "/order", param);
        }

        //official API method, not updated for 6 years
        private string Query(HttpMethod _method, string function, Dictionary<string, string> param = null, int timeout = 60000, bool auth = true, bool json = false)//default timeout is 100 seconds, have changed this to 1 minute as this timeout is mentioned in the API docs
        {
            string method = _method.Method;

            string paramData = json ? BuildJSON(param) : BuildQueryData(param);
            string url = "/api/v1" + function + ((method == "GET" && paramData != "") ? "?" + paramData : "");
            string postData = (method != "GET") ? paramData : "";

            var domain = TradeEngineBitmex.DOMAIN;
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(domain + url);
            webRequest.Method = method;

            if (auth)
            {
                string expires = GetExpires().ToString();
                string message = method + url + expires + postData;
                byte[] signatureBytes = hmacsha256(Encoding.UTF8.GetBytes(apiSecret), Encoding.UTF8.GetBytes(message));
                string signatureString = ByteArrayToString(signatureBytes);

                webRequest.Headers.Add("api-expires", expires);
                webRequest.Headers.Add("api-key", apiKey);
                webRequest.Headers.Add("api-signature", signatureString);
            }

            try
            {
                if (postData != "")
                {
                    webRequest.ContentType = json ? "application/json" : "application/x-www-form-urlencoded";
                    var data = Encoding.UTF8.GetBytes(postData);
                    using (var stream = webRequest.GetRequestStream())
                    {
                        stream.Write(data, 0, data.Length);
                    }
                }

                using (WebResponse webResponse = webRequest.GetResponse())
                using (Stream str = webResponse.GetResponseStream())
                using (StreamReader sr = new StreamReader(str))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (WebException wex) //will have Status property set to WebExceptionStatus.Timeout if the call times out
            {
                using HttpWebResponse response = (HttpWebResponse)wex.Response;
                if (response != null)
                {
                    using Stream str = response.GetResponseStream();
                    using StreamReader sr = new StreamReader(str);
                    return sr.ReadToEnd();
                }
                else
                {
                    return wex.ToString();
                }
            }
        }

        //Update to .net core - this does not work, just thrown 401 unauthorized errors. No idea why
        private string _Query(HttpMethod method, string function, Dictionary<string, string> param = null, int timeout = 60000, bool auth = true, bool json = false)//default timeout is 100 seconds, have changed this to 1 minute as this timeout is mentioned in the API docs
        {
            string paramData = json ? BuildJSON(param) : BuildQueryData(param);
            string postData = (method != HttpMethod.Get) ? paramData : "";

            if (postData == null)
            {
                return string.Empty;
            }

            string url = "/api/v1" + function + ((method == HttpMethod.Get && paramData != "") ? "?" + paramData : "");
            var domain = TradeEngineBitmex.DOMAIN;
            //HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(domain + url);

            //note, under heavy loads this can exhaust the available number of sockets as the socket resources do not get disposed immediately upon
            // release of the HttpClient object
            //The correct way to do this is to use HttpClientFactory, but that seems a bit excessive for this type of usage
            var httpClient = new HttpClient();
            var httpRequest = new HttpRequestMessage();
            httpRequest.RequestUri = new Uri(domain + url);
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeout);

            if (auth)
            {
                string expires = GetExpires().ToString();
                string message = method + url + expires + postData;
                byte[] signatureBytes = hmacsha256(Encoding.UTF8.GetBytes(apiSecret), Encoding.UTF8.GetBytes(message));
                string signatureString = ByteArrayToString(signatureBytes);

                httpRequest.Headers.Add("api-expires", expires);
                httpRequest.Headers.Add("api-key", apiKey);
                httpRequest.Headers.Add("api-signature", signatureString);
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(postData);
                using var memoryStream = new MemoryStream();
                memoryStream.Write(data, 0, data.Length);
                //A stream content that represent the actual request stream
                using var stream = new StreamContent(memoryStream);
                httpRequest.Content = stream;
                httpRequest.Content.Headers.Add("Content-Type", json ? "application/json" : "application/x-www-form-urlencoded");

                //Send the request
                var response = httpClient.Send(httpRequest);

                //Ensure we got success response from the server
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result;
            }
            catch (HttpRequestException rex)
            {
                //do nothing, this may return from delete orders if there is nothing to delete but it must say 'error'
                return "error:" + rex.ToString();
            }
            catch (WebException wex) //will have Status property set to WebExceptionStatus.Timeout if the call times out
            {
                using HttpWebResponse response = (HttpWebResponse)wex.Response;
                if (response != null)
                {
                    using Stream str = response.GetResponseStream();
                    using StreamReader sr = new(str);
                    return sr.ReadToEnd();
                }
                else
                {
                    return "error:" + wex.ToString();
                }
            }
            catch (Exception ex)
            {
                return "";
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        private byte[] hmacsha256(byte[] keyByte, byte[] messageBytes)
        {
            using (var hash = new HMACSHA256(keyByte))
            {
                return hash.ComputeHash(messageBytes);
            }
        }
        #endregion

        #region RateLimiter

        private long lastTicks = 0;
        private object thisLock = new object();

        private void RateLimit()
        {
            lock (thisLock)
            {
                long elapsedTicks = DateTime.Now.Ticks - lastTicks;
                var timespan = new TimeSpan(elapsedTicks);
                if (timespan.TotalMilliseconds < rateLimit)
                    Thread.Sleep(rateLimit - (int)timespan.TotalMilliseconds);
                lastTicks = DateTime.Now.Ticks;
            }
        }

        #endregion RateLimiter

        #region Order book
        public class OrderBookItem
        {
            public string Symbol { get; set; }
            public int Level { get; set; }
            public int BidSize { get; set; }
            public decimal BidPrice { get; set; }
            public int AskSize { get; set; }
            public decimal AskPrice { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public List<OrderBookItem> GetOrderBook(string symbol, int depth)
        {
            //this threw and invalid credentials error for a while, then started to work....
            //var v = bitmexAPI.GetOrderBook("XBt", 100);

            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["depth"] = depth.ToString()
            };

            string res = Query(HttpMethod.Get, "/orderBook", param);
            try
            {
                var v = JsonConvert.DeserializeObject<OrderBookItem[]>(res);
                return v.ToList();
            }
            catch
            {
                return null;
            }
        }
        #endregion

        //the tick size for XBT is 0.1, any more decimal places and orders, stops etc. will fail where the price is specified
        public static double GetTickSize(string symbolName, double price)
        {
            var decPlaces = SymbolNames.GetTickSizeForSymbol(symbolName);
            return Math.Round(price, decPlaces);
        }

        //public string GetOrders()
        //{
        //    var param = new Dictionary<string, string>();
        //    param["symbol"] = "XBTUSD";
        //    //param["filter"] = "{\"open\":true}";
        //    //param["columns"] = "";
        //    //param["count"] = 100.ToString();
        //    //param["start"] = 0.ToString();
        //    //param["reverse"] = false.ToString();
        //    //param["startTime"] = "";
        //    //param["endTime"] = "";
        //    return Query(GET, "/order", param);
        //}

        //public string DeleteOrders()
        //{
        //    var param = new Dictionary<string, string>();
        //    param["orderID"] = "de709f12-2f24-9a36-b047-ab0ff090f0bb";
        //    param["text"] = "cancel order by ID";
        //    return Query("DELETE", "/order", param);
        //}


        #region Error handling
        public class BitmexAPIErrorMessage
        {
            required public string message { get; set; }
            required public string name { get; set; }
        }
        public class BitmexAPIError
        {
            public const string Error = "ERROR";

            [JsonProperty("error")]
            required public BitmexAPIErrorMessage ErrorMessage { get; set; }
        }


        public BitmexAPIError GetError(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<BitmexAPIError>(json);
            }

            catch (Exception ex1)
            {
                return null;
            }
        }
        #endregion

        public class BitmexAPIObject
        {
            //public BitmexAPIError Error { get; set; }
            [JsonProperty("account")]
            required public string Account { get; set; }

            [JsonProperty("text")]
            required public string Text { get; set; }

            [JsonProperty("transactTime")]
            required public string TransactionTime { get; set; }

            [JsonProperty("timestamp")]
            required public string Timestamp { get; set; }

            protected double GetValueDouble(string s)
            {
                if (double.TryParse(s, out double ret))
                {
                    return ret;
                }
                return 0;
            }
        }

        public List<T> DeserializeList<T>(string ret)
        {
            return JsonConvert.DeserializeObject<List<T>>(ret);
        }

        #region Execution history
        public class ExecutionHistory : BitmexAPIObject
        {
            [JsonProperty("orderID")]
            required public string orderId { get; set; }

            [JsonProperty("lastQty")]
            required public string Quantity { get; set; }

            [JsonProperty("lastPx")]
            required public string Price { get; set; }

            [JsonProperty("ordType")]
            required public string OrderType { get; set; }
        }

        public List<ExecutionHistory> GetExecutionHistory(string symbol, DateTime time, string orderId)
        {
            var timeStamp = time.ToString("yyyy-MM-ddT23:59:59.000Z");
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["timestamp"] = timeStamp
            };

            var ret = Query(HttpMethod.Get, "/user/executionhistory", param);

            if (ret.Contains("error"))
            {
                LogTrades.WriteLine(ret);
                return null;
            }

            // try
            //{
            var j = DeserializeList<ExecutionHistory>(ret);
            return j.Where(x => x.orderId == orderId).ToList();// && x.Text.Contains("Triggered")).ToList();
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine(ex.ToString());
            //    return new List<Position>();
            //}

            return new List<ExecutionHistory>();
        }

        #endregion

        #region Wallet
        public class Wallet : BitmexAPIObject
        {
            /*
             * {"account":409805,"currency":"XBt","deposited":0,"withdrawn":0,"transferIn":1000000,"transferOut":0,"amount":1000000,"pendingCredit":0,"pendingDebit":0,"confirmedDebit":0,"timestamp":"2023-07-09T12:00:00.132Z"}
             * */
            [JsonProperty("currency")]
            public string Currency { get; set; }

            [JsonProperty("amount")]
            public double Amount { get; set; }
        }

        public class Margin : BitmexAPIObject
        {
            [JsonProperty("amount")]
            public double Amount { get; set; }

            [JsonProperty("unrealisedPnl")]
            public double UnrealisedPnl { get; set; }
        }


        public List<Wallet> GetWallet(double timeout = 0)
        {
            //$89,990 will be returned as 89,990,000,000 - x 10^6 for some reason
            //0.3384 XBT will be returned as 3384,0000 - x 10^8 as this is in satoshis ?
            var param = new Dictionary<string, string>
            {
                ["currency"] = "all"
            };

            var json = timeout == 0
                ? Query(HttpMethod.Get, "/user/wallet", param)
                : Query(HttpMethod.Get, "/user/wallet", param, (int)(timeout * 1000));

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return DeserializeList<Wallet>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                //return new List<Wallet>() { new Wallet { Error = GetError(ret) } };
                return null;
            }
        }

        public Wallet GetWalletSingleCurrency(string currency)
        {
            //$89,990 will be returned as 89,990,000,000 - x 10^6 for some reason
            //0.3384 XBT will be returned as 3384,0000 - x 10^8 as this is in satoshis ?
            var param = new Dictionary<string, string>
            {
                ["currency"] = currency
            };

            var json = Query(HttpMethod.Get, "/user/wallet", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<Wallet>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                //return new List<Wallet>() { new Wallet { Error = GetError(ret) } };
                return null;
            }
        }

        public Margin GetMargin()
        {
            var json = Query(HttpMethod.Get, "/user/margin");

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<Margin>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                //return new List<Wallet>() { new Wallet { Error = GetError(ret) } };
                return null;
            }
        }
        #endregion

        #region Positions

        public class Position : BitmexAPIObject
        {
            /*
                account:            Your unique account ID.
                symbol:             The contract for this position.
                currency:           The margin currency for this position.
                underlying:         Meta data of the symbol.
                quoteCurrency:      Meta data of the symbol, All prices are in the quoteCurrency
                commission:         The maximum of the maker, taker, and settlement fee.
                initMarginReq:      The initial margin requirement. This will be at least the symbol's default initial maintenance margin, but can be higher if you choose lower leverage.
                maintMarginReq:     The maintenance margin requirement. This will be at least the symbol's default maintenance maintenance margin, but can be higher if you choose a higher risk limit.
                riskLimit:          This is a function of your maintMarginReq.
                leverage:           1 / initMarginReq.
                crossMargin:        True/false depending on whether you set cross margin on this position.
                deleveragePercentile: Indicates where your position is in the ADL queue.
                rebalancedPnl:      The value of realised PNL that has transferred to your wallet for this position.
                prevRealisedPnl:    The value of realised PNL that has transferred to your wallet for this position since the position was closed.
                currentQty:         The current position amount in contracts.
                currentCost:        The current cost of the position in the settlement currency of the symbol (currency).
                currentComm:        The current commission of the position in the settlement currency of the symbol (currency).
                realisedCost:       The realised cost of this position calculated with regard to average cost accounting.
                unrealisedCost:     currentCost - realisedCost.
                grossOpenPremium:   The amount your bidding above the mark price in the settlement currency of the symbol (currency).
                markPrice:          The mark price of the symbol in quoteCurrency.
                markValue:          The currentQty at the mark price in the settlement currency of the symbol (currency).
                homeNotional:       Value of position in units of underlying.
                foreignNotional:    Value of position in units of quoteCurrency.
                realisedPnl:        The negative of realisedCost.
                unrealisedPnl:      unrealisedGrossPnl.
                liquidationPrice:   Once markPrice reaches this price, this position will be liquidated.
                bankruptPrice:      Once markPrice reaches this price, this position will have no equity.
            */

            [JsonProperty("isOpen")]
            public string IsOpen { get; set; }

            [JsonProperty("symbol")]
            public string Symbol { get; set; } //e.g. XBTUSD

            [JsonProperty("underlying")]
            public string UnderlyingCurrency { get; set; }//e.g. XBT: This will fail if passed as a parameter to the Wallet method

            [JsonProperty("quoteCurrency")]
            public string QuoteCurrency { get; set; } //e.g. USD

            [JsonProperty("currency")]
            public string Currency { get; set; } //e.g. XBt: this is the value which can be passed into the Wallet method, to get a specific currenct. 

            [JsonProperty("leverage")]
            public double? Leverage { get; set; }

            [JsonProperty("openingQty")]
            public double? OpeningQuantity { get; set; }

            [JsonProperty("currentQty")]
            public double? CurrentQuantity { get; set; }

            [JsonProperty("liquidationPrice")]
            public double? LiquidationPrice { get; set; }
        }

        public List<Position> GetOpenPositions(string symbolName = null, double timeout = 60)
        {
            var symbol = symbolName != null ? "{" + $@"""symbol"":""{symbolName}""" + "}" : null;
            var param = new Dictionary<string, string>
            {
                ["filter"] = symbol
            };

            var ret = Query(HttpMethod.Get, "/position", param, (int)(timeout * 1000));

            if (ret.Contains("error"))
            {
                LogTrades.WriteLine(ret);
                return null;
            }

            // try
            //{
            var j = DeserializeList<Position>(ret);
            return j.Where(x => x.IsOpen == "true").ToList();
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine(ex.ToString());
            //    return new List<Position>();
            //}
        }

        public List<Position> GetClosedPositions(string symbolName = null, double timeout = 60)
        {
            var symbol = symbolName != null ? "{" + $@"""symbol"":""{symbolName}""" + "}" : null;
            var param = new Dictionary<string, string>
            {
                ["filter"] = symbol
            };

            var ret = Query(HttpMethod.Get, "/position", param, (int)(timeout * 1000));

            if (ret.Contains("error"))
            {
                LogTrades.WriteLine(ret);
                return null;
            }

            // try
            //{
            var j = DeserializeList<Position>(ret);
            return j.Where(x => x.IsOpen == "false").ToList();
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine(ex.ToString());
            //    return new List<Position>();
            //}
        }
        #endregion

        #region Set leverage
        public bool SetLeverage(string symbol, double leverage)
        {
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["leverage"] = leverage.ToString()
            };

            var json = Query(HttpMethod.Post, "/position/leverage", param);

            //this can be deserialized to a lot of info, but there is no need to do that here

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return false;
            }
            return true;
        }
        #endregion

        #region Order
        public class Order : BitmexAPIObject
        {

            [JsonProperty("orderID")]
            public string OrderID { get; set; }

            [JsonProperty("ordStatus")]
            public string OrderStatus { get; set; }

            [JsonProperty("ordRejReason")]
            public string OrderRejectionReason { get; set; }

            [JsonProperty("avgPx")]
            public double? AveragePrice { get; set; }

            [JsonProperty("stopPx")]
            public double? StopTriggerPrice { get; set; }

            [JsonProperty("orderQty")]
            public double? OrderQuantity { get; set; }

            [JsonProperty("leavesQty")]
            public double? LeavesQuantity { get; set; }

            public bool OrderClosedOnExchange; //added to handle this situation

            /*
            [JsonProperty("cumQty")]
            public double? CumulativeQuantity { get; set; }
            
            [JsonProperty("clOrdID")]
            public string ClientOrderId { get; set; }    
            
            [JsonProperty("clOrdLinkID")]
            public string ClientLinkOrderId { get; set; }

            [JsonProperty("symbol")]
            public string Symbol { get; set; }

            [JsonProperty("triggered")]
            public string Triggered { get; set; }       
            
            [JsonProperty("side")]
            public string Side { get; set; }


            [JsonProperty("price")]
            public string Price { get; set; }

            [JsonProperty("displayQty")]
            public string DisplayQuantity { get; set; }


            [JsonProperty("pegOffsetValue")]
            public string TrailingOffset { get; set; }

            [JsonProperty("pegPriceType")]
            public string PegPriceType { get; set; }

            [JsonProperty("currency")]
            public string Currency { get; set; }

            [JsonProperty("settlCurrency")]
            public string SettlementCurrency { get; set; }

            [JsonProperty("ordType")]
            public string OrderType { get; set; }

            [JsonProperty("timeInForce")]
            public string TimeInForce { get; set; }

            [JsonProperty("execInst")]
            public string ExecutionInstructions { get; set; }

            [JsonProperty("contingencyType")]
            public string ContingencyType { get; set; }

            [JsonProperty("workingIndicator")]
            public string WorkingIndicator { get; set; }

            [JsonProperty("leavesQty")]
            public string LeavesQuantity { get; set; }

            [JsonProperty("cumQty")]
            public string CumulativeQuantity { get; set; }
            */

            //public double GetAveragePrice()
            //{
            //    return GetValueDouble(AveragePrice);
            //}
            //public double GetCumulativeQuantity()
            //{
            //    return GetValueDouble(CumulativeQuantity);
            //}
        }

        /* 
         * Send order
         * Set negative order quantity for sell order
         * Will default to limit order if limitPrice is set
         * Will default to stop if stopPrice is set
         * Otherwise will default to market order
         * There are other order types which could be specified - MarketIfTouched, LimitIfTouched, Pegged.
         * */

        public Order SendOrder(
            string symbol,
            double orderQuantity = 0,
            string execInst = null, //optional excution instruction(s)
            double limitPrice = 0,
            double stopPrice = 0
            )
        {
            //$89,990 will be returned as 89,990,000,000 - x 10^6 for some reason
            //0.3384 XBT will be returned as 3384,0000 - x 10^8 as this is in satoshis ?
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
            };
            if (orderQuantity != 0)
            {
                param.Add("orderQty", orderQuantity.ToString());
            }
            if (limitPrice != 0)
            {
                limitPrice = GetTickSize(symbol, limitPrice);
                param.Add("price", limitPrice.ToString());
                param.Add("ordType", "Limit");
            }
            if (stopPrice != 0)
            {
                stopPrice = GetTickSize(symbol, stopPrice);
                param.Add("stopPx", stopPrice.ToString());
                param.Add("ordType", "Stop");
            }
            if (execInst != null)
            {
                param.Add("execInst", execInst.ToString());
            }

            var json = Query(HttpMethod.Post, "/order", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);

                if (json.Contains("current position is 0"))
                {
                    var v = JsonConvert.DeserializeObject(json);
                    return new Order
                    {
                        OrderClosedOnExchange = true,
                        Account = null,
                        Text = "",
                        TransactionTime = null,
                        Timestamp = null
                    };
                }

                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<Order>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }

        public Order GetOrder(string symbol, string orderID)
        {

            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                //["filter"] = @"{""open"":""" + open.ToString() + @"""}",
                ["filter"] = @"{""orderID"":""" + orderID + @"""}"

            };


            var json = Query(HttpMethod.Get, "/order", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine("GetOrder: " + json);
                return null;
                //throw new Exception();
            }
            try
            {
                //if order is not found, this will create an empty list so the return object will be null
                return DeserializeList<Order>(json).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }

        public List<Order> GetOrders(string symbol)
        {

            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol
            };


            var json = Query(HttpMethod.Get, "/order", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine("GetOrders: " + json);
                return null;
            }
            try
            {
                return DeserializeList<Order>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }

        public Order AmendStopOrder(_Trade stop)
        {
            try
            {
                var tickPrice = GetTickSize(stop.SymbolName, stop.Price);
                var order = GetOrder(stop.SymbolName, stop.ItemId);
                if (order == null)
                {
                    return null;
                }

                // if order.StopTriggerPrice - tickPrice < 3 this will also throw error, and can't really check for this range as
                // it may be unsuitable for other symbols
                if (order.StopTriggerPrice == tickPrice)
                {
                    return order;
                }

                var param = new Dictionary<string, string>
                {
                    ["orderID"] = stop.ItemId,
                    ["stopPx"] = GetTickSize(stop.SymbolName, stop.Price).ToString(),
                    ["orderQty"] = (-stop.Quantity).ToString(), //must specify either order quantity or leaves quantity
                                                                //["leavesQty"] = order.LeavesQuantity.ToString(), //don't send this it just throws error
                };

                var json = Query(HttpMethod.Put, "/order", param);

                if (json.Contains("error"))
                {
                    if (!json.Contains("Invalid amend: orderQty, leavesQty, price, stopPx unchanged")) //this is the error message when stop price is too close to existing order price
                    {
                        LogTrades.WriteLine(json);
                    }
                    return null;
                }
                return JsonConvert.DeserializeObject<Order>(json);
            }
            catch
            {
                return null;
            }
        }


        public Order CloseOrder(string symbol)
        {
            return SendOrder(symbol, 0, "Close");
        }

        public List<Order> DeleteOrder(string orderId)
        {
            var param = new Dictionary<string, string>
            {
                ["orderID"] = orderId
            };

            var json = Query(HttpMethod.Delete, "/order", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return DeserializeList<Order>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }

        public List<Order> DeleteAllOrders(string symbol)
        {
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbol
            };

            var json = Query(HttpMethod.Delete, "/order/all", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return DeserializeList<Order>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }
        #endregion

        #region Get last price
        class LastPrice
        {
            [JsonProperty("close")]
            public double Close { get; set; }

        }
        public double GetLastPrice(string symbolName)
        {
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbolName,
                ["binSize"] = "1m",
                ["reverse"] = "true"
            };

            var json = Query(HttpMethod.Get, "/trade/bucketed", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return 0;
            }
            try
            {
                var list = DeserializeList<LastPrice>(json);
                var lastPrice = list[0].Close;
                return lastPrice;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return 0;
            }
        }
        #endregion

        #region Get lot size
        public class Instrument
        {
            [JsonProperty("lotSize")]
            public double LotSize { get; set; }
            [JsonProperty("makerFee")]
            public double MakerFee { get; set; }
            [JsonProperty("takerFee")]
            public double TakerFee { get; set; }

        }
        public Instrument GetInstrument(string symbolName)
        {
            var param = new Dictionary<string, string>
            {
                ["symbol"] = symbolName,
                ["columns"] = "lotSize,makerFee,takerFee"
            };

            var json = Query(HttpMethod.Get, "/instrument", param);

            if (json.Contains("error"))
            {
                LogTrades.WriteLine(json);
                return null;
            }
            try
            {
                return DeserializeList<Instrument>(json)[0];
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                LogTrades.WriteLine(json);
                return null;
            }
        }
        #endregion

    }
}