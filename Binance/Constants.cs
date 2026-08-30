using AlgoTrader.Shared;

namespace AlgoTrader.Windows
{
    public partial class MainWindow
    {
        private static void SetupConstants()
        {
            #region Exchange
            TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BINANCE;

            
            //live (keys are OK but commented to be safe)
            //TradeEngine.LIVE_KEY =    "vVo1RvHqDbC07mA7QTaj8Q7dtF6qE7mCrTBztdHOidQLml5dhdk4v1nLDwlzHyZ6"; // also, should enable only trusted IP addresses on Binance - have not done this yet
            //TradeEngine.LIVE_SECRET = "H7IhdsS3TVUoTjowVOC93lGpcVZo5PjCD1I2vwUeWbIO02N1EUTeb21nWavZfrB1"; // Same for this

            //testnet
            TradeEngine.TESTNET_KEY =    "rssumaVWFRJ6b1Uh3n6E87CXwKz3UytIfpkaGngMVx21ukjCl0Mtik8VwAePt99K";
            TradeEngine.TESTNET_SECRET = "9vTXpWsL5mSVWAb6piM40kLb9qXumu6wxYFhXwLW4OTZt8YIXc5nhGEHhtzkrK35";

            #endregion

            #region Connection
            MainObj.BACKTEST = false;
            MainObj.READ_LIVE_DATA = true; //if true, will update items live. If writing to DB, this is ignored
            MainObj.WRITE_TO_DB = true;
            #endregion

            #region Test trading
            TradeEngine.USE_TESTNET = true;
            TradeEngine.RESTRICTED_TRADE_VALUE = 10;
            #endregion

            #region Trading
            TradeEngine.FEE = 0.15; //percent, total for 2 trades
            TradeEngine.DEFAULT_LEVERAGE = 3;
            //TradeEngineBitmex.MIN_LEVERAGE_SHORT = 1;
            //TradeEngineBitmex.MIN_LEVERAGE_LONG = 1.333;

            TradeEngine.MAX_STOP_LOSS = 6; //leverage will scale to always target this as a stop loss when auto-setting a stop from the buttons (if there is no stop)
            TradeEngine.STOP_MIN_VALUE = new Dictionary<double, double>
            {
                { 5, 0.5 },
                { 60, 1.35 },
            };
            TradeEngine.MAX_ENTRY_DIFF_PERCENT = 0.5;
            //TradeEngine.BREAK_EVEN_PERCENT = 6; //will move stop or floor to break even when trade moves in profit by this percent, and will auto-set floor at this level if there is no floor
            TradeEngine.INITIAL_TAKE_PROFIT_PERCENT_MARKET = 6; //will auto-set ceiling to this value - and it is then used in the OCO (Binance) will default to 10% if set to zero here
            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0; //percent to offset trade entry lines
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0; //percent to offset trade stop lines

            TradeEngineBinance.RETAINED_BNB_PERCENTAGE = 10;

            //TradeEngine.STOP_TRAIL = 2.0; //floor trail percent

            //TradeEngine.SET_STOPS_ON_EXCHANGE = true;
            #endregion

            #region Display length and initial timeframe
            MainObj.NUM_DAYS_TO_SHOW = 30;
            MainObj.INITIAL_TIMEFRAME = 5;
            #endregion

        }
    }

}
