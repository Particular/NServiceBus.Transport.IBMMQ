namespace NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;

static class HandlerCompletion
{
    static int target;
    static int currentCount;
    static TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    static TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task Completion => completionSource.Task;

    public static int CurrentCount => Volatile.Read(ref currentCount);

    public static void Reset(int targetCount)
    {
        target = targetCount;
        currentCount = 0;
        completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static void OpenGate() => gate.TrySetResult();

    public static Task WaitForGate(CancellationToken cancellationToken = default) => gate.Task.WaitAsync(cancellationToken);

    public static void SignalOne()
    {
        if (Interlocked.Increment(ref currentCount) >= target)
        {
            completionSource.TrySetResult();
        }
    }
}
