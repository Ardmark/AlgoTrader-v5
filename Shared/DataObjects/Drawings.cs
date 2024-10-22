using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlgoTrader.Shared
{
    public partial class UIDrawingBase
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string SymbolName { get; set; }
        public Enums.ExchangeId ExchangeId
        {
            get
            {
                return TradeEngine.EXCHANGE;
            }
        }
        public string TradeReference { get; set; }

        public bool IsVisible { get; set; } = true;
        public double Opacity { get; set; } = 1;

        //color (serialized, unused)
        public byte Red { get; set; } = 0;
        public byte Green { get; set; } = 0;
        public byte Blue { get; set; } = 0;
        public byte Alpha { get; set; } = 255;

        [NotMapped]
        public byte R_L { get; set; } = 0;
        [NotMapped]
        public byte G_L { get; set; } = 0;
        [NotMapped]
        public byte B_L { get; set; } = 0;
        [NotMapped]
        public byte A_L { get; set; } = 255;
        [NotMapped]
        public byte R_D { get; set; } = 0;
        [NotMapped]
        public byte G_D { get; set; } = 0;
        [NotMapped]
        public byte B_D { get; set; } = 0;
        [NotMapped]
        public byte A_D { get; set; } = 255;

        //drawing can be associated with a trade
        //This could be the other way round, but ChartLine needs to access the attached trade object for stop movement checks,
        // which cannot be done without a db context synchronisation and a linq query, if the trade is not attached to the line
        //Also, since the line is not always required with the trade, it is more efficient to do it this way
        public _Trade Trade { get; set; }
        public int? TradeId { get; set; } //must be nullable or if Trade not set, will still try to find it with Id of zero which throws exception on 1SaveChanges()

        //don't need this in db
        //[System.ComponentModel.DataAnnotations.Schema.NotMapped]
        //public TradeUpdateRequest UpdateRequest { get; set; }

        //unused
        //public int StartFrame { get; set; } 
        //public int EndFrame { get; set; }

        public virtual void CopyTo(UIDrawingBase uiDrawingBase)
        {
            uiDrawingBase.TradeId = TradeId;
            uiDrawingBase.SymbolName = SymbolName;
            uiDrawingBase.TradeReference = TradeReference;
            uiDrawingBase.IsVisible = IsVisible;
            uiDrawingBase.Opacity = Opacity;
            uiDrawingBase.Red = Red;
            uiDrawingBase.Green = Green;
            uiDrawingBase.Blue = Blue;
            uiDrawingBase.Alpha = Alpha;
        }
    }


}
