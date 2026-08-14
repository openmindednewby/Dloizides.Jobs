using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Backplane;

/// <summary>
/// The DEFAULT backplane: a no-op. Publishing drops the event; subscribing hands back a disposable that does
/// nothing. Push is off, poll is unaffected — a status client simply keeps polling <see cref="IJobStatusQuery"/>
/// as it always has. Zero dependencies, always available; it is why "add real-time" never becomes "and now the
/// service won't start without a message bus".
/// </summary>
public sealed class NullJobStatusBackplane : IJobStatusBackplane
{
    /// <summary>The shared instance — it holds no state, so one suffices.</summary>
    public static readonly NullJobStatusBackplane Instance = new();

    /// <inheritdoc />
    public Task PublishAsync(JobStatusEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public IDisposable Subscribe(Action<JobStatusEvent> handler) => NullSubscription.Instance;

    private sealed class NullSubscription : IDisposable
    {
        public static readonly NullSubscription Instance = new();

        public void Dispose()
        {
            // Nothing was subscribed; nothing to release.
        }
    }
}
