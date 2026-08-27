namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class BinanceAPIRelatedChanges : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo._Trades", "OrderListId", c => c.String());
            DropColumn("dbo._Trades", "TradeIdExchange");
            DropColumn("dbo._Trades", "ExchangeMethodIndex");
            DropColumn("dbo._Trades", "IsQuoteToBasePurchase");
            DropColumn("dbo._Trades", "StubCallbackTradeReference");
        }
        
        public override void Down()
        {
            AddColumn("dbo._Trades", "StubCallbackTradeReference", c => c.String());
            AddColumn("dbo._Trades", "IsQuoteToBasePurchase", c => c.Boolean(nullable: false));
            AddColumn("dbo._Trades", "ExchangeMethodIndex", c => c.Int(nullable: false));
            AddColumn("dbo._Trades", "TradeIdExchange", c => c.Long(nullable: false));
            DropColumn("dbo._Trades", "OrderListId");
        }
    }
}
