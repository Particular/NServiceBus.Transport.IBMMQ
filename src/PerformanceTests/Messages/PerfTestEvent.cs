namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Messages;

class PerfTestEvent : IEvent
{
    public int Index { get; set; }
}
