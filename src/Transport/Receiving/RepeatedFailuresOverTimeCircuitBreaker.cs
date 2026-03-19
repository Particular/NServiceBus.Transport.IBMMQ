namespace NServiceBus.Transport.IBMMQ;

using Logging;

sealed class RepeatedFailuresOverTimeCircuitBreaker : IDisposable
{
    const int Disarmed = 0;
    const int Armed = 1;
    const int Triggered = 2;

    static readonly TimeSpan NoPeriodicTriggering = TimeSpan.FromMilliseconds(-1);
    static readonly ILog Logger = LogManager.GetLogger<RepeatedFailuresOverTimeCircuitBreaker>();

    readonly string name;
    readonly Action<Exception> triggerAction;
    readonly Action armedAction;
    readonly Action disarmedAction;
    readonly TimeSpan timeToWaitBeforeTriggering;
    readonly TimeSpan timeToWaitWhenTriggered;
    readonly TimeSpan timeToWaitWhenArmed;
    readonly Timer timer;
    readonly Lock stateLock = new();

    int circuitBreakerState = Disarmed;
    Exception? lastException;

    public RepeatedFailuresOverTimeCircuitBreaker(
        string name,
        TimeSpan timeToWaitBeforeTriggering,
        Action<Exception> triggerAction,
        Action? armedAction = null,
        Action? disarmedAction = null,
        TimeSpan? timeToWaitWhenTriggered = null,
        TimeSpan? timeToWaitWhenArmed = null)
    {
        this.name = name;
        this.triggerAction = triggerAction;
        this.armedAction = armedAction ?? (() => { });
        this.disarmedAction = disarmedAction ?? (() => { });
        this.timeToWaitBeforeTriggering = timeToWaitBeforeTriggering;
        this.timeToWaitWhenTriggered = timeToWaitWhenTriggered ?? TimeSpan.FromSeconds(10);
        this.timeToWaitWhenArmed = timeToWaitWhenArmed ?? TimeSpan.FromSeconds(1);

        timer = new Timer(CircuitBreakerTriggered);
    }

    public void Success()
    {
        if (Volatile.Read(ref circuitBreakerState) == Disarmed)
        {
            return;
        }

        lock (stateLock)
        {
            if (circuitBreakerState == Disarmed)
            {
                return;
            }

            circuitBreakerState = Disarmed;

            timer.Change(Timeout.Infinite, Timeout.Infinite);
            Logger.InfoFormat("The circuit breaker for '{0}' is now disarmed.", name);

            try
            {
                disarmedAction();
            }
            catch (Exception ex)
            {
                Logger.Error($"The circuit breaker for '{name}' was unable to execute the disarm action.", ex);
                throw;
            }
        }
    }

    public Task Failure(Exception exception, CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref lastException, exception);

        var previousState = Volatile.Read(ref circuitBreakerState);
        if (previousState is Armed or Triggered)
        {
            return Delay(previousState, cancellationToken);
        }

        lock (stateLock)
        {
            previousState = circuitBreakerState;
            if (previousState is Armed or Triggered)
            {
                return Delay(previousState, cancellationToken);
            }

            circuitBreakerState = Armed;

            try
            {
                armedAction();
            }
            catch (Exception ex)
            {
                Logger.Error($"The circuit breaker for '{name}' was unable to execute the arm action.",
                    new AggregateException(ex, exception));
                throw;
            }

            timer.Change(timeToWaitBeforeTriggering, NoPeriodicTriggering);
            Logger.WarnFormat(
                "The circuit breaker for '{0}' is now in the armed state due to '{1}' and might trigger in '{2}' when not disarmed.",
                name, exception, timeToWaitBeforeTriggering);
        }

        return Delay(Disarmed, cancellationToken);
    }

    Task Delay(int previousState, CancellationToken cancellationToken)
    {
        var timeToWait = previousState == Triggered ? timeToWaitWhenTriggered : timeToWaitWhenArmed;

        if (Logger.IsDebugEnabled)
        {
            Logger.DebugFormat("The circuit breaker for '{0}' is delaying the operation by '{1}'.",
                name, timeToWait);
        }

        return Task.Delay(timeToWait, cancellationToken);
    }

    void CircuitBreakerTriggered(object? state)
    {
        if (Volatile.Read(ref circuitBreakerState) == Disarmed)
        {
            return;
        }

        lock (stateLock)
        {
            if (circuitBreakerState == Disarmed)
            {
                return;
            }

            circuitBreakerState = Triggered;
            Logger.WarnFormat("The circuit breaker for '{0}' will now be triggered with exception '{1}'.",
                name, lastException);

            try
            {
                triggerAction(lastException!);
            }
            catch (Exception ex)
            {
                Logger.Fatal($"The circuit breaker for '{name}' was unable to execute the trigger action.",
                    new AggregateException(ex, lastException!));
            }
        }
    }

    public void Dispose() => timer.Dispose();
}
