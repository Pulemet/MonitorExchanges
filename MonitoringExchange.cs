using System;
using System.Collections.Generic;
using System.Linq;
using Crownwood.Magic.Win32;
using Deltix.EMS.API;
using Deltix.Timebase.Api.Messages;
using IdxEditor.Rendering;
using IdxEditor.Rendering.Attributes;
using QuantOffice.Execution;

public class MonitoringExchange
{
    private StrategyTimer CheckLastMessageTimer = null;

    // Activates when data for exchange stopped receiving
    private bool IsDeactivated { get; set; }
    private DateTime ActivateTime;
    private readonly long _waitInterval;
    public readonly PortfolioExecutor PortfolioExecutor;
    public string Name { get; set; }
    public bool Running { get; set; }
    public ExchangeParameters ExchangeParameters { get; set; }
    public Dictionary<string, ExchangeSymbol> ExchangeSymbols;

    public MonitoringExchange(ExchangeParameters parameters, PortfolioExecutor portfolioExecutor)
    {
        PortfolioExecutor = portfolioExecutor;

        Running = false;
        IsDeactivated = false;
        Name = parameters.Name;
        ExchangeParameters = parameters;
        ExchangeSymbols = new Dictionary<string, ExchangeSymbol>();
        // Time of waiting data for Exchange is set to 80% on time of waiting data for all Instruments
        _waitInterval = (long) (PortfolioExecutor.WaitInterval * 0.8);
    }

    private void OnTimedEvent(Object source)
    {
        if (IsDeactivated)
            return;
        DateTime lastMessageTime = DateTime.MinValue;
        string lastSymbol = "";
        foreach (var symbol in ExchangeSymbols.Values)
        {
            if (symbol.LastMessageTime > lastMessageTime)
            {
                lastMessageTime = symbol.LastMessageTime;
                lastSymbol = symbol.Name;
            }
        }

        if (lastMessageTime != DateTime.MinValue && (DateTime.Now - lastMessageTime).TotalSeconds > _waitInterval)
        {
            Deactivate();
            var textMessage = String.Format(
                "Messages are not received within {0} seconds.\r\nLast message time: {1} on {2} Instrument.",
                _waitInterval, Utils.TimeInString(lastMessageTime.ToUniversalTime()), lastSymbol);
            var logMessage = Name + " exchange: " + "Messages are not received within " + _waitInterval +
                             " seconds.";
            var title = Name + ": data for exchange stopped receiving";
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);
        }
        else if (lastMessageTime == DateTime.MinValue && (DateTime.Now - ActivateTime).TotalSeconds > _waitInterval)
        {
            Deactivate();
            var textMessage = "Messages are not received within " + _waitInterval +
                              " seconds after activating exchange.";
            var logMessage = Name + ": " + textMessage;
            var title = Name + ": data for exchange are not received";
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);
        }
    }

    private void Deactivate()
    {
        IsDeactivated = true;
        foreach (var symbol in ExchangeSymbols.Values)
        {
            symbol.DeactivateNotifications(true);
        }
    }

    public void Start()
    {
        if (Running)
            return;
        Running = true;
        ActivateTime = DateTime.Now;
        CheckLastMessageTimer =
            PortfolioExecutor.Timers.CreateRecursiveTimer(new TimeSpan(0, 0, 0, 1), this.OnTimedEvent, null);
        CheckLastMessageTimer.Start();
        PortfolioExecutor.SendLog("Activate " + Name + " exchange.");
    }

    public void Stop()
    {
        if (!Running)
            return;
        CheckLastMessageTimer.Stop();
        foreach (var symbol in ExchangeSymbols.Values)
        {
            symbol.Deactivate();
        }

        PortfolioExecutor.SendLog("Deactivate: " + Name + " exchange.");
        Running = false;
    }

    public void UpdateStatus(DateTime newTime, string symbol, double bestBid, double bestAsk, ref double midPrice)
    {
        if (IsDeactivated)
        {
            IsDeactivated = false;
            DateTime lastMessageTime = DateTime.MinValue;
            foreach (var exchangeSymbol in ExchangeSymbols.Values)
            {
                if (exchangeSymbol.LastMessageTime > lastMessageTime)
                {
                    lastMessageTime = exchangeSymbol.LastMessageTime;
                }

                exchangeSymbol.ActivateNotifications(true);
            }

            if (lastMessageTime == DateTime.MinValue)
                lastMessageTime = ActivateTime;
            var textMessage = String.Format(
                "Receiving messages is resumed after pause of {0} hours {1} mins {2} secs.",
                (DateTime.Now - lastMessageTime).Hours, (DateTime.Now - lastMessageTime).Minutes,
                (DateTime.Now - lastMessageTime).Seconds);
            var logMessage = Name + " exchange: " + textMessage;
            var title = Name + ": data for exchange resumed";
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);
        }

        ExchangeSymbols[symbol].UpdateStatus(newTime, bestBid, bestAsk, ref midPrice);
    }

    public void RemoveSymbolsForOrders(string symbol, SentOrderStatus status)
    {
        ExchangeSymbols[symbol].StopSendingOrders(true, status);
        ExchangeParameters.SymbolsForOrders =
            Utils.GetLineFromList(ExchangeSymbols.Values.Where(e => e.IsSendOrders).Select(e => e.Name).ToList());
    }

    public void UpdateSymbols()
    {
        if (!Running)
        {
            foreach (var symbol in ExchangeSymbols.Select(s => s.Value))
            {
                symbol.Deactivate();
            }

            return;
        }

        var paramSymbols = Utils.GetListFromLine(ExchangeParameters.Symbols);

        var addSymbols = paramSymbols
            .Except(ExchangeSymbols.Select(s => s.Value).Where(s => s.IsActivate()).Select(s => s.Name)).ToList();
        var removeSymbols = ExchangeSymbols.Keys.Except(paramSymbols).ToList();

        foreach (var symbol in removeSymbols)
        {
            ExchangeSymbols[symbol].Deactivate();
            ExchangeSymbols.Remove(symbol);
            PortfolioExecutor.RemoveSymbol(symbol);
        }

        foreach (var symbol in addSymbols)
        {
            if (!ExchangeSymbols.ContainsKey(symbol))
            {
                PortfolioExecutor.AddSymbol(symbol);
                ExchangeSymbols.Add(symbol, new ExchangeSymbol(symbol, this));
            }

            ExchangeSymbols[symbol].Activate();
        }
    }

    public void UpdateSymbolsForOrders()
    {
        if (Running == false)
        {
            return;
        }

        var paramSymbols = Utils.GetListFromLine(ExchangeParameters.SymbolsForOrders)
            .Intersect(ExchangeSymbols.Keys).ToList();
        var addSymbols = paramSymbols
            .Except(ExchangeSymbols.Values.Where(s => s.IsSendOrders).Select(e => e.Name).ToList()).ToList();
        var removeSymbols = ExchangeSymbols.Values.Where(s => s.IsSendOrders).Select(s => s.Name)
            .Except(paramSymbols).ToList();

        foreach (var symbol in removeSymbols)
        {
            ExchangeSymbols[symbol].StopSendingOrders(true);
        }

        foreach (var symbol in addSymbols)
        {
            ExchangeSymbols[symbol].StartSendingOrders();
        }

        ExchangeParameters.SymbolsForOrders =
            Utils.GetLineFromList(ExchangeSymbols.Values.Where(s => s.IsSendOrders).Select(s => s.Name).ToList());
    }

    #region Print

    public string GetLastMessageTime()
    {
        DateTime lastTime = DateTime.MinValue;
        foreach (var symbol in ExchangeSymbols.Values)
            if (symbol.LastMessageTime > lastTime)
                lastTime = symbol.LastMessageTime;
        if (lastTime == DateTime.MinValue)
            return "";
        else if ((DateTime.Now - lastTime).TotalSeconds > PortfolioExecutor.WaitInterval)
            return "Data on exchange have ceased";
        return Utils.TimeInString(lastTime.ToUniversalTime());
    }

    public string GetFilledOrdersOnSymbols()
    {
        string result = "";
        foreach (var symbol in ExchangeSymbols.Values)
        {
            if (symbol.LastOrderStatus.Status == SentOrderStatus.NORMAL &&
                symbol.LastOrderStatus.CountFilledOrders > 0)
                result += symbol.Name + ": " + symbol.LastOrderStatus.CountFilledOrders + "; ";
            if (symbol.LastOrderStatus.Status == SentOrderStatus.REJECT)
                result += symbol.Name + ": REJECT; ";
            if (symbol.LastOrderStatus.Status == SentOrderStatus.NOT_RESPONDING)
                result += symbol.Name + ": NOT RESPONDING; ";
        }

        return result == "" ? "" : result.Substring(0, result.Length - 2);
    }

    public string GetOrderBookStatus()
    {
        string result = "";
        foreach (var symbol in ExchangeSymbols)
        {
            if (symbol.Value.SpreadInfo.Status == SpreadStatus.CROSS)
                result += symbol.Key + ": Negative spread at " + symbol.Value.SpreadInfo.LastErrorTime + "; ";
            if (symbol.Value.SpreadInfo.Status == SpreadStatus.EMPTY_SIDE)
                result += symbol.Key + ": Empty side at " + symbol.Value.SpreadInfo.LastErrorTime + "; ";
        }

        return result == "" ? "" : result.Substring(0, result.Length - 2);
    }

    #endregion

    #region Orders statuses

    public void UpdateOrderStatus(string symbol, long id)
    {
        ExchangeSymbols[symbol].LastOrderStatus.LastFilledTime = DateTime.UtcNow;
        ExchangeSymbols[symbol].LastOrderStatus.CountFilledOrders++;
        ExchangeSymbols[symbol].LastOrderStatus.LastSentOrderId =
            ExchangeSymbols[symbol].LastOrderStatus.LastSentOrderId == id
                ? 0
                : ExchangeSymbols[symbol].LastOrderStatus.LastSentOrderId;
    }

    public bool CheckFillLastOrder(string symbol, DateTime currentTime)
    {
        long orderId = ExchangeSymbols[symbol].LastOrderStatus.LastSentOrderId;
        if (orderId != 0)
        {
            Order order = PortfolioExecutor.orderProcessor.GetOrderData(orderId);
            var textMessage = String.Format("Order id: {0}.\r\nSubmission Time: {1}.\r\nCurrentTime: {2}.",
                orderId, order.SubmissionTime, currentTime);
            var logMessage = Name + "/" + symbol + ": Order is not responding; Id = " + orderId + ".";
            var title = Name + "/" + symbol + ": Order is not responding";
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);
            return false;
        }

        return true;
    }

    public bool IsReadyToMatch(string symbol, int timePeriod)
    {
        return !IsDeactivated && ExchangeSymbols[symbol].IsUpdated(timePeriod);
    }

    #endregion
}

[Serializable]
public sealed class ExchangeParameters : Parameters<ExchangeParameters>
{
    #region Variables

    [EditableInRuntime] [DisplayInfo(DisplayName = "Activate?")]
    public bool IsActivate;

    [EditableInRuntime] [DisplayInfo(DisplayName = "Name")]
    public string Name;

    [EditableInRuntime] [DisplayInfo(DisplayName = "Input the list of Symbols")]
    public string Symbols;

    [EditableInRuntime] [DisplayInfo(DisplayName = "Input the list of Symbols for sending orders:")]
    public string SymbolsForOrders;

    [EditableInRuntime] [DisplayInfo(DisplayName = "Allow one side of book?")]
    public bool IsAllowOneSide;

    [EditableInRuntime] [DisplayInfo(DisplayName = "Interval of duration of cross (sec)")]
    public int MaxCrossInterval;

    #endregion

    #region BuildingBlocks

    public ExchangeParameters(string name, bool isActivate, string symbols, string symbolsForOrders, int maxCross,
        bool isOneSide)
    {
        Name = name;
        IsActivate = isActivate;
        Symbols = symbols;
        SymbolsForOrders = symbolsForOrders;
        MaxCrossInterval = maxCross;
        IsAllowOneSide = isOneSide;
    }

    public ExchangeParameters(string name, bool isActivate, string symbols, string symbolsForOrders)
    {
        Name = name;
        IsActivate = isActivate;
        Symbols = symbols;
        SymbolsForOrders = symbolsForOrders;
        IsAllowOneSide = false;
        MaxCrossInterval = 0;
    }

    public ExchangeParameters()
    {
        Name = "";
        IsActivate = false;
        Symbols = "";
        SymbolsForOrders = "";
        IsAllowOneSide = false;
        MaxCrossInterval = 0;
    }

    internal override void CopyDataFrom(ExchangeParameters source)
    {
        if (source == null)
        {
            throw new ArgumentNullException("source");
        }

        Name = source.Name;
        IsActivate = source.IsActivate;
        Symbols = source.Symbols;
        SymbolsForOrders = source.SymbolsForOrders;
    }

    internal override string ToString(string prefix)
    {
        return String.Format(
            "Exchange: {0}; Activate? {1}, List of Symbols: {2}, List of Symbols for sending orders: {3}", Name,
            IsActivate, Symbols, SymbolsForOrders);
    }

    #endregion
}
