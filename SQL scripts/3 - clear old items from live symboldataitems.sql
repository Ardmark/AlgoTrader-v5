/* Timestamp format is yyyy-mm-dd */
delete from [AlgoTrader.Binance].dbo.BTCUSDT
		 where Timestamp < '2026-9-12 16:30'
