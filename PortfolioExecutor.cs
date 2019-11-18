

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography.X509Certificates;
using System.Timers;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using Deltix.EMS.API;
using Deltix.EMS.Coordinator;
using Deltix.EMS.Simulator;
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


/// <summary>
/// This class contains portfolio level event processing logic.
/// </summary>
public partial class PortfolioExecutor : PortfolioExecutorBase
{
    #region LocalVariables

    // in seconds (30 minutes)
    public int IntervalValidationMessages = 1800;

    internal OrderProcessor orderProcessor;

    // EMail client
    internal EMailSender EMailSender;

    // List of objects for monitoring Exchanges
    public List<MonitoringExchange> MonitoringExchanges = new List<MonitoringExchange>();

    private StrategyTimer ChannelTimer;
    public DataFeedValidator DataFeedValidator;

    // This list is created with triples of parameters (Exchange_1, Exchange_2, Instrument) necessary for match,
    // because the main list of input parameters (ParametersMatchExchanges) contains a list of Instruments for each pair of exchanges
    public InputList<MatchExchangesParameters> ListMatchExchanges;

    #endregion

    #region BuildingBlocks

    public static EMSParameters CreateEMSParameters()
    {
        EMSParameters emsParameters = new EMSParameters();
        OrderSimulatorParameters simulatorParams = new OrderSimulatorParameters();
        simulatorParams.simulationMode = SimulationMode.BBO;
        simulatorParams.slip_cur = 0;
        simulatorParams.slip_stock = 0;
        simulatorParams.slip_fut = 0;
        emsParameters.OrderExecutor.Parameters = simulatorParams;

        return emsParameters;
    }

    internal void OnUpdateInputParameters(EditableInputParameters inputParameters)
    {
        SynchronizeInputParameters(inputParameters);
        UpdateParamsMonitors(inputParameters, MonitoringExchanges);
        UpdateInputParameters(inputParameters);
        UpdateMonitors();
        UpdateMatchExchanges();
    }

    internal void SynchronizeInputParameters(EditableInputParameters inputParameters)
    {
        // check unknown Exchanges for ListMatchExchanges
        var allExchanges = ListExchanges.Select(e => e.Name).ToList();
        InputList<MatchExchangesParameters> removedExchanges = new InputList<MatchExchangesParameters>();
        foreach (var exchange in ListMatchExchanges)
        {
            if(!allExchanges.Contains(exchange.FirstExchange) || !allExchanges.Contains(exchange.SecondExchange))
                removedExchanges.Add(exchange);
        }

        foreach (var removedExchange in removedExchanges)
        {
            ListMatchExchanges.Remove(removedExchange);
            SendLog(String.Format("Remove match exchanges {0}-{1} on {2} symbol because monitoring exchanges do not contain them",
                removedExchange.FirstExchange, removedExchange.SecondExchange, removedExchange.Symbols));
        }

        // check unknown Symbols for ListMatchExchanges
        foreach (var matchExchange in ListMatchExchanges)
        {
            var exchangesForAddingSymbol = (inputParameters == null ? ListExchanges : inputParameters.ListExchanges).Where(e => e.Name == matchExchange.FirstExchange || e.Name == matchExchange.SecondExchange).
                Where(e => !Utils.GetListFromLine(e.Symbols).Contains(matchExchange.Symbols)).ToList();

            foreach (var exchangeForAddingSymbol in exchangesForAddingSymbol)
            {
                exchangeForAddingSymbol.Symbols += (exchangeForAddingSymbol.Symbols == "" ? "" : ", ") + matchExchange.Symbols; 
                SendLog(String.Format("Added {0} symbol for {1} exchange because this symbol is needed for matching",
                        matchExchange.Symbols, exchangeForAddingSymbol.Name));
            }
        }
    }

    internal void UpdateParamsMonitors(EditableInputParameters inputParameters, List<MonitoringExchange> monitors)
    {
        foreach (var monitor in monitors)
        {
            monitor.ExchangeParameters = inputParameters.ListExchanges.FirstOrDefault(e => e.Name == monitor.Name);
        }
    }

    private void UpdateMonitors()
    {
        foreach (var monitor in MonitoringExchanges)
        {
            if (monitor.ExchangeParameters.IsActivate)
                monitor.Start();
            else
                monitor.Stop();

            monitor.UpdateSymbols();
            monitor.UpdateSymbolsForOrders();
        }
    }

    private void UpdateMatchExchanges()
    {
        var activateExñhanges = ListExchanges.Where(e => e.IsActivate).Select(e => e.Name).ToList();
        var deactivateExñhanges = ListExchanges.Where(e => !e.IsActivate).Select(e => e.Name).ToList();

        foreach (var matchExchange in ListMatchExchanges)
        {
            if (activateExñhanges.Contains(matchExchange.FirstExchange) &&
                activateExñhanges.Contains(matchExchange.SecondExchange) &&
                DataFeedValidator.FindExchange(matchExchange) == null)
            {
                DataFeedValidator.AddMatchExchange(matchExchange);
                SendLog(String.Format("Activate match exchanges {0}-{1} on {2} symbol because monitoring on exchange activated",
                    matchExchange.FirstExchange, matchExchange.SecondExchange, matchExchange.Symbols));
            } else if (deactivateExñhanges.Contains(matchExchange.FirstExchange) ||
                       deactivateExñhanges.Contains(matchExchange.SecondExchange))
            {
                DataFeedValidator.RemoveMatchExchange(DataFeedValidator.FindExchange(matchExchange));
                SendLog(String.Format("Deactivate match exchanges {0}-{1} on {2} symbol because monitoring on exchange deactivated",
                    matchExchange.FirstExchange, matchExchange.SecondExchange, matchExchange.Symbols));
            }
        }
    }

    public void CreateChannelMessage(object source)
    {
        foreach (var monitor in MonitoringExchanges)
        {
            StrategyChannels.SendInputParameters(monitor);
        }
    }

    public void AddSymbol(string symbol)
    {
        foreach (InstrumentExecutor instr in Slice)
        {
            if (instr.Symbol == symbol)
                return;
        }

        AddExperimentSymbols(new string[] {symbol}, false);

        foreach (InstrumentExecutor instr in Slice)
        {
            if (instr.Symbol == symbol)
            {
                instr.OnInit();
                instr.OrderBook = OrderBookManager.CreateRealOrderBook(instr.Symbol, 10);
                break;
            }
        }
    }

    public void RemoveSymbol(string symbol)
    {
        foreach (var monitor in MonitoringExchanges)
        {
            if (monitor.ExchangeSymbols.ContainsKey(symbol))
                return;
        }

        foreach (InstrumentExecutor instr in Slice)
        {
            if (instr.Symbol == symbol)
            {
                instr.OrderExecutor.RemoveOrderStatusListener(instr.OnOrderStatus, new OrderStatusFilter(instr.Symbol));
                instr.StopSendingOrders();
                break;
            }
        }

        RemoveExperimentSymbols(new string[] { symbol });
    }

    public void SendMessage(string title, string textMessage, string logMessage)
    {
        SendLog(logMessage);
        EMailSender.Send(title, textMessage);
    }

    public void SendLog(string logMessage)
    {
        if (IsEnabledLog)
            Log(logMessage);
    }

    #endregion

    #region Events

    public override void OnInit()
    {
        orderProcessor = new OrderProcessor(this);
        ((OrderProcessor)orderProcessor).OnError += delegate (Exception e) { InfraStructure.LogHandler.Logger.Log(e, false); };
        orderProcessor.SetParameters(emsParameters);

        ChannelTimer = Timers.CreateRecursiveTimer(new TimeSpan(0, 0, 0, ChannelUpdateInterval),
            CreateChannelMessage, null);
        ChannelTimer.Start();

        List<string> initSymbols = new List<string>();

        foreach (var instr in Slice)
        {
            initSymbols.Add(instr.Symbol);
        }

        RemoveExperimentSymbols(initSymbols.ToArray());

        //Call Instruments OnInit
        base.OnInit();

        SendLog("Initialize parameters");

        // Initialize triples for matching
        ListMatchExchanges = new InputList<MatchExchangesParameters>();
        foreach (var matchExchange in ParametersMatchExchanges)
        {
            var listSymbols = Utils.GetListFromLine(matchExchange.Symbols);
            foreach (var symbol in listSymbols)
            {
                ListMatchExchanges.Add(new MatchExchangesParameters(matchExchange.FirstExchange, matchExchange.SecondExchange,
                    symbol, matchExchange.TimePeriod, matchExchange.Threshold));
            }
        }

        SynchronizeInputParameters(null);
        DataFeedValidator = new DataFeedValidator(this);

        try
        {
            EMailSender = new EMailSender(this, SendMailParameters); // create e-mail sender
        }
        catch (Exception exception)
        {
            SendLog(String.Format("Initialization failed! Error: {0}", exception));
        }

        foreach (var exchange in ListExchanges)
        {
            MonitoringExchanges.Add(new MonitoringExchange(exchange, this));
        }

        UpdateMonitors();
        UpdateMatchExchanges();
        SendLog("Start strategy");
    }

    public override void OnExit(ExitState exitState)
    {
        ChannelTimer.Stop();

        foreach (InstrumentExecutor instr in Slice)
        {
            instr.StopSendingOrders();
        }

        foreach (var monitor in MonitoringExchanges)
        {
            monitor.Stop();
        }

        EMailSender.Dispose();
        SendLog("Stop strategy");
        //Call Instruments OnExit
        base.OnExit(exitState);
    }

    #endregion

    #region InputParameters

    [DisplayInfo(DisplayName = "Time of waiting messages (sec)")]
    public int WaitInterval = 60;

    [DisplayInfo(DisplayName = "Interval of sending orders (sec)")]
    public int SendOrderInterval = 60;

    [DisplayInfo(DisplayName = "Interval of update channel (sec)")]
    public int ChannelUpdateInterval = 20;

    [DisplayInfo(DisplayName = "Max delay for `PendingSubmit` and `PendingCancel` order statuses (sec)")]
    public int WaitPendingStatus = 3;

    [EditableInRuntime]
    [DisplayInfo(DisplayName = "Exchanges")]
    public InputList<ExchangeParameters> ListExchanges = new InputList<ExchangeParameters>();

    //[EditableInRuntime]
    [DisplayInfo(DisplayName = "Compare Exchanges")]
    public InputList<MatchExchangesParameters> ParametersMatchExchanges = new InputList<MatchExchangesParameters>();

    [DisplayInfo(DisplayName = "Send E-Mail Parameters")]
    public MailSenderParameters SendMailParameters = new MailSenderParameters();

    [DisplayInfo(DisplayName = "EMS Parameters")]
    public Deltix.EMS.Coordinator.EMSParameters emsParameters = CreateEMSParameters();

    [EditableInRuntime]
    [DisplayInfo(DisplayName = "Enable logs?")]
    public bool IsEnabledLog = false;

    #endregion
}
