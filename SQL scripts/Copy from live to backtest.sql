
drop table [AlgoTrader.Backtest].dbo.BTCUSDT
select * into [AlgoTrader.Backtest].dbo.BTCUSDT	   from [AlgoTrader.Binance].dbo.BTCUSDT  

drop table [AlgoTrader.Backtest].dbo._ChartLines
select * into [AlgoTrader.Backtest].dbo._ChartLines	   from [AlgoTrader.Binance].dbo._ChartLines

drop table [AlgoTrader.Backtest].dbo._Trades
select * into [AlgoTrader.Backtest].dbo._Trades	   from [AlgoTrader.Binance].dbo._Trades  

--delete from [AlgoTrader.Backtest].dbo._ChartLines
--delete from [AlgoTrader.Backtest].dbo._Trades
-- ------------------------------------------------------------------------------------------------------------

-- select top 1 * from [AlgoTrader.Binance].dbo.BTCUSDT order by Timestamp desc
-- delete from [AlgoTrader.Binance].dbo.BTCUSDT where Timestamp < '2026-09-16 09:30'