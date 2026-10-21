#nullable enable

namespace NServiceBus.Transport.IBMMQ.Tests;

using IBM.WMQ;
using IBM.WMQ.PCF;
using NServiceBus.Logging;
using NUnit.Framework;

[TestFixture]
[Category("RequiresBroker")]
public class BackoutCountTests
{
    const string QueueName = "NSB.TESTS.BACKOUTCOUNT";

    [OneTimeSetUp]
    public void Setup()
    {
        BrokerRequirement.Verify();
        TearDown();
        RunPcf(MQC.MQCMD_CREATE_Q, command => command.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL), MQC.MQRCCF_OBJECT_ALREADY_EXISTS);
    }

    [OneTimeTearDown]
    public void TearDown() => RunPcf(MQC.MQCMD_DELETE_Q, command => command.AddParameter(MQC.MQIACF_PURGE, MQC.MQPO_YES), MQC.MQRC_UNKNOWN_OBJECT_NAME);

    [TestCase(TransportTransactionMode.ReceiveOnly)]
    public async Task Redelivery_invokes_recoverability_before_the_handler(TransportTransactionMode mode)
    {
        PutAndBackout(backouts: 2);
        var calls = new List<string>();

        await ReceiveOnce(
            mode,
            (_, _) =>
            {
                calls.Add("message");
                return Task.CompletedTask;
            },
            (errorContext, _) =>
            {
                calls.Add($"error {errorContext.ImmediateProcessingFailures}: {errorContext.Exception.Message}");
                return Task.FromResult(ErrorHandleResult.RetryRequired);
            },
            TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0], Does.StartWith("error 2:").And.Contain("backed out 2 time(s)"));
        Assert.That(calls[1], Is.EqualTo("message"));
    }

    static async Task ReceiveOnce(TransportTransactionMode mode, OnMessage onMessage, OnError onError, CancellationToken cancellationToken)
    {
        var context = new ReceiveContext(QueueName, 0, onMessage, onError, (_, _, _) => { });
        var converter = new IBMMQMessageConverter(new MqPropertyNameEncoder(), 1208);
        var log = LogManager.GetLogger<ReceiveStrategy>();
        ReceiveStrategy strategy = mode switch
        {
            TransportTransactionMode.ReceiveOnly => new ReceiveOnlyReceiveStrategy(log, converter, context),
            TransportTransactionMode.None or TransportTransactionMode.SendsAtomicWithReceive or TransportTransactionMode.TransactionScope or _ =>
                throw new ArgumentOutOfRangeException(nameof(mode))
        };

        using var connection = new MqConnection(LogManager.GetLogger<MqConnection>(), TestBrokerConnection.Connect(), name => name, (_, _) => { }, 100);
        using var queue = connection.OpenInputQueue(QueueName);
        var getOptions = new MQGetMessageOptions { Options = strategy.GetOptionsFlags, WaitInterval = 1000 };

        Assert.That(await strategy.ReceiveMessage(queue, getOptions, connection, cancellationToken).ConfigureAwait(false), Is.True);
    }

    static void PutAndBackout(int backouts)
    {
        using var qm = TestBrokerConnection.Connect();
        using (var output = qm.AccessQueue(QueueName, MQC.MQOO_OUTPUT))
        {
            var message = new MQMessage();
            message.WriteString("payload");
            output.Put(message);
        }

        using var input = qm.AccessQueue(QueueName, MQC.MQOO_INPUT_AS_Q_DEF);
        for (var i = 0; i < backouts; i++)
        {
            input.Get(new MQMessage(), new MQGetMessageOptions { Options = MQC.MQGMO_SYNCPOINT });
            qm.Backout();
        }
    }

    static void RunPcf(int commandId, Action<PCFMessage> addParameters, int ignoredReason)
    {
        using var qm = TestBrokerConnection.Connect();
        var agent = new PCFMessageAgent(qm);
        try
        {
            var command = new PCFMessage(commandId);
            command.AddParameter(MQC.MQCA_Q_NAME, QueueName);
            addParameters(command);
            agent.Send(command);
        }
        catch (PCFException e) when (e.ReasonCode == ignoredReason)
        {
        }
        finally
        {
            agent.Disconnect();
        }
    }
}
