﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IdxEditor.Rendering.Attributes;
using QuantOffice.Execution;

public class DataFeedValidator
{
    private readonly PortfolioExecutor PortfolioExecutor;
    public List<MatchExchange> ListMatchExchanges { get; set; }

    public DataFeedValidator(PortfolioExecutor portfolioExecutor)
    {
        PortfolioExecutor = portfolioExecutor;
        ListMatchExchanges = new List<MatchExchange>();
        foreach (var matchExchange in PortfolioExecutor.ListMatchExchanges)
        {
            ListMatchExchanges.Add(new MatchExchange(matchExchange, portfolioExecutor));
        }
    }

    public void UpdateIndicators(double price, string exchange, string symbol)
    {
        foreach (var matchExchange in ListMatchExchanges)
        {
            if (matchExchange.Symbol == symbol)
            {
                matchExchange.GetExchange(exchange)?.UpdatePrice(price);
            }
        }
    }

    public MatchExchange FindExchange(MatchExchangesParameters matchExchangeParameters)
    {
        foreach (var matchExchange in ListMatchExchanges)
        {
            if (matchExchange.IsEqual(matchExchangeParameters))
                return matchExchange;
        }

        return null;
    }

    public void AddMatchExchange(MatchExchangesParameters matchExchange)
    {
        ListMatchExchanges.Add(new MatchExchange(matchExchange, PortfolioExecutor));
    }

    public void RemoveMatchExchange(MatchExchange matchExchange)
    {
        ListMatchExchanges.Remove(matchExchange);
    }
}


[Serializable]
public sealed class MatchExchangesParameters : Parameters<MatchExchangesParameters>
{
    #region Variables

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "First Exchange")]
    public string FirstExchange;

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "Second Exchange")]
    public string SecondExchange;

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "Symbols")]
    public string Symbols;

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "Time Period for SMA (secs)")]
    public int TimePeriod;

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "Threshold")]
    public double Threshold;

    #endregion

    #region BuildingBlocks

    public MatchExchangesParameters(string firstExchange, string secondExchange, string symbol, int timePeriod, double threshold)
    {
        FirstExchange = firstExchange;
        SecondExchange = secondExchange;
        Symbols = symbol;
        TimePeriod = timePeriod;
        Threshold = threshold;
    }

    public MatchExchangesParameters()
    {
        FirstExchange = "";
        SecondExchange = "";
        Symbols = "";
        TimePeriod = 10;
        Threshold = 0.0003;
    }

    internal override void CopyDataFrom(MatchExchangesParameters source)
    {
        if (source == null)
        {
            throw new ArgumentNullException("source");
        }

        FirstExchange = source.FirstExchange;
        SecondExchange = source.SecondExchange;
        Symbols = source.Symbols;
        TimePeriod = source.TimePeriod;
        Threshold = source.Threshold;
    }

    internal override string ToString(string prefix)
    {
        return String.Format(
            "First Exchange: {0}; Second Exchange: {1}, Symbol: {2}", FirstExchange,
            SecondExchange, Symbols);
    }

    #endregion
}
