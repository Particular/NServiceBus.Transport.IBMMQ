using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq;

Console.Title = "Bridge";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport("localhost", "admin", "passw0rd", null, null));

var endpointB = new EndpointConfiguration("DEV.SHIPPING");

//endpointB.RegisterPublisher<OrderPlaced>("DEV.SALES");

//ibmmq.HasEndpoint(endpointB);

//var endpointA = new BridgeEndpoint("DEV.SALES");

//endpointA.RegisterPublisher<OrderShipped>("DEV.SHIPPING");

//learning.HasEndpoint(endpointA);

//bridgeConfiguration.AddTransport(ibmmq);
//bridgeConfiguration.AddTransport(learning);
//builder.UseNServiceBusBridge(bridgeConfiguration);

var host = builder.Build();

await host.RunAsync();
