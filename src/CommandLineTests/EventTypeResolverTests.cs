namespace NServiceBus.Transport.IBMMQ.CommandLine.Tests;

using System.Reflection;
using NUnit.Framework;

[TestFixture]
public class EventTypeResolverTests
{
    static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

    [Test]
    public void Resolve_finds_type_in_assembly()
    {
        var names = EventTypeResolver.Resolve("NServiceBus.Transport.IBMMQ.CommandLine.Tests.BaseEvent", AssemblyPath);

        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.BaseEvent"));
    }

    [Test]
    public void Resolve_throws_for_unknown_type()
    {
        Assert.That(
            () => EventTypeResolver.Resolve("DoesNot.Exist", AssemblyPath),
            Throws.InvalidOperationException.With.Message.Contains("not found"));
    }

    [Test]
    public void Resolve_returns_only_leaf_type_when_no_derived_types()
    {
        var names = EventTypeResolver.Resolve("NServiceBus.Transport.IBMMQ.CommandLine.Tests.LeafEvent", AssemblyPath);

        Assert.That(names, Is.EqualTo(["NServiceBus.Transport.IBMMQ.CommandLine.Tests.LeafEvent"]));
    }

    [Test]
    public void Resolve_includes_derived_types()
    {
        var names = EventTypeResolver.Resolve("NServiceBus.Transport.IBMMQ.CommandLine.Tests.BaseEvent", AssemblyPath);

        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.BaseEvent"));
        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.DerivedEvent"));
    }

    [Test]
    public void Resolve_includes_interface_implementors()
    {
        var names = EventTypeResolver.Resolve("NServiceBus.Transport.IBMMQ.CommandLine.Tests.ITestEvent", AssemblyPath);

        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.ITestEvent"));
        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.BaseEvent"));
        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.DerivedEvent"));
        Assert.That(names, Does.Contain("NServiceBus.Transport.IBMMQ.CommandLine.Tests.LeafEvent"));
    }
}

// Test type hierarchy
public interface ITestEvent;
public class BaseEvent : ITestEvent;
public class DerivedEvent : BaseEvent;
public class LeafEvent : ITestEvent;
