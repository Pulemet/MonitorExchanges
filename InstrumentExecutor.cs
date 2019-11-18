

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using Deltix.EMS.API;
using Deltix.EMS.Coordinator;
using Deltix.StrategyServer.Api.Channels.Attributes;
using Deltix.Timebase.Api;
using Deltix.Timebase.Api.Messages.Universal;
using Deltix.Timebase.Api.Schema;
using IdxEditor.Rendering.Attributes;
using QuantOffice.Calendar;
using QuantOffice.CodeGenerator;
using QuantOffice.Connection;
using QuantOffice.CustomMessages;
using QuantOffice.Data;
using QuantOffice.HistoryService.Data;
using QuantOffice.StrategyRunner;
using QuantOffice.SyntheticInstruments;
using QuantOffice.Execution;
using QuantOffice.Execution.Utils;
using QuantOffice.MarketDataProvider;
using QuantOffice.MarketDataProvider.DataCache;
using QuantOffice.Options;
using QuantOffice.Reporting.Custom;
using QuantOffice.SyntheticInstruments.MarketRunner;
using QuantOffice.SyntheticInstruments.MarketRunner.CustomMessages;
using QuantOffice.Utils;
using QuantOffice.Utils.Collections;
using QuantOffice.Utils.ObjectInspector;
using RTMath.Containers;
using UhfConnector.ServerSideConnector;
using Basket = QuantOffice.SyntheticInstruments.CustomView.Basket;
using Bar = QuantOffice.Data.IBarInfo;
using BestBidOfferMessage = QuantOffice.Data.BestBidOfferMessage;
using DacMessage = QuantOffice.Data.DacMessage;
using Exchange = QuantOffice.Execution.Exchange;
using ExecutorBase = QuantOffice.Execution.InstrumentExecutorBase;
using IBestBidOfferMessageInfo = QuantOffice.Data.IBestBidOfferMessageInfo;
using IDacMessageInfo = QuantOffice.Data.IDacMessageInfo;
using ILevel2MessageInfo = QuantOffice.Data.ILevel2MessageInfo;
using IL2SnapshotMessageInfo = QuantOffice.Data.IL2SnapshotMessageInfo;
using IL2MessageInfo = QuantOffice.Data.IL2MessageInfo;
using IMarketMessageInfo = QuantOffice.Data.IMarketMessageInfo;
using IPackageHeaderInfo = QuantOffice.Data.IPackageHeaderInfo;
using IPacketEndMessageInfo = QuantOffice.Data.IPacketEndMessageInfo;
using ITradeMessageInfo = QuantOffice.Data.ITradeMessageInfo;
using L2Message = QuantOffice.Data.L2Message;
using L2SnapshotMessage = QuantOffice.Data.L2SnapshotMessage;
using Level2Message = QuantOffice.Data.Level2Message;
using MarketMessage = QuantOffice.Data.MarketMessage;
using PackageHeader = QuantOffice.Data.PackageHeader;
using PacketEndMessage = QuantOffice.Data.PacketEndMessage;
using Path = System.IO.Path;
using TradeMessage = QuantOffice.Data.TradeMessage;
using TimeInterval = QuantOffice.Execution.Utils.TimeInterval;
using Deltix.Timebase.Api.Messages;
using QuantOffice.L2;


/// <summary>
/// This class contains instrument level event processing logic.
/// </summary>
public partial class InstrumentExecutor : InstrumentExecutorBase
{
    #region CustomProperties

    #endregion

    #region LocalVariables

    internal OrderProcessor OrderExecutor;
    internal OrderBook OrderBook;

    private OrderSide SendSide = OrderSide.Buy;
    private StrategyTimer SendOrderTimer = null;
    private bool IsStopSendOrderTimer;

    public Dictionary<string, MonitoringExchange> workExchangesOrders;

    #endregion

    #region BuildingBlocks

    private void ChangeSide()
    {
        Position pos = OrderExecutor.GetPositionData(Symbol);
        if (SendSide == OrderSide.Buy && (pos == null || !Utils.CompareDouble(pos.Quantity, 0)))
        {
            SendSide = OrderSide.Sell;
        }
        else if (SendSide == OrderSide.Sell && (pos == null || !Utils.CompareDouble(pos.Quantity, 0)))
        {
            SendSide = OrderSide.Buy;
        }
    }

    private void OnSendOrder(Object source)
    {
        if (workExchangesOrders.Count == 0)
        {
            StopSendingOrders();
            return;
        }

        List<string> removeExchanges = new List<string>();

        foreach (var exchange in workExchangesOrders)
        {
            if (!exchange.Value.CheckFillLastOrder(Symbol.ToString(), CurrentTime))
            {
                removeExchanges.Add(exchange.Key);
                continue;
            }

            if(exchange.Value.ExchangeSymbols[Symbol].LastMessageTime == DateTime.MinValue)
                continue;
                
            ChangeSide();
            long currentId = OrderExecutor.GetNextValidOrderId();
            double orderSize = 0.1;
            MarketOrder order = new MarketOrder(currentId, Symbol, orderSize, SendSide, OrderTimeInForce.IOC);
            order.Exchange = exchange.Key;
            OrderExecutor.SendOrder(order);
        }

        if(removeExchanges.Count > 0)
            removeExchanges.ForEach(e => RemoveExchange(e, SentOrderStatus.NOT_RESPONDING));
    }

    private void RemoveExchange(string exchange, SentOrderStatus status)
    {
        FindMonitor(exchange).RemoveSymbolsForOrders(Symbol.ToString(), status);
    }

    public void OnBookChanged(BaseOrderBook orderBook)
    {
        if (Double.IsInfinity(orderBook.BestBid()) && Double.IsInfinity(orderBook.BestAsk()))
            return;
        var exchangeId = ((IExchangeOrderBook)orderBook).ExchangeId;
        string exchange = ExchangeCodec.LongToCode(exchangeId);
        var monitor = FindMonitor(exchange);
        double midPrice = 0;
        monitor.UpdateStatus(DateTime.Now, Symbol, orderBook.BestBid(), orderBook.BestAsk(), ref midPrice);
        if (Math.Abs(midPrice) > Utils.Delta)
            PortfolioExecutor.DataFeedValidator.UpdateIndicators(midPrice, exchange, Symbol);
    }

    internal void OnUpdateInputParameters(EditableInstrumentInputParameters inputParameters)
    {
    	UpdateInputParameters(inputParameters);
    }

    private MonitoringExchange FindMonitor(string exchange)
    {
        return PortfolioExecutor.MonitoringExchanges.FirstOrDefault(m => m.Name == exchange);
    }

    public void OnOrderStatus(object sender, OrderStatusEventArgs e)
    {
        OrderStatusInfo info = e.OrderStatusInfo;
        Order order = OrderExecutor.GetOrderData(info.OrderId);

        if (info.OrderStatus == OrderStatus.Rejected)
        {
            OrderStatusRejectedInfo rejectedInfo = (OrderStatusRejectedInfo)info;
            var textMessage = String.Format("Symbol: {0}.\r\nOrder Id: {1}.\r\nStatus: {2}.\r\nSide: {3}.\r\nReason: {4}.",
                Symbol, order.Id, order.Status, order.Side, rejectedInfo.RejectionReason);
            var logMessage = order.Exchange + "/" + Symbol + ": Order (" + order.Id + ") is Rejected.";
            var title = "Rejected order on " + order.Exchange + " exchange";
            PortfolioExecutor.SendMessage(title, textMessage, logMessage);

            RemoveExchange(order.Exchange, SentOrderStatus.REJECT);
        }
        
        if (info.OrderStatus == OrderStatus.Filled)
        {
            workExchangesOrders[order.Exchange].UpdateOrderStatus(Symbol.ToString(), order.Id);
        }

        if (info.OrderStatus == OrderStatus.PendingSubmit || info.OrderStatus == OrderStatus.PendingCancel)
        {
            workExchangesOrders[order.Exchange].ExchangeSymbols[Symbol].SetPendingStatus(order);
        }
    }

    public void StartSendingOrders()
    {
        if (IsStopSendOrderTimer)
        {
            IsStopSendOrderTimer = false;
            SendOrderTimer = Timers.CreateRecursiveTimer(new TimeSpan(0, 0, 0, PortfolioExecutor.SendOrderInterval),
                OnSendOrder, null);
            SendOrderTimer.Start();
        }   
    }

    public void StopSendingOrders()
    {
        if (!IsStopSendOrderTimer)
        {
            SendOrderTimer.Stop();
            IsStopSendOrderTimer = true;
        }     
    }

    #endregion

    #region Events

    public override void OnInit()
    {
        workExchangesOrders = new Dictionary<string, MonitoringExchange>();
        OrderExecutor = PortfolioExecutor.orderProcessor;
        OrderExecutor.AddOrderStatusListener(OnOrderStatus, new OrderStatusFilter(Symbol));
        IsStopSendOrderTimer = true;
        StartSendingOrders();
    }

    #endregion

    #region Scales

    #endregion

    #region InputParameters

    #endregion
}



