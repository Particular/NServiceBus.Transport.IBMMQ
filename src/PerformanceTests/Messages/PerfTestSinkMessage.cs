namespace NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class PerfTestSinkMessage : IMessage
{
    public int Index { get; set; }
}
