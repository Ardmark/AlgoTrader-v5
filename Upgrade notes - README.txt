This is DotNet 8.0, as required by the exchange rate API, although another could probably be found, or this one could probably be rebuilt for 4.6.1, this upgrade needs doing anyway.
Using OpenExchangeRates, have set up a login and an API key. Will need to save the exchange rate at time of trade exit.

Must set Build Action in App.xaml to 'Application Definition'
must set Resources build action to 'Resource'
Copy App.config contents to new App.config file (already there)
Changes:
Modified assembyinfo.cs to remove duplicate attributes (like [assembly: AssemblyVersion("1.0.0.0")], [assembly: AssemblyFileVersion("1.0.0.0")]) - these things are now in Properties and will throw compilation errors.

Modified Backtest.csproj to enable Windows Forms -     <UseWindowsForms>True</UseWindowsForms> (it is false by default)

