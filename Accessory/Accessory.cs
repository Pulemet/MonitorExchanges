
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




[assembly: AssemblyVersion("1.0.*")]



	#region Reports

	#endregion

	#region CustomMessages














	#endregion

/// <summary>
/// This class provides access to fundamental and custom attributes. It is generated class to achieve the best performance with selected attributes. For each selected attribute special provider is generated inside this class with the name of the attribute.
/// </summary>
[BacktestingLiveIncompatibilityAttribute(BacktestingLiveIncompatibilityAttribute.IncompatibilityType.LimitedLive)]
public class FundamentalProvider : CVProvider, ICVFundamentalProvider
{
	private CVFundamental today = null;
	private CVRecordBase record = null;
	
	private CVQueue<short> queueCurrency;
	private CVQueue<DateTime> queueCurrency_time;

	/// <summary>
	/// For internal purpose.
	/// </summary>
	public FundamentalProvider(CVRecordBase record)
	{
		this.record = record;
		
		fieldProviders.Add(Currency = new FundamentalFieldProviderCurrency(record));

		foreach (CVQueue queue in record.DataQueues)
		{			
			if (queue.Descriptor.DisplayName == "Currency")
				queueCurrency = (CVQueue<short>) queue;
			if (queue.Descriptor.DisplayName == "Currency_time")
				queueCurrency_time = (CVQueue<DateTime>) queue;
		}
		

	}



	/// <summary>
	/// For internal purpose.
	/// </summary>
	public void StartNewDay(DateTime clientDate)
	{
		for (int i = 0; i < fieldProviders.Count; ++i)
			((ICVFundamentalProvider)fieldProviders[i]).StartNewDay(clientDate);

		todayDate = clientDate;
		today = new CVFundamental();
		
		today.Currency = queueCurrency.Current;
	}
	
	/// <summary>
	/// Today fundamental data.
	/// </summary>
	[UIShow(Description = "Today fundamental data.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.Current instead of Fundamental.Today.EPS")]
	public CVFundamental Today
	{
		get
		{
			return today;
		}
	}
	
	/// <summary>
	/// Historical fundamental data 'count' periods ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data 'count' periods ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.PeriodsAgo() instead of Fundamental.PeriodsAgo().EPS")]
	public CVFundamental PeriodsAgo(int count)
	{
		CVFundamental result = new CVFundamental();
		
		if (queueCurrency.Count > count)
			result.Currency = queueCurrency.GetBack(count);		
		
		return result;
	}

	/// <summary>
	/// Historical fundamental data 'days' trading days ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data 'days' trading days ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field.For example for attribute EPS use Fundamental.EPS.DaysAgo() instead of Fundamental.DaysAgo().EPS")]
	public CVFundamental DaysAgo(int days)
	{
		CVFundamental result = new CVFundamental();
		
		CVFundamental atDate = AtDate(GetDateTradingDaysAgo(days, record.Calendar));
		result.Currency = atDate.Currency;
		
		return result;
	}
	
	/// <summary>
	/// Previous trading day fundamental data.
	/// </summary>
	[UIShow(Description = "Previous trading day fundamental data.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.Yesterday instead of Fundamental.Yesterday.EPS")]
	public CVFundamental Yesterday
	{
		get
		{
			return DaysAgo(1);
		}
	}
	
	/// <summary>
	/// Historical fundamental data 'weeks' weeks ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data 'weeks' weeks ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.WeeksAgo() instead of Fundamental.WeeksAgo().EPS")]
	public CVFundamental WeeksAgo(int weeks)
	{
		return AtDate(todayDate.AddDays(-7 * weeks));
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'months' months ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data in the same trading day 'months' months ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.MonthsAgo() instead of Fundamental.MonthsAgo().EPS")]
	public CVFundamental MonthsAgo(int months)
	{
		DateTime past = GetDateTimeMonthsAgoForSameTradingDay(months, record.Calendar);
		return AtDate(past);
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'quarters' quarters ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data in the same trading day 'quarters' quarters ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.QuartersAgo() instead of Fundamental.QuartersAgo().EPS")]
	public CVFundamental QuartersAgo(int quarters)
	{
		return MonthsAgo(3 * quarters);
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'years' years ago.
	/// </summary>
	[UIShow(Description = "Historical fundamental data in the same trading day 'years' years ago.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.YearsAgo() instead of Fundamental.YearsAgo().EPS")]
	public CVFundamental YearsAgo(int years)
	{
		return MonthsAgo(12 * years);
	}

	/// <summary>
	/// Historical fundamental data for the given date.
	/// </summary>
	[UIShow(Description = "Historical fundamental data for the given date.")]
	[Obsolete("This method has been deprecated. For best performance please use appropriate method from particular attribute field. For example for attribute EPS use Fundamental.EPS.AtDate() instead of Fundamental.AtDate().EPS")]
	public CVFundamental AtDate(DateTime date)
	{
		CVFundamental result = new CVFundamental();
		
		long indexCurrency = queueCurrency_time.BinarySearch(date);
		result.Currency = queueCurrency.GetBack(indexCurrency >= 0 ? indexCurrency : ~indexCurrency);
		
		return result;
	}

	
	/// <summary>
	/// Provides access to fundamental attribute data.
	/// </summary>
	public FundamentalFieldProviderCurrency Currency;
}

[BacktestingLiveIncompatibilityAttribute(BacktestingLiveIncompatibilityAttribute.IncompatibilityType.LimitedLive)]
public class CVFundamental
{
	
	public short Currency;

	public static bool operator == (CVFundamental a, CVFundamental b)
	{
		object a_ = a;
		object b_ = b;

		if (a_ == null)
			return b_ == null;
	
		if (b_ == null)
		{
			return false;
		}
		
		return a.Currency == b.Currency;
	}
	
	public static bool operator != (CVFundamental a, CVFundamental b)
	{
		return !(a == b);
	}
	
	public override bool Equals(object obj)
	{
		if (!(obj is CVFundamental))
			return false;
		return this == (obj as CVFundamental);
	}

	public override int GetHashCode()
	{
		return Currency.GetHashCode();
	}
}
/// <summary>
/// This class provides access to particular fundamental or custom attribute. It is generated class to achieve the best performance with selected attributes. 
/// </summary>
[BacktestingLiveIncompatibilityAttribute(BacktestingLiveIncompatibilityAttribute.IncompatibilityType.LimitedLive)]
public class FundamentalFieldProviderCurrency : CVProvider, ICVFundamentalProvider
{
	private CVRecordBase record = null;
	
	private CVQueue<short> queueCurrency;
	private CVQueue<DateTime> queueCurrency_time;

	/// <summary>
	/// For internal purpose.
	/// </summary>
	public FundamentalFieldProviderCurrency(CVRecordBase record)
	{
		this.record = record;

		foreach (CVQueue queue in record.DataQueues)
		{			
			if (queue.Descriptor.DisplayName == "Currency")
				queueCurrency = (CVQueue<short>) queue;
			if (queue.Descriptor.DisplayName == "Currency_time")
				queueCurrency_time = (CVQueue<DateTime>) queue;
		}
	}

	/// <summary>
	/// For internal purpose.
	/// </summary>
	public void StartNewDay(DateTime clientDate)
	{
		todayDate = clientDate;
	}

	/// <summary>
	/// Today fundamental data.
	/// </summary>
	[UIShow(Description = "Current fundamental data.")]
	public short Current
	{
		get
		{			
			return queueCurrency.Current;
		}
	}
	
	/// <summary>
	/// Count of historical data points available to retrieve by PeriodsAgo(int count) method.
	/// </summary>
	[UIShow(Description = "Count of historical data available.")]
	public long Count
	{
		get
		{		
		return queueCurrency.Count;
		}
	}

	/// <summary>
	/// The capacity of historical data stack. The maximum count of data points available to retrieve by PeriodsAgo(int count) method.
	/// </summary>
	[UIShow(Description = "The capacity of historical data stack. The maximum count of data points available to retrieve by PeriodsAgo(int count) method")]
	public long StackSize
	{
		get
		{		
		return queueCurrency.Size;
		}
	}

	/// <summary>
	/// Historical fundamental data 'count' periods ago.
	/// </summary>
	/// <param name="count">The count of data points ago to retrieve data.</param>
	/// <returns>Value of the attribute 'count' data points ago.</returns>
	[UIShow(Description = "Historical fundamental data 'count' periods ago.")]
	public short PeriodsAgo(int count)
	{		
		if (queueCurrency.Count > count)
			return queueCurrency.GetBack(count);
		return new NullableShort();	
	}

	/// <summary>
	/// Historical fundamental data 'days' trading days ago.
	/// </summary>
	/// <param name="days">The count of trading days ago to retrieve data.</param>
	/// <returns>Value of the attribute 'days' trading days ago.</returns>
	[UIShow(Description = "Historical fundamental data 'days' trading days ago.")]
	public short DaysAgo(int days)
	{		
		return AtDate(GetDateTradingDaysAgo(days, record.Calendar));
	}
	
	/// <summary>
	/// Previous trading day fundamental data.
	/// </summary>
	[UIShow(Description = "Previous trading day fundamental data.")]
	public short Yesterday
	{
		get
		{
			return DaysAgo(1);
		}
	}
	
	/// <summary>
	/// Historical fundamental data 'weeks' weeks ago.
	/// </summary>
	/// <param name="weeks">The count of weeks ago to retrieve data.</param>
	/// <returns>Value of the attribute 'weeks' weeks ago.</returns>
	[UIShow(Description = "Historical fundamental data 'weeks' weeks ago.")]
	public short WeeksAgo(int weeks)
	{
		return AtDate(todayDate.AddDays(-7 * weeks));
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'months' months ago.
	/// </summary>
	/// <param name="months">The count of months ago to retrieve data.</param>
	/// <returns>Value of the attribute 'months' months ago.</returns>
	[UIShow(Description = "Historical fundamental data in the same trading day 'months' months ago.")]
	public short MonthsAgo(int months)
	{
		DateTime past = GetDateTimeMonthsAgoForSameTradingDay(months, record.Calendar);
		return AtDate(past);
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'quarters' quarters ago.
	/// </summary>
	/// <param name="quarters">The count of quarters ago to retrieve data.</param>
	/// <returns>Value of the attribute 'quarters' quarters ago.</returns>
	[UIShow(Description = "Historical fundamental data in the same trading day 'quarters' quarters ago.")]
	public short QuartersAgo(int quarters)
	{
		return MonthsAgo(3 * quarters);
	}

	/// <summary>
	/// Historical fundamental data in the same trading day 'years' years ago.
	/// </summary>
	/// <param name="years">The count of years ago to retrieve data.</param>
	/// <returns>Value of the attribute 'years' years ago.</returns>
	[UIShow(Description = "Historical fundamental data in the same trading day 'years' years ago.")]
	public short YearsAgo(int years)
	{
		return MonthsAgo(12 * years);
	}

	/// <summary>
	/// Historical fundamental data for the given date.
	/// </summary>
	/// <param name="date">Date to retrieve data.</param>
	/// <returns>If carry over is ON returns nearest value to given date. If carry over is OFF returns value in case date is the actual date of data update, 
	/// otherwise empty value. Empty value is NaN for double and float types and is null for reference types. 
	/// For nullable primitives such as NullableInt, NullableLong there is IsNull property that allows to determine is any value available.</returns>
	[UIShow(Description = "Historical fundamental data for the given date.")]
	public short AtDate(DateTime date)
	{		
		long index = queueCurrency_time.BinarySearch(date);
		if (index < 0)
			index = ~index;
		
		if (queueCurrency_time.GetBack(index).Date > date.Date)
			throw new Exception("There is no data available for date" + date);
		
		return queueCurrency.GetBack(index);
	}

}

public class IndexProvider
{	
	[NonRendered][UIShow] public QuoteProvider Quote;	
}

public class ScalingResult : CVScalingResultBase
{
	public InstrumentExecutorList InstrumentList = new InstrumentExecutorList();
	
	public ScalingResult(string symbol, InstrumentExecutor executor) : base(symbol, executor)
	{
	}
	
	protected override void Add(InstrumentExecutorBase executorBase)
	{
		InstrumentList.Add(executorBase as InstrumentExecutor);
	}

	protected override IList<InstrumentExecutorBase> InstrumentListContainer 
	{ 
		get { return InstrumentList; }
		set { InstrumentList = value as InstrumentExecutorList;}
	}
}

public class ScaleProvider : ScaleProviderBase
{
	public new ScalingResult GetInterval(string symbol)
	{
		return base.GetInterval(symbol) as ScalingResult;
	}

	public new ScalingResult GetInterval(int number)
	{
		return base.GetInterval(number) as ScalingResult;
	}
}

public class InstrumentExecutorList : InstrumentExecutorListBase
{
	public new InstrumentExecutor this[int index] 
	{ 
		get
		{
			return base[index] as InstrumentExecutor;
		}
	}
	
	public new InstrumentExecutor this[SymbolKey symbol]
	{
		get
		{
			return data[symbol] as InstrumentExecutor;
		}
	}
	
	public bool TryGetValue(SymbolKey symbol, out InstrumentExecutor executor)
	{
		InstrumentExecutorBase executorBase;
		bool result = data.TryGetValue(symbol, out executorBase);
		executor = (InstrumentExecutor)executorBase;
		return result;
	}
}

public class CVDebugger : ICVDebuggerBase
{
	public void LaunchDebug()
	{
		System.Diagnostics.Debugger.Launch();
	}
}

/// <summary>
/// This part of the class contains base logic for strategy channels.
/// </summary>
[Serializable]
[BacktestingLiveIncompatibilityAttribute(BacktestingLiveIncompatibilityAttribute.IncompatibilityType.LiveOnly)]
public partial class StrategyChannels
{
	private PortfolioExecutor portfolioExecutor;
	private string strategyUserDefinedStatus;
	private QuantOffice.SyntheticInstruments.MarketRunner.CustomMessages.IStrategyServerServicesProxy _servicesProvider;
		
	public StrategyChannels(PortfolioExecutor externalPortfolioExecutor)
	{
		portfolioExecutor = externalPortfolioExecutor;		
	}

	public void SetServicesProvider(QuantOffice.SyntheticInstruments.MarketRunner.CustomMessages.IStrategyServerServicesProxy servicesProvider)
	{
		_servicesProvider = servicesProvider;
	}

	/// <summary>
	/// Returns registered portfolio executor. If more than one is registered throws exception.
	/// </summary>
	public PortfolioExecutor PortfolioExecutor
	{
		get
		{
			return portfolioExecutor;
		}
	}

	// protected override string GetStrategyAssemblyPath(string workFolderPath)
	protected string GetStrategyAssemblyPath(string workFolderPath)
	{
		return this.GetType().Assembly.Location;	
	}

	/*[Deltix.StrategyServer.Api.Channels.Attributes.Output(IsInternal = true)]
	[Deltix.StrategyServer.Api.Channels.Attributes.MessageType(typeof(byte[]))]
	public Deltix.Timebase.Api.IMessageChannel<byte[]> UserDefinedStrategyStatus;*/
		
	public void SendStrategyStatus(string status)
	{
		MemoryStream stream = new MemoryStream();
		new BinaryFormatter().Serialize(stream, status);

		// UserDefinedStrategyStatus.Send(stream.ToArray());

		stream.Close();

		strategyUserDefinedStatus = status;
	}

	public void ResetStrategyStatus()
	{
		MemoryStream stream = new MemoryStream();
		new BinaryFormatter().Serialize(stream, string.Empty);

		// UserDefinedStrategyStatus.Send(stream.ToArray());

		stream.Close();

		strategyUserDefinedStatus = string.Empty;
	}

	/*[Deltix.StrategyServer.Api.Channels.Attributes.Remote(Permissions = Deltix.StrategyServer.Api.ServerPermissions.Read)]
	public string RequestStrategyUserDefinedStatus()
	{
		return strategyUserDefinedStatus;
	}*/

	public static string[] SplitString(string s)
	{
		if (string.IsNullOrEmpty(s))
			return null;

		char[] separators = new char[] {',', ' ', ';', '\r', '\n', '\t'};

		Dictionary<char, bool> separatorTable = new Dictionary<char, bool>();
		for (int i = 0; i < separators.Length; i++)
			separatorTable.Add(separators[i], true);

		List<string> result = new List<string>();

		for (int pos = 0; pos < s.Length; pos++)
		{
			if (char.IsWhiteSpace(s[pos]))
				continue;

			char endChar = separators[0];
			if (s[pos] == '\'' || s[pos] == '\"')
			{
				endChar = s[pos];
				pos++;
			}

			int tickerLength = 0;

			bool hasClosingQuote = false;
			for (; pos + tickerLength < s.Length; tickerLength++)
			{
				int curPos = pos + tickerLength;
				if (s[curPos] == '\r' || s[curPos] == '\n')
					break;

				if (endChar != separators[0])
				{
					if (s[curPos] == endChar && (curPos + 1 >= s.Length || separatorTable.ContainsKey(s[curPos + 1])))
					{
						hasClosingQuote = true;
						break;
					}
				}
				else if (separatorTable.ContainsKey(s[curPos]))
					break;
			}

			if (endChar != separators[0] && !hasClosingQuote)
			{
				pos--;
				tickerLength++;
			}

			if (tickerLength > 0)
				result.Add(s.Substring(pos, tickerLength));
			pos += tickerLength;
		}

		return result.ToArray();
	}

	private SymbolKey GetSymbolKey(string symbol)
	{
		return portfolioExecutor.Instrument.MarketRunner.SymbolKeyGenerator.GetSymbolId(symbol);
	}

	private DateTime GetClientTime(DateTime utcTime)
	{
		return utcTime == DateTime.MinValue || utcTime == DateTime.MaxValue ?
			utcTime : portfolioExecutor.Instrument.MarketRunner.UtcToClientConverter.Convert(utcTime);
	}
}



