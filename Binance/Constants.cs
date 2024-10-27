using AlgoTrader.Shared;
using AlgoTrader.Shared.ExchangeConnection;
using Shared.Trading;

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
            MainObj.BACKTEST = false;
            MainObj.READ_LIVE_DATA = true; //if true, will update items live. If writing to DB, this is ignored
            MainObj.WRITE_TO_DB = true;

            //Trading
            TradeEngine.EXCHANGE = Shared.Enums.ExchangeId.BINANCE;
            TradeEngine.USE_SIMULATED_EXCHANGE = false;
            TradeEngine.RESTRICTED_TRADE_VALUE = 0;
            TradeEngine.MAX_CONCURRENT_TRADES = 1;
            TradeEngine.INITIAL_TAKE_PROFIT_PERCENT_MARKET = 0; //will auto-set ceiling if this value is set - and it is then used in the OCO (Binance) will default to 10% if set to zero here
            TradeEngine.MAX_FLIP_DIFF_PERCENT = 0;
            TradeEngineBinance.RETAINED_BNB_PERCENTAGE = 10;
            TradeEngine.MA_STOP_MIN_VALUE = 1;
            //in Binance, the key and secret are sent in the node script so these values are not in this project
            TradeEngine.STOP_TRAIL = 2.0; //floor trail percent
            TradeEngine.MAX_STOP = 2;

            TradeEngine.ENTRY_LINE_PRICE_GAP_PC = 0.1; //percent to offset trade entry lines
            TradeEngine.STOP_LINE_PRICE_GAP_PC = 0.1; //percent to offset trade stop lines

            TradeEngine.SET_STOPS_ON_EXCHANGE = false;

            //Display
            MainObj.NUM_DAYS_TO_SHOW = 90;
            MainObj.INITIAL_TIMEFRAME = 60;

        }
    }

}
