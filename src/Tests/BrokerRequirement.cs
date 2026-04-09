namespace NServiceBus.Transport.IBMMQ.Tests;

using NUnit.Framework;

/// <summary>
/// Validates that an IBM MQ broker is reachable before running broker-dependent tests.
/// Call <see cref="Verify"/> from a [OneTimeSetUp] method. The result is cached so
/// only the first call performs a connection attempt.
/// </summary>
static class BrokerRequirement
{
    const int Unknown = 0;
    const int Available = 1;
    const int Unavailable = 2;

    static int state = Unknown;

    public static void Verify()
    {
        var current = Volatile.Read(ref state);

        if (current == Unavailable)
        {
            Assert.Inconclusive(
                "IBM MQ broker is not available. These tests require a running broker. " +
                "Set the IBMMQ_CONNECTIONSTRING environment variable or start a local broker.");
        }

        if (current == Available)
        {
            return;
        }

        try
        {
            using var qm = TestBrokerConnection.Connect();
            qm.Disconnect();
            Interlocked.Exchange(ref state, Available);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref state, Unavailable);
            Assert.Inconclusive(
                $"IBM MQ broker is not available: {ex.Message}. " +
                "Set the IBMMQ_CONNECTIONSTRING environment variable or start a local broker.");
        }
    }
}
