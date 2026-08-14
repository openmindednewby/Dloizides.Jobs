using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// The cross-pod FAN-OUT seam for real-time status PUSH — the scale-out half of the status story. With N
/// replicas a job runs (and reports progress) on pod B while a browser's stream is attached to pod A, so a
/// change on B must reach EVERY pod. An implementation delivers a published <see cref="JobStatusEvent"/> to
/// every subscriber across every replica; each pod then re-reads the durable <see cref="JobStatus"/> and
/// pushes it to its own connected clients.
/// </summary>
/// <remarks>
/// <para>
/// PUSH IS NEVER LOAD-BEARING. This seam only NOTIFIES; correctness lives entirely in the store (persist
/// first, push second). A dropped notification or a blipped connection is healed by the next poll or a
/// reconnect-then-snapshot, because both the push path and the poll path read the same
/// <see cref="IJobStatusQuery"/> truth. An implementation may therefore drop events under load rather than
/// block a job — and <see cref="PublishAsync"/> must never throw the job body's persistence call off course
/// (the runtime wraps every publish, but a well-behaved impl fails soft too).
/// </para>
/// <para>
/// The default is <c>NullJobStatusBackplane</c> (no push; poll-only) — always works, zero dependencies. The
/// selection is config-driven (<c>Jobs:Status:Backplane</c>) via a registration list, so a new transport
/// (Postgres <c>LISTEN</c>/<c>NOTIFY</c>, Redis pub/sub, …) is an interface impl plus one config line, with
/// no core change and NO assumption baked in here about the underlying transport.
/// </para>
/// </remarks>
public interface IJobStatusBackplane
{
    /// <summary>
    /// Broadcast a status change to every subscriber on every replica. Called AFTER the corresponding store
    /// write has committed. Best-effort by contract: an implementation should fail soft (log and move on)
    /// rather than propagate a transport blip into the job path.
    /// </summary>
    Task PublishAsync(JobStatusEvent evt, CancellationToken cancellationToken);

    /// <summary>
    /// Register a handler invoked for every event this replica receives (its own publishes and those from
    /// other replicas). Dispose the returned handle to unsubscribe — a wire connection subscribes on open and
    /// disposes on close. Handlers must be cheap and non-throwing; an implementation isolates a throwing
    /// handler so one bad subscriber cannot starve the others.
    /// </summary>
    IDisposable Subscribe(Action<JobStatusEvent> handler);
}
