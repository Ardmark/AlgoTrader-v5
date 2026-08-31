using AlgoTrader.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlgoTrader.Shared
{
    public class _Trade
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string TradeReference { get; set; } //shared GUID for all trades in this order group (order, entry, stop etc.)
        public string SymbolName { get; set; } //the specific name of the trade type, i.e. 'Crossover market', 'Stochastic crossing' etc.
        public TradeItemStatus ItemStatus { get; set; }
        public TradeGlobalStatus GlobalStatus { get; set; }
        public TradeType Type { get; set; } //price break, line break etc
        public TradeAction Action { get; set; }
        public TradeStop Stop { get; set; }
        public TradeExchangeStatus TradeExchangeStatus { get; set; } //entered on or off exchange
        public TradeExecutionStatus TradeExecutionStatus { get; set; }
        public DateTime Timestamp { get; set; }
        public double ActionValue { get; set; } //trail value
        public int TimeFrame { get; set; }
        public double Leverage { get; set; } = 1;
        public TradeExecutionSystem ExecutionSystem { get; set; } //manual or auto
        public TradePosition Position { get; set; } //long or short
        public double Quantity { get; set; } //note, decimal has only 2 decimal places in db, so will truncate anything < 0.01 to zero
        public double Price { get; set; } //whatever the price is at the time of the trade, this will likely not be the minute close or open value
        public double Result { get; set; }
        public string ItemId { get; set; } //unique id for this trade, as opposed to the TradeReference which is shared across all trades used in this order.Note: Binance will allow re-use of an Id providing no orders are open and using that Id, but it is better to assign a unique Id each time anyway
        public string OrderListId { get; set; } //OCO's are sent as an order list and need to be cancelled with that Id, the individual Id's are not saved in the exchange order so cannot cancel orders individually
        public double LiquidationPrice { get; set; }
        public double Balance { get; set; }
        public double ExchangeRate { get; set; }
        public double Equity {  get ; set; }

        #region Drawing (one - to - many, unused)
        //adding this seems to change the entire db structure, renaming dbo.ChartLines to dbo.DrawingBases....
        //but this is pointless anyway, as a trade can only be attached to a single ui drawing object anyway
        //public ICollection<DrawingBase> Drawings { get; set; }
        #endregion

        //don't need this in db
        //[System.ComponentModel.DataAnnotations.Schema.NotMapped]
        //public TradeUpdateRequest UpdateRequest { get; set; }


        public static string GetNewTradeId()
        {
            return Guid.NewGuid().ToString().Replace("-", ""); //used as a canvas name, it cannot have hyphens
        }

        //note, clone and copy do not transfer the ExchangeMethodScript or ExchangeMethodIndex
        internal _Trade Clone()
        {
            return new _Trade
            {
                //Id = Id, //do not clone the Id, since that is unique between trades and is EF-assigned
                Id = 0, //marks as a newly created trade
                TradeReference = TradeReference,
                Type = Type,
                Position = Position,
                Action = Action,
                Stop = Stop,
                ExecutionSystem = ExecutionSystem,
                Timestamp = Timestamp,
                ActionValue = ActionValue,
                TimeFrame = TimeFrame,
                Price = Price,
                Result = Result,
                SymbolName = SymbolName,
                TradeExchangeStatus = TradeExchangeStatus,
                Quantity = Quantity,
                TradeExecutionStatus = TradeExecutionStatus,
                ItemId = ItemId,
                LiquidationPrice = LiquidationPrice,
                Leverage = Leverage,

                //although these are defaults, set them explicitly anyway
                ItemStatus = TradeItemStatus.OPEN,
                GlobalStatus = TradeGlobalStatus.ACTIVE,
            };
        }
        internal void CopyTo(_Trade trade)
        {
            //Id = stop.Id; as for Clone()
            trade.TradeReference = TradeReference;
            trade.Type = Type;
            trade.Position = Position;
            trade.Action = Action;
            trade.Stop = Stop;
            trade.ExecutionSystem = ExecutionSystem;
            trade.Timestamp = Timestamp;
            trade.ActionValue = ActionValue;
            trade.TimeFrame = TimeFrame;
            trade.Price = Price;
            trade.Result = Result;
            trade.SymbolName = SymbolName;
            trade.TradeExchangeStatus = TradeExchangeStatus;
            trade.ItemStatus = ItemStatus;
            trade.GlobalStatus = GlobalStatus;
            trade.Quantity = Quantity;
            trade.TradeExecutionStatus = TradeExecutionStatus;
            trade.ItemId = ItemId;
            trade.LiquidationPrice = LiquidationPrice;
            trade.Leverage = Leverage;
        }
    }
}
