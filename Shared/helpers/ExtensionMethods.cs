using AlgoTrader.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AlgoTrader.Shared
{
    public static class List
    {
        public static T GetPanelByType<T>(this List<T> panels, Type panelType)
        {
            //do this so the base TradeEngine will be returned from n-depth derived class
            foreach (var panel in panels)
            {
                var ret = GetBaseType(panel.GetType());
                if (ret)
                {
                    return panel;
                }  
            }

            return default;

            bool GetBaseType(Type t)
            {
                if (t == panelType)
                {
                    return true;
                }
                else if (t == null)
                {
                    return false;
                }
                else
                {
                    return GetBaseType(t.BaseType);
                }
            }
        }

        //see notes in UIDrawingBase about why the Trade is attached to the Line, and not vice-versa
        public static _ChartLine GetLineForTrade(this List<_ChartLine> lines, _Trade trade)
        {
            //in backtesting, the line trade does not reference the order for some reason, so have switched to Id comparison instead
            return trade != null
                        ? lines.Where(x => x.TradeId == trade.Id).FirstOrDefault()
                        : null;
        }

        public static _Trade GetTradeForTradeReference(this List<_Trade> trades, TradeAction action, string tradeReference)
        {
            return trades.FirstOrDefault(x =>
                            x.Action == action &&
                            x.TradeReference == tradeReference);
        }

        public static string GetEnumDisplayName(this Enum enumType)
        {
            return enumType.GetType().GetMember(enumType.ToString())
                           .First()
                           .GetCustomAttribute<DisplayAttribute>()
                           .Name;
        }
    }
}
