namespace NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class PerfTestEvent : IEvent
{
    public int Index { get; set; }
}
