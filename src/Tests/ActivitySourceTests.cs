namespace NServiceBus.Transport.IBMMQ.Tests;

using System.Diagnostics;
using NUnit.Framework;

[TestFixture]
public class ActivitySourceTests
{
    [Test]
    public void Has_expected_name()
    {
        Assert.That(ActivitySources.Main.Name, Is.EqualTo("NServiceBus.Transport.IBMMQ"));
    }

    [Test]
    public void Has_version()
    {
        Assert.That(ActivitySources.Main.Version, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Version_does_not_contain_build_metadata()
    {
        Assert.That(ActivitySources.Main.Version, Does.Not.Contain("+"));
    }

    [Test]
    public void Name_constant_matches_activity_source()
    {
        Assert.That(ActivitySources.Main.Name, Is.EqualTo(ActivitySources.Name));
    }

    [Test]
    [TestCase(ActivitySources.Receive)]
    [TestCase(ActivitySources.Dispatch)]
    [TestCase(ActivitySources.PutToQueue)]
    [TestCase(ActivitySources.PutToTopic)]
    [TestCase(ActivitySources.Attempt)]
    public void Activity_names_are_prefixed_with_source_name(string activityName)
    {
        Assert.That(activityName, Does.StartWith(ActivitySources.Name));
    }

    [Test]
    public void Listener_receives_activities()
    {
        var stoppedActivities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivitySources.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        using (ActivitySources.Main.StartActivity(ActivitySources.Dispatch, ActivityKind.Internal))
        {
        }

        Assert.That(stoppedActivities, Has.Count.EqualTo(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stoppedActivities[0].OperationName, Is.EqualTo(ActivitySources.Dispatch));
            Assert.That(stoppedActivities[0].Kind, Is.EqualTo(ActivityKind.Internal));
        }
    }
}
