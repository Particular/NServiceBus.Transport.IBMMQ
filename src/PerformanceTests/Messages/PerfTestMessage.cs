namespace NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class PerfTestMessage : IMessage
{
    public int Index { get; set; }
}
