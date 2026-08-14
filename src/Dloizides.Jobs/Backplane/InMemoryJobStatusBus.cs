using System.Collections.Concurrent;
using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Backplane;

/// <summary>
/// A thread-safe local fan-out hub: N subscribers, deliver-to-all, with a throwing handler isolated so it
/// cannot starve the others. It is the shared core of every in-process backplane — the
/// <see cref="InMemoryJobStatusBackplane"/> uses it directly, and a cross-replica backplane (e.g. the Postgres
/// <c>LISTEN</c>/<c>NOTIFY</c> one) reuses it as its LOCAL delivery leg: the transport carries an event onto
/// the pod, then this hub fans it out to that pod's connected clients.
/// </summary>
public sealed class InMemoryJobStatusBus
{
    private readonly ConcurrentDictionary<Guid, Action<JobStatusEvent>> _handlers = new();

    /// <summary>Register a handler; dispose the handle to unsubscribe. Safe to call concurrently.</summary>
    public IDisposable Subscribe(Action<JobStatusEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.NewGuid();
        _handlers[id] = handler;
        return new Subscription(this, id);
    }

    /// <summary>Deliver an event to every current subscriber. A handler that throws is swallowed so one bad
    /// subscriber cannot break delivery to the rest — push is best-effort by contract.</summary>
    public void Publish(JobStatusEvent evt)
    {
        foreach (var handler in _handlers.Values)
        {
            try
            {
                handler(evt);
            }
            catch
            {
                // Best-effort local delivery: an individual handler fault never stops the fan-out.
            }
        }
    }

    private void Unsubscribe(Guid id) => _handlers.TryRemove(id, out _);

    private sealed class Subscription : IDisposable
    {
        private readonly InMemoryJobStatusBus _bus;
        private readonly Guid _id;
        private int _disposed;

        public Subscription(InMemoryJobStatusBus bus, Guid id)
        {
            _bus = bus;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _bus.Unsubscribe(_id);
            }
        }
    }
}
