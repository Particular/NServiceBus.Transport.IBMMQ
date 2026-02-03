using NServiceBus.IbmMq.Messages;

namespace NServiceBus.IbmMq.Sales;

public class ShippingStatusResponseHandler : IHandleMessages<ShippingStatusResponse>
{
    public Task Handle(ShippingStatusResponse message, IMessageHandlerContext context)
    {
        Console.WriteLine($"Received Shipping Status Update, OrderId: {message.OrderId}.");
        Console.WriteLine($"Message: {message.Text}");

        return Task.CompletedTask;
    }
}
