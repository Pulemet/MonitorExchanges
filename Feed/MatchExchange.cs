using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crownwood.Magic.Win32;
using QuantOffice.Execution;
using FinAnalysis.TA;


public class MatchExchange
{
    private const int _checkPeriod = 1000;      // in milliseconds, to check through period, and not every update.
    private readonly double Threshold = 0.0003; 
    private readonly int TimePeriod = 300;
    private readonly PortfolioExecutor PortfolioExecutor;
    private DateTime StartTime;
    private DateTime CheckTime;
    private Sma SmaIndicator;
    private DateTime LastUpdateTime;

    public string Exchange { get; set; }
    public string Symbol { get; set; }
    public DateTime UnMatchTime { get; set; }
    public MatchExchange ExchangeForMatch { get; set; }
    public bool IsErrorSent { get; set; }

    // ----------------------------------------------------------------------------------------------------
    //public double MaxDiff = 0;
    //public int Counter = 0;
    // ----------------------------------------------------------------------------------------------------

    public MatchExchange(MatchExchangesParameters parameters, PortfolioExecutor portfolioExecutor)
    {
        Exchange = parameters.FirstExchange;
        Symbol = parameters.Symbol;
        TimePeriod = parameters.TimePeriod;
        Threshold = parameters.Threshold;
        PortfolioExecutor = portfolioExecutor;
        InitializeMainParams();
        ExchangeForMatch = new MatchExchange(parameters.SecondExchange, Symbol, portfolioExecutor, this);
    }

    public MatchExchange(string exchange, string symbol, PortfolioExecutor portfolioExecutor, MatchExchange exchangeForMatch)
    {
        Exchange = exchange;
        Symbol = symbol;
        TimePeriod = exchangeForMatch.TimePeriod;
        Threshold = exchangeForMatch.Threshold;
        PortfolioExecutor = portfolioExecutor;
        InitializeMainParams();
        ExchangeForMatch = exchangeForMatch;
    }

    public void InitializeSmaIndicator()
    {
        SmaIndicator = new Sma(new TimeSpan(0, 0, 0, TimePeriod), false);
        SmaIndicator.ResamplingFactor = 1;
        SmaIndicator.ResamplingMethod = FinAnalysis.Base.ResamplingType.LastElement;
        SmaIndicator.ValueDelayPointPeriod = 0;
        SmaIndicator.ValueDelayTimePeriod = TimeSpan.Parse("00:00:00");
    }

    public void InitializeMainParams()
    {
        UnMatchTime = DateTime.MinValue;
        IsErrorSent = false;
        StartTime = DateTime.Now;
        CheckTime = DateTime.Now;
        LastUpdateTime = StartTime.AddSeconds(-TimePeriod);
        InitializeSmaIndicator();
    }

    public void UpdatePrice(double price)
    {
        LastUpdateTime = DateTime.Now;
        SmaIndicator.Add(price, LastUpdateTime);

        if ((DateTime.Now - CheckTime).TotalMilliseconds <= _checkPeriod)
            return;

        CheckTime = DateTime.Now;

        // in case of one exchange is not updated
        if ((DateTime.Now - ExchangeForMatch.LastUpdateTime).TotalSeconds > TimePeriod)
        {
            ExchangeForMatch.StartTime = DateTime.Now;
            return;
        }  

        double percentDiff = Math.Abs(SmaIndicator.SMA - ExchangeForMatch.SmaIndicator.SMA) / SmaIndicator.SMA;

        // ----------------------------------------------------------------------------------------------------
        /*MaxDiff = percentDiff > MaxDiff ? percentDiff : MaxDiff;
        if(Counter++ == 10)
        {
            PortfolioExecutor.SendLog(String.Format(
                "Exchange: {0}; Symbol: {1}; SMA: {2}; Compare with {3}: {4}; Difference = {5:F8}; Max difference: {6:F8}",
                Exchange, Symbol, SmaIndicator.SMA, ExchangeForMatch.Exchange, ExchangeForMatch.SmaIndicator.SMA,
                percentDiff, Math.Max(MaxDiff, ExchangeForMatch.MaxDiff)));
            Counter = 0;
        }*/
        // ----------------------------------------------------------------------------------------------------

        if ((DateTime.Now - StartTime).TotalSeconds >= TimePeriod && !IsSentErrorStatus() && percentDiff > Threshold)
        {
            //PortfolioExecutor.SendLog(String.Format("Sending error! Exchange: {0}; Symbol: {1}; SMA: {2}; Compare with {3}: {4}",
            //Exchange, Symbol, SmaIndicator.SMA, ExchangeForMatch.Exchange, ExchangeForMatch.SmaIndicator.SMA));
            SetSentErrorStatus(true);
            if (!PortfolioExecutor.MonitoringExchanges.FirstOrDefault(e => e.Name == Exchange).IsReadyToMatch(Symbol, TimePeriod) ||
                !PortfolioExecutor.MonitoringExchanges.FirstOrDefault(e => e.Name == ExchangeForMatch.Exchange).IsReadyToMatch(Symbol, TimePeriod))
            {
                return;
            }
            var textMessage = String.Format("{0}-{1} exchanges {2}: data does not match!\r\nSMA for {0} = {3:F8}\r\nSMA for {1} = {4:F8}\r\nDifference = {5:F8}:",
                Exchange, ExchangeForMatch.Exchange, Symbol, SmaIndicator.SMA, ExchangeForMatch.SmaIndicator.SMA, percentDiff);
            var logMessage = String.Format("{0}-{1} exchanges {2}: data does not match!", Exchange, ExchangeForMatch.Exchange, Symbol);
            var title = String.Format("{0}-{1} exchanges {2}: data does not match", Exchange, ExchangeForMatch.Exchange, Symbol);
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);
            SetUnMatchTime(DateTime.Now);
        }
        else if (UnMatchTime != DateTime.MinValue && (DateTime.Now - UnMatchTime).TotalSeconds >= PortfolioExecutor.IntervalValidationMessages)
        {
            SetUnMatchTime(DateTime.MinValue);
            SetSentErrorStatus(false);
        }
    }

    public void SetUnMatchTime(DateTime time)
    {
        UnMatchTime = time;
        ExchangeForMatch.UnMatchTime = time;
    }

    public void SetSentErrorStatus(bool status)
    {
        IsErrorSent = status;
        ExchangeForMatch.IsErrorSent = status;
    }

    public bool IsSentErrorStatus()
    {
        return IsErrorSent && ExchangeForMatch.IsErrorSent;
    }

    public MatchExchange GetExchange(string name)
    {
        if (Exchange == name)
            return this;
        else if (ExchangeForMatch.Exchange == name)
            return ExchangeForMatch;
        return null;
    }
}
