using AlgoTrader.Shared;

namespace AlgoTrader.Windows
{
    public partial class MainWindow
    {
        private static void SetupConstants()
        {
            #region Exchange keys
            
            //live (keys are OK but commented to be safe)
            TradeEngine.LIVE_KEY =    "vVo1RvHqDbC07mA7QTaj8Q7dtF6qE7mCrTBztdHOidQLml5dhdk4v1nLDwlzHyZ6"; // also, should enable only trusted IP addresses on Binance - have not done this yet
            TradeEngine.LIVE_SECRET = "H7IhdsS3TVUoTjowVOC93lGpcVZo5PjCD1I2vwUeWbIO02N1EUTeb21nWavZfrB1"; // Same for this

            //testnet
            TradeEngine.TESTNET_KEY =    "rssumaVWFRJ6b1Uh3n6E87CXwKz3UytIfpkaGngMVx21ukjCl0Mtik8VwAePt99K";
            TradeEngine.TESTNET_SECRET = "9vTXpWsL5mSVWAb6piM40kLb9qXumu6wxYFhXwLW4OTZt8YIXc5nhGEHhtzkrK35";

            #endregion

            //TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BINANCE_SPOT;
            TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BINANCE_MARGIN;

            TradeEngine.USE_TESTNET = false; //if true, Spot exchange will be used instead of margin, regardless of TradeEngine.EXCHANGE setting
            
            TradeEngine.RESTRICT_TRADE_VALUE = true; //set in binance.cs, this is 0.0001 BTC - the lot size filter is incorrect

            #region Connection
            MainObj.READ_LIVE_DATA = true; //if true, will update items live. If writing to DB, this is ignored
            MainObj.WRITE_TO_DB = true;
            #endregion
            
            #region Leverage
            TradeEngine.USE_FIXED_LEVERAGE = false; //if true, will use the default leverage for all trades, otherwise will calculate leverage from stop
            TradeEngine.MIN_ALLOWED_LEVERAGE_SHORT = 1;
            TradeEngine.MIN_ALLOWED_LEVERAGE_LONG = 1.333;
            TradeEngine.MAX_ALLOWED_LEVERAGE = 3; //leverage cannot excede this

            //these min and max values are just the arbitrary ranges which the leverages are calculated from.
            //The actual leverage value can be lower or higher than these values, but will never go outside MIN_ and MAX_ values above.
            TradeEngine.STOP_MAX_VALUE = new Dictionary<double, double>
            {
              //{ timeframe, stop }
              // these values are calculated against the MIN_LEVERAGE
              // e.g. for a stop of 1%, the leverage will be 5 on 5-min
                { 5, 1 },
                { 60, 2.7 },
            };           
            TradeEngine.MIN_LEVERAGE = new Dictionary<double, double>
            {
              //{ timeframe, leverage }
                { 5, 5 },
                { 60, 2 },
            };

            TradeEngine.STOP_MIN_VALUE = new Dictionary<double, double>
            {
              //{ timeframe, stop }
              // these values are calculated against the MAX_LEVERAGE
              // e.g. for a stop of 0.5%, the leverage will be 10 on 5-min
                { 5, 0.5 },
                { 60, 1.35 },
            };            
            TradeEngine.MAX_LEVERAGE = new Dictionary<double, double>
            {
              //{ timeframe, leverage }
                { 5, 10 },
                { 60, 4 },
            };

            //leverage is always reset to this after a trade is closed, and before a new trade is opened. This is also the default leverage for all trades if USE_FIXED_LEVERAGE is true.
            TradeEngine.DEFAULT_LEVERAGE = 5;

            #endregion

            #region stop loss
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0;////this percent will be used to offset line for trade exit
            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0;//this percent will be used to offset line for trade entry
            TradeEngine.STOPLINE_MIN_VALUE = 0.1; //if stopline is moved closer than this % to Order / Entry, this will trigger a UI warning
            TradeEngine.MAX_STOP_LOSS = 1; //
            #endregion

            #region Trading
            TradeEngine.FEE = 0.15; //percent, total for 2 trades
            TradeEngine.MAX_ENTRY_DIFF_PERCENT = 0.1;
            TradeEngine.INITIAL_TAKE_PROFIT_PERCENT_MARKET = 6; //will auto-set ceiling to this value - and it is then used in the OCO (Binance) will default to 10% if set to zero here
            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0; //percent to offset trade entry lines
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0; //percent to offset trade stop lines
            TradeEngine.MINIMUM_TRADE_TIME = TradeEngine.USE_TESTNET ? 0 : 15; //3 bars on 5min, really need a dictionary for this
            #endregion

            #region Display length and initial timeframe
            MainObj.NUM_DAYS_TO_SHOW = 30;
            MainObj.INITIAL_TIMEFRAME = 5;
            #endregion

        }
    }

}
