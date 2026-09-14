/*
delete from [AlgoTrader.Perpetual.Backtest].dbo.XBTUSD
insert into [AlgoTrader.Perpetual.Backtest].dbo.XBTUSD([Timestamp],[Open],[Close],[High],[Low],Interrupted)
		 select [Timestamp],[Open],[Close],[High],[Low],Interrupted from [AlgoTrader.Live].dbo.BTCUSDT
*/	


drop table [AlgoTrader.Backtest].dbo.BTCUSDT
select * into [AlgoTrader.Backtest].dbo.BTCUSDT	   from [AlgoTrader.Binance].dbo.BTCUSDT  

drop table [AlgoTrader.Backtest].dbo._ChartLines
select * into [AlgoTrader.Backtest].dbo._ChartLines	   from [AlgoTrader.Binance].dbo._ChartLines

drop table [AlgoTrader.Backtest].dbo._Trades
select * into [AlgoTrader.Backtest].dbo._Trades	   from [AlgoTrader.Binance].dbo._Trades  

delete from [AlgoTrader.Backtest].dbo._ChartLines
delete from [AlgoTrader.Backtest].dbo._Trades
-- ------------------------------------------------------------------------------------------------------------