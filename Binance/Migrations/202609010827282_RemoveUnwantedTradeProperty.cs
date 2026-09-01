namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class RemoveUnwantedTradeProperty : DbMigration
    {
        public override void Up()
        {
            DropColumn("dbo._Trades", "ExecutionSystem");
        }
        
        public override void Down()
        {
            AddColumn("dbo._Trades", "ExecutionSystem", c => c.Int(nullable: false));
        }
    }
}
