using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq;
using NServiceBus.IbmMq.Messages;

Console.Title = "Bridge";
var builder = Host.CreateApplicationBuilder(args);

var bridgeConfiguration = new BridgeConfiguration();
bridgeConfiguration.RunInReceiveOnlyTransactionMode();
var ibmmq = new BridgeTransport(new IbmMqTransport("localhost", "admin", "passw0rd", null, null));
var learning = new BridgeTransport(new LearningTransport());

var endpointB = new BridgeEndpoint("DEV.SHIPPING");

endpointB.RegisterPublisher<OrderPlaced>("DEV.SALES");

ibmmq.HasEndpoint(endpointB);

var endpointA = new BridgeEndpoint("DEV.SALES");

endpointA.RegisterPublisher<OrderShipped>("DEV.SHIPPING");

learning.HasEndpoint(endpointA);

bridgeConfiguration.AddTransport(ibmmq);
bridgeConfiguration.AddTransport(learning);
builder.UseNServiceBusBridge(bridgeConfiguration);

var host = builder.Build();

await host.RunAsync();
