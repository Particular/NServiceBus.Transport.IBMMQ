using FullTransportEndpoint.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus.Transport.IbmMq;

Console.Title = "FullTransportEndpoint";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport(settings =>
{
    settings.QueueManagerName = "QM1";
    settings.Host = "localhost";
    settings.Port = 1414;
    settings.Channel = "DEV.APP.SVRCONN";
    settings.User = "admin";
    settings.Password = "passw0rd";
});

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
    Console.Write("\nHow many message to send: ");

    var readLineTask = Task.Run(Console.ReadLine, cts.Token);
    _ = await Task.WhenAny(readLineTask, Task.Delay(Timeout.Infinite, cts.Token));

    if (cts.IsCancellationRequested)
    {
        break;
    }

    var input = await readLineTask;
    var x = int.TryParse(input, out var nrInt) ? nrInt : 1;
    Console.WriteLine();

    var t = new List<Task>();

    for (int i = 0; i < x; i++)
    {
        var data = $"{instanceId}/{++sendCount}";
        Console.WriteLine($"Sending message: {data}");
        t.Add(instance.Send("DEV.SHIPPING", new MyMessage(data)));
    }

    await Task.WhenAll(t);
    Console.WriteLine("Done");
}


await host.StopAsync();

