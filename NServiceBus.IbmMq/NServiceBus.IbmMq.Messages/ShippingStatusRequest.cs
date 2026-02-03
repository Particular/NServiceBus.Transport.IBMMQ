namespace NServiceBus.IbmMq.Messages;

public class ShippingStatusRequest : IMessage
{
    public string OrderId { get; set; } = Guid.NewGuid().ToString()[..8];
}
