using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq;

Console.Title = "FullTransportEndpoint";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport("localhost", "admin", "passw0rd", null, null);

var endpointB = new EndpointConfiguration("DEV.SHIPPING");
endpointB.SendFailedMessagesTo("error");
endpointB.UseTransport(ibmmq);
endpointB.UseSerialization<SystemJsonSerializer>();
endpointB.PurgeOnStartup(true);
endpointB.SendOnly();

builder.UseNServiceBus(endpointB);

var host = builder.Build();

await host.StartAsync();

var instance = host.Services.GetRequiredService<IMessageSession>();


using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var instanceId = Guid.CreateVersion7();
long sendCount = 0;

while (!cts.IsCancellationRequested)
{
    if (!Console.KeyAvailable)
    {
        await Task.Delay(100);
        continue;
    }

    Console.ReadLine();

    var t = new List<Task>();
    for (int i = 0; i < 10; i++)
    {
        var data = $"{instanceId}/{++sendCount}";
        Console.WriteLine($"Sending message: {data}");
        t.Add(instance.Send("DEV.SHIPPING", new MyMessage(data)));
    }

    await Task.WhenAll(t);
    Console.WriteLine("Done");
}


await host.StopAsync();

public record MyMessage(string Data) : IMessage
{

}
