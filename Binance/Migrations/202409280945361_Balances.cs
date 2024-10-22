namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class Balances : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo._Trades", "Balance", c => c.Double(nullable: false));
            AddColumn("dbo._Trades", "ExchangeRate", c => c.Double(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo._Trades", "ExchangeRate");
            DropColumn("dbo._Trades", "Balance");
        }
    }
}
