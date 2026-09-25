namespace AlgoTrader.Backtest.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class StopID : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo._ChartLines", "StopId", c => c.Int());
            CreateIndex("dbo._ChartLines", "StopId");
            AddForeignKey("dbo._ChartLines", "StopId", "dbo._Trades", "Id");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo._ChartLines", "StopId", "dbo._Trades");
            DropIndex("dbo._ChartLines", new[] { "StopId" });
            DropColumn("dbo._ChartLines", "StopId");
        }
    }
}
