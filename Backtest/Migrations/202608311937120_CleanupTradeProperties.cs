namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CleanupTradeProperties : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo._Trades", "TradeExchangeStatus", c => c.Int(nullable: false));
            AddColumn("dbo._Trades", "TradeExecutionStatus", c => c.Int(nullable: false));
            DropColumn("dbo._Trades", "ExchangeStatus");
            DropColumn("dbo._Trades", "ExchangeId");
            DropColumn("dbo._Trades", "ExchangeMethodScript");
        }
        
        public override void Down()
        {
            AddColumn("dbo._Trades", "ExchangeMethodScript", c => c.String());
            AddColumn("dbo._Trades", "ExchangeId", c => c.Int(nullable: false));
            AddColumn("dbo._Trades", "ExchangeStatus", c => c.Int(nullable: false));
            DropColumn("dbo._Trades", "TradeExecutionStatus");
            DropColumn("dbo._Trades", "TradeExchangeStatus");
        }
    }
}
