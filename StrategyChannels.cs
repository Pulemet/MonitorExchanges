

using System;
using System.Collections.Generic;
using QuantOffice.Execution;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using Deltix.StrategyServer.Api.Channels.Attributes;
using Deltix.Timebase.Api;
using Deltix.StrategyServer.Api.Channels;
using Deltix.Timebase.Api.Messages;
using Deltix.Timebase.Api.Schema;
using RTMath.Containers;
using IBestBidOfferMessageInfo = QuantOffice.Data.IBestBidOfferMessageInfo;
using IPacketEndMessageInfo = QuantOffice.Data.IPacketEndMessageInfo;
using ITradeMessageInfo = QuantOffice.Data.ITradeMessageInfo;
using IL2MessageInfo = QuantOffice.Data.IL2MessageInfo;
using IL2SnapshotMessageInfo = QuantOffice.Data.IL2SnapshotMessageInfo;
using ILevel2MessageInfo = QuantOffice.Data.ILevel2MessageInfo;
using PackageHeader = QuantOffice.Data.PackageHeader;

public partial class StrategyChannels
{
	[Output(Title = "Statistic", IsInternal = false)]
	[MessageType(typeof(StatisticMessage))]
	public IUniqueMessageChannel<StatisticMessage> StatisticChannel;

	private void SendInputParameters(StatisticMessage message)
	{
		StatisticChannel.Send(message);
	}

	public void SendInputParameters(MonitoringExchange monitor)
	{
		StatisticMessage message = new StatisticMessage()
		{
			Exchange = monitor.Name,
		    IsEnabled = monitor.Running,
            LastMessage = monitor.GetLastMessageTime(),
            FilledOrders = monitor.GetFilledOrdersOnSymbols(),
            OrderBookStatus = monitor.GetOrderBookStatus()
        };
		SendInputParameters(message);
	}
}

[Serializable]
public class StatisticMessage : InstrumentMessage
{
	[PrimaryKey]
	[SchemaElement(Name = "Exchange")]
	public string Exchange;
	
	[SchemaElement(Name = "Enabled?")]
	public bool IsEnabled;

    [SchemaElement(Name = "Last Update")]
    public string LastMessage;

    [SchemaElement(Name = "Count of Filled Orders on Symbols (Status)")]
	public string FilledOrders;

    [SchemaElement(Name = "Order Book Status")]
    public string OrderBookStatus;
}






