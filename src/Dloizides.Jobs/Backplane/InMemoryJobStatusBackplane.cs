using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Backplane;

/// <summary>
/// An in-process backplane: publish and subscribe both go through one shared <see cref="InMemoryJobStatusBus"/>.
/// Real push, zero dependencies — but WITHIN A SINGLE PROCESS only, so it is right for a single-replica
/// deployment (or local dev) and for tests, and wrong for a scaled-out one (a browser on pod A never sees a
/// job on pod B). For cross-replica push choose a transport-backed backplane (<c>Postgres</c>).
/// </summary>
/// <remarks>
/// Two instances constructed over the SAME bus fan out to each other, which is how a test stands in "two pods
/// on one transport" deterministically without a broker.
/// </remarks>
public sealed class InMemoryJobStatusBackplane : IJobStatusBackplane
{
    private readonly InMemoryJobStatusBus _bus;

    /// <summary>Construct over a shared bus (registered as a singleton, so every consumer shares one).</summary>
    public InMemoryJobStatusBackplane(InMemoryJobStatusBus bus) => _bus = bus;

    /// <inheritdoc />
    public Task PublishAsync(JobStatusEvent evt, CancellationToken cancellationToken)
    {
        _bus.Publish(evt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<JobStatusEvent> handler) => _bus.Subscribe(handler);
}
