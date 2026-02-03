using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq.Messages;

namespace NServiceBus.IbmMq.Sales;

public class InputLoopService(IMessageSession messageSession) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Press 'C' to Check Shipping Status, 'P' to Place Order, or 'Q' to quit.");
            Console.ResetColor();
            var key = Console.ReadKey();
            Console.WriteLine();

            switch (key.Key)
            {
                case ConsoleKey.C:
                    var message = new ShippingStatusRequest();

                    await messageSession.Send(message);

                    Console.WriteLine($"Requested Shipping Status, OrderId: {message.OrderId}.");
                    break;

                case ConsoleKey.P:
                    var messageEvent = new OrderPlaced();

                    await messageSession.Publish(messageEvent);

                    Console.WriteLine($"Placed New Order, OrderId: {messageEvent.OrderId}.");
                    break;

                case ConsoleKey.Q:
                    return;

                default:
                    Console.WriteLine("Unknown input. Please try again.");
                    break;
            }
        }
    }
}
