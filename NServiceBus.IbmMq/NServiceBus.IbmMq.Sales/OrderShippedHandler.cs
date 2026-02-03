using NServiceBus.IbmMq.Messages;

namespace NServiceBus.IbmMq.Sales;

public class OrderShippedHandler : IHandleMessages<OrderShipped>
{
    public Task Handle(OrderShipped message, IMessageHandlerContext context)
    {
        Console.WriteLine($"Received OrderShipped Event, OrderId: {message.OrderId}");

        return Task.CompletedTask;
    }
}
