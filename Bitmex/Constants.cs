using AlgoTrader.Shared;
using AlgoTrader.Shared.ExchangeConnection;
using System.Windows.Forms;

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
            MainObj.BACKTEST = false; //set TRUE for testnet trading
            MainObj.READ_LIVE_DATA = true; //if true, will update items live. If writing to DB, this is ignored
            MainObj.WRITE_TO_DB = true;

            //Trading
            TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BITMEX;
            TradeEngine.USE_SIMULATED_EXCHANGE = false;
            TradeEngine.RESTRICTED_TRADE_VALUE = 0;

            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0; //percent to offset trade entry lines
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0; //percent to offset trade stop lines

            TradeEngine.INITIAL_TAKE_PROFIT_PERCENT_MARKET = 0; //will auto-set t/p if this value is set
            TradeEngine.TARGET_STOP_LOSS = 6; //leverage will scale to always target this as a stop loss
            TradeEngineBitmex.MIN_LEVERAGE_SHORT = 1;
            TradeEngineBitmex.MIN_LEVERAGE_LONG = 1.333;
            TradeEngineBitmex.MAX_LEVERAGE = 5;
            TradeEngine.MINIMUM_TRADE_TIME = 120;
            TradeEngine.STOP_MIN_VALUE = 1;
            TradeEngine.MAX_ENTRY_DIFF_PERCENT = 0.5;

            /*************************************************************/
            /* Set correct domain in BitMEXAPI.cs */
            /* API keys need 'Order', not 'Order Cancel' permission, as the latter is to only cancel orders */
            /*************************************************************/

            TradeEngineBitmex.KEY = "6_Y9ZZuQJLxl09BFTNNMHu8L"; //LIVE TRADING - bitmex.com, not testnet.bitmex.com
            TradeEngineBitmex.SECRET = "lr1tQ7L1p2p6O0UKryJ0GXSb3ay5EBEFylFfaVeXSDn2snMP";
            TradeEngineBitmex.DOMAIN = "https://www.bitmex.com";

            //Bitmex has a maximum order price of 1000000, and a short stop will excede this even though the liquidation price will be set automatically to well above this maximum.
            //Limit the value of any short stops, otherwise a Floor will not be set and this is just inconsistent even though it is rather pointless to set one anyway.
            TradeEngine.MAX_SHORT_STOP = 500000;

            //Display
            MainObj.NUM_DAYS_TO_SHOW = 90;
            MainObj.INITIAL_TIMEFRAME = 60;

        }
    }

}
