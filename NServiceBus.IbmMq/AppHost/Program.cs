var builder = DistributedApplication.CreateBuilder(args);

var ibmmq = builder.AddContainer("ibm-mq", "ibmcom/mq", "latest")
    .WithEnvironment("LICENSE", "accept")
    .WithEnvironment("MQ_QMGR_NAME", "QM1")
    .WithEndpoint(1414, 1414)
    .WithHttpsEndpoint(9443, 9443)
    .WithLifetime(ContainerLifetime.Persistent)
    //.WithHttpsHealthCheck("/ibmmq/console/login.html")
    ;

builder.AddProject<Projects.MessageBridgeComponent>("Bridge")
    .WaitFor(ibmmq);

builder.AddProject<Projects.NServiceBus_IbmMq_Sales>("Sales")
        .WaitFor(ibmmq);

builder.AddProject<Projects.NServiceBus_IbmMq_Shipping>("Shipping")
        .WaitFor(ibmmq);


builder.Build().Run();
