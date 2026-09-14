namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class SaveEquity : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo._Trades", "Equity", c => c.Double(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo._Trades", "Equity");
        }
    }
}
