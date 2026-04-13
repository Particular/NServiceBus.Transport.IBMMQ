using McMaster.Extensions.CommandLineUtils;
using NServiceBus.Transport.IBMMQ.CommandLine;

var app = new CommandLineApplication { Name = "ibmmq-transport" };

var connectionOptions = ConnectionOptions.Register(app);

app.HelpOption();
EndpointCommand.Register(app, connectionOptions);
QueueCommand.Register(app, connectionOptions);
app.OnExecute(() => app.ShowHelp());

return app.Execute(args);
