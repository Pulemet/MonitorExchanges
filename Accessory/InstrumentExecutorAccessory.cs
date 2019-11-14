
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






public sealed partial class InstrumentExecutor : InstrumentExecutorBase
{
	#region LocalVariables

	#endregion

	#region InputParametersProperties
	
	public int WaitInterval
	{
		get
		{
			return PortfolioExecutor.WaitInterval;
		}
	}
	public int SendOrderInterval
	{
		get
		{
			return PortfolioExecutor.SendOrderInterval;
		}
	}
	public int ChannelUpdateInterval
	{
		get
		{
			return PortfolioExecutor.ChannelUpdateInterval;
		}
	}
	public int WaitPendingStatus
	{
		get
		{
			return PortfolioExecutor.WaitPendingStatus;
		}
	}
	public InputList<ExchangeParameters> ListExchanges
	{
		get
		{
			return PortfolioExecutor.ListExchanges;
		}
	}
	public InputList<MatchExchangesParameters> ParametersMatchExchanges
	{
		get
		{
			return PortfolioExecutor.ParametersMatchExchanges;
		}
	}
	public MailSenderParameters SendMailParameters
	{
		get
		{
			return PortfolioExecutor.SendMailParameters;
		}
	}
	public Deltix.EMS.Coordinator.EMSParameters emsParameters
	{
		get
		{
			return PortfolioExecutor.emsParameters;
		}
	}
	public bool IsEnabledLog
	{
		get
		{
			return PortfolioExecutor.IsEnabledLog;
		}
	}
	#endregion

	#region Reports

	#endregion

	#region Scales
	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override object CalculateScale(int index)
	{
	
		throw new ArgumentException("Wrong scale id.");
	}
	#endregion

	#region Internal

	/// <summary>
	/// Provides access to fundamental data.
	/// </summary>
	[NonRendered]
	public FundamentalProvider Fundamental;

	

	
	private PortfolioExecutor portfolioExecutor = null;
	
	/// <summary>
	/// For internal purpose.
	/// </summary>
	public InstrumentExecutor()
	{
	}
	
	protected override void Init(SyntheticInstrument instrument)
	{
		base.Init(instrument);
		portfolioExecutor = (PortfolioExecutor)((CVFactory)Instrument).PortfolioExecutor;
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override CVRecordBase CVRecordBase 
	{ 
		get
		{
			return record;
		}
		set
		{
			base.CVRecordBase = value;
			
			bars = (IntradayBarProvider)value.IntradayBarProvider;
			daily = (DailyBarProvider)value.DailyBarProvider;
			
			Fundamental = new FundamentalProvider(CVRecordBase);
			CVRecordBase.CVFundamentalProviders.Add(Fundamental);
			
			foreach	(CVPropertyDescriptor descriptor in CVFactory.PropertyData)
			{
				CVCustomPropertyDescriptor cpDescriptor = descriptor as CVCustomPropertyDescriptor;
				if (cpDescriptor == null)
					continue;

			}
		}
	}
	
	[NonRendered]
	public IntradayBarProvider bars;

	[NonRendered]
	public DailyBarProvider daily;

	public IntradayBarProvider Bars
	{
		get
		{
			if (!CVFactory.UseIntraday)
				throw new Exception("Access to 'Bars' object is forbidden from strategies without intraday subscription.");
			return bars;
		}
	}
	
	public DailyBarProvider Daily
	{
		get
		{
			if (!CVFactory.UseDaily)
				throw new Exception("Access to 'Daily' object is forbidden from strategies without daily subscription.");
			return daily;
		}
	}

	
	public override IntradayBarProvider IntradayBarProvider
	{
		get
		{
			return bars;
		}
	}

	public override DailyBarProvider DailyBarProvider
	{
		get
		{
			return daily;
		}
	}
	
	/// <summary>
	/// Returns the instance of the class contains portfolio level event processing logic.
	/// </summary>
	[NonRendered]
	private PortfolioExecutor PortfolioExecutor
	{
		get
		{
			return portfolioExecutor; //(PortfolioExecutor)((CVFactory)Instrument).PortfolioExecutor;
		}
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override CVScalingResultBase GetNewScalingResult(string symbol)
	{
		return new ScalingResult(symbol, this);
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override void CalculateProperty(int index)
	{
		switch (index)
		{
			default:
				throw new ArgumentException("Wrong property index.");					
		}
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override void FillEmptyProperty(int index)
	{
		switch (index)
		{
			default:
				throw new ArgumentException("Wrong property index.");					
		}
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	protected override void OnBarUpdate(BarPeriodicity barPeriodicity, long queueIndex)
	{
		Bar bar;

		if (barPeriodicity == BarPeriodicity.Daily)
		{
			long agoIndex = Daily.AllCount - 1 - queueIndex;
			if (agoIndex >= 0 && agoIndex < Daily.Count)
			{
				bar = Daily.DaysAgo(agoIndex);
				OnBarUpdate(barPeriodicity, bar);
			}
		}
		else if (barPeriodicity == BarPeriodicity.Intraday)
		{
			long agoIndex = Bars.AllCount - 1 - queueIndex;
			if (agoIndex >= 0 && agoIndex < Bars.Count)
			{
				bar = Bars.BarsAgo(agoIndex);
				OnBarUpdate(barPeriodicity, bar);
			}
		}
		else
		{
			throw new Exception(String.Format("Invalid bar periodicity '{0}'", barPeriodicity));
		}
	}

	#endregion

	

	internal void UpdateInputParameters(EditableInstrumentInputParameters inputParameters)
	{

	}
}

internal sealed class EditableInstrumentInputParameters
{
	internal EditableInstrumentInputParameters(string _symbol, InstrumentExecutor ie)
	{
		Symbol = _symbol;
		
	}	

	public string Symbol;
	
}



