using AlgoTrader.Shared;

namespace AlgoTrader.Windows
{
    public partial class MainWindow
    {
        void SetupConstants()
        {
            //****//
            //LIVE//
            //****//

            //Connection
            MainObj.BACKTEST = true; //set TRUE for testnet trading
            MainObj.READ_LIVE_DATA = true; //if true, will update items live. If writing to DB, this is ignored
            MainObj.WRITE_TO_DB = true;

            //Trading
            TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BITMEX;
            TradeEngine.USE_SIMULATED_EXCHANGE = false;
            TradeEngine.RESTRICTED_TRADE_VALUE = 0;

            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0; //percent to offset trade entry lines
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0; //percent to offset trade stop lines

            TradeEngine.INITIAL_TAKE_PROFIT_PERCENT_MARKET = 0; //will auto-set t/p if this value is set
            TradeEngine.MAX_STOP_LOSS = 6; //leverage will scale to always target this as a stop loss if actual loss is greater from default leverage
           

            /* 
             * Reason for not scaling leverage to stop depth:
             * The first trade may scale to 5X, but that may likely fail, and the next may scale to less - 
             *  with the market becoming more volatile before it breaks -
             *  and this can happen a few times, so the final successful trade makes a loss against the previous failed trades.
             * So it is better to just have a fixed leverage, and only scale it if the stop is excessive for some reason
             * */
            TradeEngineBitmex.MIN_LEVERAGE_SHORT = 1;
            TradeEngineBitmex.MIN_LEVERAGE_LONG = 1.333;
            TradeEngineBitmex.DEFAULT_LEVERAGE = 3;
            TradeEngine.MINIMUM_TRADE_TIME = 0;

            TradeEngine.STOP_MIN_VALUE = 1.25; //will move stop or floor to this value when trade enters, and will auto-set floor at this level if there is no floor
            TradeEngine.MAX_ENTRY_DIFF_PERCENT = 0.5;


            /*************************************************************/
            /* Set correct domain in BitMEXAPI.cs */
            /* API keys need 'Order', not 'Order Cancel' permission, as the latter is to only cancel orders */
            /*************************************************************/

            //never live here
            TradeEngineBitmex.KEY = "p0Y99qfKEYSYjsXwNgvegSMp"; //TESTNET TRADING - testnet.bitmex.com
            TradeEngineBitmex.SECRET = "G6HCrOwgYU0MlRXLY4JiHCYKjKIfltOOAhkdGEg5rM0JzfHc";
            TradeEngineBitmex.DOMAIN = "https://testnet.bitmex.com";

            //Bitmex has a maximum order price of 1000000, and a short stop will excede this even though the liquidation price will be set automatically to well above this maximum.
            //Limit the value of any short stops, otherwise a Floor will not be set and this is just inconsistent even though it is rather pointless to set one anyway.
            TradeEngine.MAX_SHORT_STOP = 500000;

            //Display
            MainObj.NUM_DAYS_TO_SHOW = 90;
            MainObj.INITIAL_TIMEFRAME = 60;
        }
    }

}
