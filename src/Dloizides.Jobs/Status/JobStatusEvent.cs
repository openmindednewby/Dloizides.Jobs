namespace Dloizides.Jobs.Status;

/// <summary>
/// The lightweight NOTIFICATION that crosses an <see cref="Abstractions.IJobStatusBackplane"/> — "job X
/// changed, here is the kind and the run". It is deliberately NOT a <see cref="JobStatus"/>: a full status
/// (with its timeline) is heavy and, over a Postgres <c>NOTIFY</c>, would risk the 8000-byte payload cap.
/// Instead the wire layer treats this as a hint and re-reads the DURABLE <see cref="JobStatus"/> from the
/// store, so a push client and a poll client always render the same truth — the persist-first-push-second
/// rule made concrete.
/// </summary>
/// <param name="Job">The job whose status changed — the key the wire re-queries and filters clients on.</param>
/// <param name="Kind">Why it changed — a <see cref="JobStatusEventKinds"/> value (or a terminal outcome).
/// Carried only as an event name / hint; the authoritative state still comes from the store re-read.</param>
/// <param name="RunId">The run that moved, for the subscriber's own logging/correlation.</param>
/// <param name="OccurredAt">When the change was published (after the store write committed).</param>
public sealed record JobStatusEvent(string Job, string Kind, Guid RunId, DateTimeOffset OccurredAt);

/// <summary>
/// The well-known <see cref="JobStatusEvent.Kind"/> values. A terminal transition carries the run's outcome
/// string (<c>completed</c>/<c>failed</c>/<c>cancelled</c>) directly, so these name only the non-terminal
/// transitions plus the two claim flavours.
/// </summary>
public static class JobStatusEventKinds
{
    /// <summary>A run was accepted and queued (published after the enqueue insert committed).</summary>
    public const string Queued = "queued";

    /// <summary>A fresh run was claimed under a lease (published after the claim CAS landed).</summary>
    public const string Claimed = "claimed";

    /// <summary>A stranded run was reclaimed and is resuming from its checkpoint.</summary>
    public const string Reclaimed = "reclaimed";

    /// <summary>A progress snapshot was persisted (published only when the owning CAS write landed).</summary>
    public const string Progress = "progress";

    /// <summary>A checkpoint was persisted (published only when the owning CAS write landed).</summary>
    public const string Checkpoint = "checkpoint";
}
