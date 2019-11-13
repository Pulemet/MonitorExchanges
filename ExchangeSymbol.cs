using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crownwood.Magic.Win32;
using Deltix.EMS.API;
using Deltix.Timebase.Api.Messages;
using QuantOffice.Execution;

public class ExchangeSymbol
{
    private readonly long _waitInterval;
    private readonly PortfolioExecutor _portfolioExecutor;
    private readonly MonitoringExchange _exchange;
    private StrategyTimer CheckLastMessageTimer = null;
    private StrategyTimer PendingStatusTimer = null;
    private DateTime ActivateTime { get; set; }
    private Order PendingOrder { get; set; }
    private bool Notifications { get; set; }
    private bool Running { get; set; }
    private bool IsRunningPendingTimer { get; set; }
    private bool IsDeactivatedByExchange { get; set; }
    public bool IsSendOrders { get; set; }
    public string Name { get; set; }
    public LastOrderStatus LastOrderStatus { get; set; }
    public SpreadInfo SpreadInfo { get; set; }
    public DateTime LastMessageTime { get; private set; }

    public ExchangeSymbol(string symbol, MonitoringExchange exchange)
    {
        Name = symbol;
        _exchange = exchange;
        _portfolioExecutor = exchange.PortfolioExecutor;

        _waitInterval = _portfolioExecutor.WaitInterval;
        LastMessageTime = DateTime.MinValue;
        Notifications = false;
        IsSendOrders = false;
        Running = false;
        IsRunningPendingTimer = false;
        IsDeactivatedByExchange = false;
    }

    private void OnTimedEvent(Object source)
    {
        if (LastMessageTime != DateTime.MinValue)
        {
            if ((DateTime.Now - LastMessageTime).TotalSeconds > _waitInterval && IsActivateNotifications() &&
                !IsDeactivatedByExchange)
            {
                var textMessage = String.Format(
                    "Messages are not received within {0} seconds.\r\nLast message time: {1}.",
                    _waitInterval, Utils.TimeInString(LastMessageTime.ToUniversalTime()));
                var logMessage = Name + '/' + _exchange.Name + ": " + "Messages are not received within " +
                                 _waitInterval + " seconds.";
                var title = Name + '/' + _exchange.Name + ": data stopped receiving";
                _portfolioExecutor.SendMessage(title, textMessage, logMessage);

                DeactivateNotifications(false);
            }
        }
        else if ((DateTime.Now - ActivateTime).TotalSeconds > _waitInterval && !IsDeactivatedByExchange)
        {
            ActivateTime = DateTime.MaxValue;
            var textMessage = "Messages are not received within " + _waitInterval +
                              " seconds after activating instrument.";
            var logMessage = Name + '/' + _exchange.Name + ": " + textMessage;
            var title = Name + '/' + _exchange.Name + ": data are not received";
            _portfolioExecutor.SendMessage(title, textMessage, logMessage);

            DeactivateNotifications(false);
        }
    }

    private void OnCheckPendingStatusEvent(Object source)
    {
        string message = "Order status is " + PendingOrder.Status +
                         " within " + _portfolioExecutor.WaitPendingStatus + " seconds.";
        CheckPendingStatus(OrderStatus.PendingSubmit, message);
        CheckPendingStatus(OrderStatus.PendingCancel, message);
        IsRunningPendingTimer = false;
    }

    private void CheckPendingStatus(OrderStatus status, string textMessage)
    {
        if (PendingOrder.Status == status)
        {
            var logMessage = Name + '/' + _exchange.Name + ": " + textMessage;
            var title = Name + '/' + _exchange.Name + ": " + status + " order status";
            _portfolioExecutor.SendMessage(title, textMessage, logMessage);
        }
    }

    public void SetPendingStatus(Order order)
    {
        PendingOrder = order;
        if (order.Status == OrderStatus.PendingCancel && IsRunningPendingTimer)
        {
            PendingStatusTimer.Stop();
            PendingStatusTimer = _portfolioExecutor.Timers.CreateTimer(
                new TimeSpan(0, 0, 0, _portfolioExecutor.WaitPendingStatus), this.OnCheckPendingStatusEvent, null);
            PendingStatusTimer.Start();
            return;
        }

        if (!IsRunningPendingTimer)
        {
            IsRunningPendingTimer = true;
            PendingStatusTimer = _portfolioExecutor.Timers.CreateTimer(
                new TimeSpan(0, 0, 0, _portfolioExecutor.WaitPendingStatus), this.OnCheckPendingStatusEvent, null);
            PendingStatusTimer.Start();
        }
    }

    public void UpdateStatus(DateTime newTime, double bestBid, double bestAsk)
    {
        if (LastMessageTime != DateTime.MinValue &&
            (DateTime.Now - LastMessageTime).TotalSeconds > _portfolioExecutor.WaitInterval &&
            !IsDeactivatedByExchange)
        {
            var textMessage = String.Format(
                "Receiving messages is resumed after pause of {0} hours {1} mins {2} secs.",
                (DateTime.Now - LastMessageTime).Hours, (DateTime.Now - LastMessageTime).Minutes,
                (DateTime.Now - LastMessageTime).Seconds);
            var logMessage = Name + '/' + _exchange.Name + ": " + textMessage;
            var title = Name + '/' + _exchange.Name + ": data resumed";
            _portfolioExecutor.SendMessage(title, textMessage, logMessage);
        }

        LastMessageTime = newTime;
        ActivateNotifications(false);
        CheckCrossBidAsk(bestBid, bestAsk);
    }

    public void StartSendingOrders()
    {
        IsSendOrders = true;
        foreach (InstrumentExecutor instr in _portfolioExecutor.Slice)
        {
            if (Name == instr.Symbol.ToString() && IsActivateNotifications())
            {
                instr.workExchangesOrders.Add(_exchange.Name, _exchange);
                instr.StartSendingOrders();
            }
        }
    }

    public void StopSendingOrders(bool isStopForever, SentOrderStatus? status = null)
    {
        LastOrderStatus.Status = status ?? LastOrderStatus.Status;
        foreach (InstrumentExecutor instr in _portfolioExecutor.Slice)
        {
            if (Name == instr.Symbol.ToString())
                instr.workExchangesOrders.Remove(_exchange.Name);
            IsSendOrders = !isStopForever;
        }
    }

    public void ActivateNotifications(bool isActivateByExchange)
    {
        if (isActivateByExchange)
        {
            IsDeactivatedByExchange = false;
            LastMessageTime = DateTime.MinValue;
            ActivateTime = DateTime.Now;
        }

        if (Notifications || IsDeactivatedByExchange)
            return;
        Notifications = true;
        if (IsSendOrders)
            StartSendingOrders();
    }

    public void DeactivateNotifications(bool isDeactivateByExchange)
    {
        IsDeactivatedByExchange = isDeactivateByExchange;
        Notifications = false;
        StopSendingOrders(!IsSendOrders);
    }

    public bool IsActivateNotifications()
    {
        return Notifications;
    }

    public void Activate()
    {
        Running = true;
        foreach (InstrumentExecutor instr in _portfolioExecutor.Slice)
        {
            if (Name == instr.Symbol.ToString())
            {
                try
                {
                    instr.OrderBook.GetExchangeOrderBook(ExchangeCodec.CodeToLong(_exchange.Name))
                        .AddOnOrderBookChangedListener(instr.OnBookChanged);
                }
                catch (Exception ex)
                {
                    _portfolioExecutor.Log(instr.Symbol + "; Message:" + ex.Message + " " + ex.StackTrace);
                }
            }
        }

        CheckLastMessageTimer =
            _portfolioExecutor.Timers.CreateRecursiveTimer(new TimeSpan(0, 0, 0, 1), this.OnTimedEvent, null);
        CheckLastMessageTimer.Start();
        ActivateTime = DateTime.Now;
        SpreadInfo = new SpreadInfo();
        LastOrderStatus = new LastOrderStatus();
        ActivateNotifications(false);
    }

    public void Deactivate()
    {
        Notifications = false;
        LastMessageTime = DateTime.MinValue;
        CheckLastMessageTimer.Stop();

        foreach (InstrumentExecutor instr in _portfolioExecutor.Slice)
        {
            if (Name == instr.Symbol.ToString())
            {
                try
                {
                    instr.OrderBook.GetExchangeOrderBook(ExchangeCodec.CodeToLong(_exchange.Name))
                        .RemoveOnOrderBookChangedListener(instr.OnBookChanged);
                    instr.workExchangesOrders.Remove(_exchange.Name);
                }
                catch (Exception ex)
                {
                    _portfolioExecutor.Log(instr.Symbol + "; Message:" + ex.Message + " " + ex.StackTrace);
                }
            }
        }

        Running = false;
    }

    public bool IsActivate()
    {
        return Running;
    }

    #region Order Book (validation check)

    public bool CheckEmptySide(double bid, double ask)
    {
        if (Double.IsInfinity(bid) ^ Double.IsInfinity(ask))
        {
            if (_exchange.ExchangeParameters.IsAllowOneSide)
            {
                return false;
            }
            else if ((DateTime.Now - SpreadInfo.LastErrorTime).TotalSeconds >
                     _portfolioExecutor.IntervalValidationMessages
                     && SpreadInfo.SendMail)
            {
                if (_exchange.ExchangeParameters.MaxCrossInterval > 0)
                {
                    if (SpreadInfo.DetectedEmptySideTime == DateTime.MinValue)
                        SpreadInfo.DetectedEmptySideTime = DateTime.Now;
                    else if ((DateTime.Now - SpreadInfo.DetectedEmptySideTime).TotalSeconds >
                             _exchange.ExchangeParameters.MaxCrossInterval)
                    {
                        var textMessage = String.Format(
                            "Empty side of Order Book during {0} seconds!\r\nSymbol: {1}.\r\nBestBid: {2}.\r\nBestAsk: {3}.",
                            _exchange.ExchangeParameters.MaxCrossInterval, Name, bid, ask);
                        var logMessage = Name + "/" + _exchange.Name + ": Empty side of Order Book!";
                        var title = Name + "/" + _exchange.Name + ": Empty side";
                        _portfolioExecutor.SendMessage(title, textMessage, logMessage);

                        SpreadInfo.UpdateStatus(SpreadStatus.EMPTY_SIDE);
                    }

                    return false;
                }
                else
                {
                    var textMessage = String.Format(
                        "Empty side of Order Book is detected!\r\nSymbol: {0}.\r\nBestBid: {1}.\r\nBestAsk: {2}.",
                        Name, bid, ask);
                    var logMessage = Name + "/" + _exchange.Name + ": Empty side of Order Book!";
                    var title = Name + "/" + _exchange.Name + ": Empty side";
                    _portfolioExecutor.SendMessage(title, textMessage, logMessage);

                    SpreadInfo.UpdateStatus(SpreadStatus.EMPTY_SIDE);
                    return false;
                }

            }
        }
        else if (!SpreadInfo.SendMail && SpreadInfo.LastErrorTime != DateTime.MinValue &&
                 SpreadInfo.Status == SpreadStatus.EMPTY_SIDE)
        {
            var textMessage = String.Format(
                "Order Book sides are restored after {0:N3} seconds.\r\nSymbol: {1}.\r\nBestBid: {2}.\r\nBestAsk: {3}.",
                (DateTime.Now - SpreadInfo.DetectedEmptySideTime).TotalSeconds, Name, bid, ask);
            var logMessage = Name + "/" + _exchange.Name + ": Order Book is restored!";
            var title = Name + "/" + _exchange.Name + ": Order Book is restored";
            _portfolioExecutor.SendMessage(title, textMessage, logMessage);

            SpreadInfo.UpdateStatus(SpreadStatus.NORMAL);
            return true;
        }
        else if (SpreadInfo.Status == SpreadStatus.NORMAL)
        {
            SpreadInfo.DetectedEmptySideTime = DateTime.MinValue;
        }

        return true;
    }

    private void CheckCrossBidAsk(double bestBid, double bestAsk)
    {
        if (!CheckEmptySide(bestBid, bestAsk))
            return;

        if (!SpreadInfo.SendMail && bestBid < bestAsk && SpreadInfo.Status == SpreadStatus.CROSS)
        {
            var textMessage = String.Format(
                "Negative spread has gone after {0:N3} seconds!\r\nSymbol: {1}.\r\nBestBid: {2}.\r\nBestAsk: {3}.",
                (DateTime.Now - SpreadInfo.DetectedCrossTime).TotalSeconds, Name, bestBid, bestAsk);
            var logMessage = Name + "/" + _exchange.Name + ": Negative spread has gone.";
            var title = Name + "/" + _exchange.Name + ": Negative spread has gone";
            _portfolioExecutor.SendMessage(title, textMessage, logMessage);

            SpreadInfo.UpdateStatus(SpreadStatus.NORMAL);
            return;
        }

        if (bestBid >= bestAsk && SpreadInfo.SendMail &&
            (SpreadInfo.LastErrorTime == DateTime.MinValue ||
             (DateTime.Now - SpreadInfo.LastErrorTime).TotalSeconds >
             _portfolioExecutor.IntervalValidationMessages))
        {
            if (_exchange.ExchangeParameters.MaxCrossInterval > 0)
            {
                if (SpreadInfo.DetectedCrossTime == DateTime.MinValue)
                    SpreadInfo.DetectedCrossTime = DateTime.Now;
                else if ((DateTime.Now - SpreadInfo.DetectedCrossTime).TotalSeconds >
                         _exchange.ExchangeParameters.MaxCrossInterval)
                {
                    var textMessage = String.Format(
                        "Negative spread during {0} seconds!\r\nSymbol: {1}.\r\nBestBid: {2}.\r\nBestAsk: {3}.",
                        _exchange.ExchangeParameters.MaxCrossInterval, Name, bestBid, bestAsk);
                    var logMessage = Name + "/" + _exchange.Name + ": Negative spread during " +
                                     _exchange.ExchangeParameters.MaxCrossInterval + " seconds!";
                    var title = Name + "/" + _exchange.Name + ": Negative spread";
                    _portfolioExecutor.SendMessage(title, textMessage, logMessage);

                    SpreadInfo.UpdateStatus(SpreadStatus.CROSS);
                }
            }
            else
            {
                var textMessage = String.Format(
                    "Negative spread is detected!\r\nSymbol: {0}.\r\nBestBid: {1}.\r\nBestAsk: {2}.",
                    Name, bestBid, bestAsk);
                var logMessage = Name + "/" + _exchange.Name + ": Negative spread.";
                var title = Name + "/" + _exchange.Name + ": Negative spread";
                _portfolioExecutor.SendMessage(title, textMessage, logMessage);

                SpreadInfo.UpdateStatus(SpreadStatus.CROSS);
            }
        }
        else if (SpreadInfo.Status == SpreadStatus.NORMAL)
        {
            SpreadInfo.DetectedCrossTime = DateTime.MinValue;
        }
    }

    public bool IsUpdated(int timePeriod)
    {
        return IsActivateNotifications() && (DateTime.Now - LastMessageTime).TotalSeconds < timePeriod;
    }

    #endregion

}

#region Accessory classes

public enum SentOrderStatus
{
    NORMAL,
    REJECT,
    NOT_RESPONDING
}

public enum SpreadStatus
{
    NORMAL,
    CROSS,
    EMPTY_SIDE
}

public sealed class LastOrderStatus
{
    public DateTime LastFilledTime { get; set; }
    public int CountFilledOrders { get; set; }
    public long LastSentOrderId { get; set; }
    public SentOrderStatus Status { get; set; }

    public LastOrderStatus()
    {
        LastFilledTime = DateTime.MinValue;
        CountFilledOrders = 0;
        LastSentOrderId = 0;
        Status = SentOrderStatus.NORMAL;
    }
}

public sealed class SpreadInfo
{
    public DateTime LastErrorTime { get; set; }
    public DateTime DetectedCrossTime { get; set; }
    public DateTime DetectedEmptySideTime { get; set; }
    public SpreadStatus Status { get; set; }
    public bool SendMail { get; set; }

    public SpreadInfo()
    {
        LastErrorTime = DateTime.MinValue;
        DetectedCrossTime = DateTime.MinValue;
        DetectedEmptySideTime = DateTime.MinValue;
        Status = SpreadStatus.NORMAL;
        SendMail = true;
    }

    public void UpdateStatus(SpreadStatus status)
    {
        Status = status;
        if (status == SpreadStatus.NORMAL)
        {
            SendMail = true;
            return;
        }

        LastErrorTime = DateTime.Now;
        SendMail = false;
    }
}

#endregion