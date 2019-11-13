
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
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






[Serializable]
public sealed partial class PortfolioExecutor : PortfolioExecutorBase
{
	public PortfolioExecutor()
	{
		slice = new InstrumentExecutorList();
	}
	
	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override void Init(CVFactory instrument)
	{
		base.Init(instrument);

		if (Instrument.CVSlice.Records != null)	
		{
			foreach	(CVRecordBase record in Instrument.CVSlice.Records.Values)
			{
			}
		}
		
	}
	
	/// <summary>
	/// List of all InstrumentExecutor objects of the strategy.
	/// </summary>
	[NonRendered]
	public InstrumentExecutorList Slice
	{
		get
		{
			return (InstrumentExecutorList)slice;
		}
	}
	
	/// <summary>
	/// Interface to class that contatins definitions of the input and output channels.
	/// </summary>
	[NonRendered]
	[UIShow(Description = "Interface to class that contatins definitions of the input and output channels.")]
	[BacktestingLiveIncompatibilityAttribute(BacktestingLiveIncompatibilityAttribute.IncompatibilityType.LiveOnly)]
	public StrategyChannels StrategyChannels
	{
		get
		{
			return _strategyChannels;
		}
	}

	private StrategyChannels _strategyChannels;

	#region Reports

	#endregion

	#region Scales

	#endregion

	#region Baskets

	#endregion

	#region CustomMessages

	#endregion

	#region InstrumentInputParameters

	#endregion

	protected override bool EnableCM 
	{
		set
		{
		}
	}

	protected override void RegisterStandardChannelsContainers(List<object> channelsContainers)
	{
		_strategyChannels = new StrategyChannels(this);
		channelsContainers.Add(_strategyChannels);
	}

	public override void UpdateInputParameters(PortfolioExecutorBase pe, List<QuantOffice.StrategyRunner.InstrumentParameters> instrumentParameters)
	{		OnUpdateInputParameters(new EditableInputParameters((PortfolioExecutor) pe, instrumentParameters));
	}

	internal void UpdateInputParameters(EditableInputParameters inputParameters)
	{
		ListExchanges = inputParameters.ListExchanges;
		IsEnabledLog = inputParameters.IsEnabledLog;
		
	}


}



internal sealed class EditableInputParameters
{
	internal EditableInputParameters(PortfolioExecutor pe, List<QuantOffice.StrategyRunner.InstrumentParameters> instrumentInputParameters)
	{
		ListExchanges = pe.ListExchanges;
		IsEnabledLog = pe.IsEnabledLog;
		InstrumentInputParameters = new List<EditableInstrumentInputParameters>();
		foreach (QuantOffice.StrategyRunner.InstrumentParameters instrumentInputParameter in instrumentInputParameters)
			InstrumentInputParameters.Add(new EditableInstrumentInputParameters(instrumentInputParameter.Symbol, (InstrumentExecutor) instrumentInputParameter.Executor));
		
	}	

	public InputList<ExchangeParameters> ListExchanges;
	public bool IsEnabledLog;
	public List<EditableInstrumentInputParameters> InstrumentInputParameters;
	

}

