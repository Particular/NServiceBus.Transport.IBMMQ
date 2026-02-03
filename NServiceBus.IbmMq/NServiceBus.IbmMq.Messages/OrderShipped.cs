namespace NServiceBus.IbmMq.Messages;

public class OrderShipped : IEvent
{
    public required string OrderId { get; set; }
}
