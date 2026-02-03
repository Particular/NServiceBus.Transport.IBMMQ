namespace NServiceBus.IbmMq.Messages;

public class OrderPlaced : IEvent
{
    public string OrderId { get; set; } = Guid.NewGuid().ToString()[..8];
}
