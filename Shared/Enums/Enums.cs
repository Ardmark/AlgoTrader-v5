using System.ComponentModel.DataAnnotations;

namespace AlgoTrader.Shared.Enums
{
    /*
     * Naming convention:
     * the rest API method name, all caps, split with an underscore
     * */

    #region Trade execution
    //ExchangeRequest - this wraps the various types of request which can be sent via API methods
    //UNUSED
    public enum ExchangeRequest
    {
        [Display(Name = "ExchangeInfo")]
        EXCHANGE_INFO,
        [Display(Name = "Account")]
        ACCOUNT,
        [Display(Name = "QuotePrice")]
        QUOTE_PRICE,
        [Display(Name = "QueryOrder")]
        QUERY_ORDER,
        [Display(Name = "AllOrders")]
        ALL_ORDERS,
        [Display(Name = "OpenOrders")]
        OPEN_ORDERS, 
        [Display(Name = "MyTrades")]
        MY_TRADES,
        [Display(Name = "CancelOrder")]
        CANCEL_ORDER,
        [Display(Name = "NewOrder")]
        NEW_ORDER,
        [Display(Name = "NewOCO")]
        NEW_OCO,
    }



   

    public enum CandlePosition
    {
        OPEN,
        CLOSE,
        HIGH,
        LOW
    }

    public enum ExchangeId
    {
        [Display(Name = "Binance")]
        BINANCE = 1,
        [Display(Name = "Bitmex")]
        BITMEX
    }

    //Order frequency - used to ensure only a certain number of trades can run each 24 hours, and whether a trade can run live or not
    public enum OrderFrequency
    {
        NULL_ORDER,
        MAX_CONCURRENT_TRADES_EXCEDED,
        MAX_TRADES_PER_24_HOURS_EXCEDED,
        PREV_CLOSE_TOO_CLOSE,
        OK_TO_ENTER_LIVE
    }
    #endregion

    #region Trade enums


    public enum TradeExchangeStatus
    {
        ENTERED_OFF_EXCHANGE, //order is not entered on the exchange, but runs anyway for testing
        ENTERED_ON_EXCHANGE,
        NEVER_ENTERED, //order was never entered,
        ENTERING_ON_EXCHANGE,
        NO_WARNING
    }
    // trade type - manual market break or auto-generated, etc.
    //NOTE - not implementing MARKET as that could be set as order type by mistake, resulting in an immediate entry
    // - similarily, LIMIT could be set without realising it, so limit orders will only be part of break orders, if ever implemented
    public enum TradeType
    {
        PRICE_BREAK_ORDER,  //Unused - for a break when a specific price is crossed. This is redundant as now all price breaks are done with lines
        LINE_BREAK_ORDER,   //price breaks line
        HMA_BREAK_ORDER,    //price breaks HMA
        MARKET_ORDER,       //immediate order
        STUB_ORDER,         //for use internally with quote asset trading (i.e. in and out of BTC, not USD(T)
        AUTO_ORDER,         //for auto-generated order
        FLIP_ORDER,         //for stops, this is a flip order (TradeAction Action will be STOP)
        NO_FLIP_ORDER,      //for stops, this will dead exit rather than flip (TradeAction Action will be WAITING_STOP)
        STOP_WAITING,        //as above but dead stop (TradeAction Action will be WAITING_STOP)
        LIMIT_ORDER
    }

    // long or short
    public enum TradePosition
    {
        LONG,
        SHORT
    }

    // action - the core of the system. Each trade of this type is read, the appropriate one taken from any grouping, and action decided accordingly
    public enum TradeAction
    {
        ORDER,
        ENTRY,
        CLOSE,
        STOP,           //line-attached
        //FLOOR,          //additional stop, not line attached
        //CEILING,        //additional take profit, not line attached
        TAKE_PROFIT,
        //BREAK_EVEN,
        WAITING_STOP,
        NULL //just a convenient way to hide take profits!
    }

    // trade status - either running or stopped, this applies to individual trades
    public enum TradeItemStatus
    {
        OPEN,
        CLOSED
    }

    // global status - this is the status of the entire trade, applies to all trades 
    public enum TradeGlobalStatus
    {
        ACTIVE,
        STOPPED
    }

    //this was used for auto entry, but now re-purposed. Auto entry would need to be defined globally now if ever used
    public enum TradeExecutionSystem
    {
        NORMAL,
        EXCHANGE_UNSYNCHED      //for floor or stop, this is set until exchange returns trade as set
    }

    //stop type, used only with TradeAction == STOP, although the default will be present on all orders obviously
    public enum TradeStop
    {
        STOP_ON_LINE_BREAK,              // - line (price break) stop
        TAKE_PROFIT_MA_CROSS,          //for a Stop which is set on a moving average (it is a stop but is only used as a take profit)
        STOP_TRAIL,             // - a trailing stop
        STOP_TRAIL_SPIKE,       // - for a spike event
        FLOOR_SET_TO_PREV_BAR   // - marks the entry as having set a floor at a previous bar
        //TAKE_PROFIT_PERCENT,         //this is for a Floor which is auto-set at some percent between entry and current high
    }

    public enum TradeUpdateRequest
    {
        //create is handled by a zero Id, so if update is set on a new trade it will get added
        UPDATE = 1,
        DELETE,
        FORCE_DELETE
    }

    #endregion

    #region Graphics
    public enum LineType
    {
        NONE,
        BREAK,
        SUPPORT,
        RESISTANCE,
        CLOSED_ENTERED,
        CLOSED_UNENTERED,
        MARKER
    }

    public enum LineStyle
    {
        SOLID,
        DOTTED,
        DASHED
    }

    public enum MarkerStyle
    {
        SQUARE,
        TRIANGLEUP,
        TRIANGLEDOWN,
        CIRCLE
    }
    #endregion

}
