using AlgoTrader.Shared.Enums;
using AlgoTrader.Shared.Logging;
using System.Data.SqlClient;
using System.IO;
using System.Windows.Threading;

namespace AlgoTrader.Shared.ExchangeConnection
{
    public class ExchangeConnector
    {

       static DateTime startTime;
       ExchangeDataProcessor exchangeDataProcessor;

       public struct FeedDataItem
        {
            internal DateTime timestamp;
            internal double price;
            internal string symbolName;
        }


        public bool IsConnected
        {
            get
            {
                if (exchangeDataProcessor.IsConnected)
                {
                    return true;
                }
                return false;
            }
        }

        public ExchangeConnector()
        {
            switch (TradeEngine.EXCHANGE)
            {
                case ExchangeId.BINANCE_SPOT:
                case ExchangeId.BINANCE_MARGIN:
                    {
                        exchangeDataProcessor = new ExchangeDataProcessorBinance();
                        break;
                    }
            }

            startTime = DateTime.UtcNow;
            /*
             * +++   DO NOT CHANGE THE SOLUTION NAME   +++
             * get correct node executable to start - this allows the code to kill multiple processes but only for a specific instance of the program
             * i.e. the Save To DB version can still run when the testing or debug versions start and stop
             * There are 2 versions - save to dB, and testing, both use a separate version for release and debug (so debug can run and not affect a running release version)
             *  - keep the save to DB version exclusively for just doing that, run the testing version as Release for long test, Debug for development
             *  */

            
        }

        //todo - sort out filename from: var value = System.Configuration.ConfigurationManager.ConnectionStrings["DataContext"];
        //set backup folder in Constants (possibly rename to Config???)
        //set up for sequential backups, although disk space may be an issue in Onedrive (perhaps have more than one folder???)

        public void StopTimers()
        {
            exchangeDataProcessor.StopTimers();
        }
 
        internal void CloseConnection()
        {
            exchangeDataProcessor.CloseConnection();
        }

        internal void RestartConnection()
        {
            exchangeDataProcessor.RestartConnection();
        }

        internal bool ToggleConnection()
        {
            bool ret = false;
            if (exchangeDataProcessor.ToggleConnection())
            {
                ret = true;
            }
            return ret;
        }
    }
}
