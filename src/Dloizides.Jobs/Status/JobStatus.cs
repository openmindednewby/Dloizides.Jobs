namespace Dloizides.Jobs.Status;

/// <summary>
/// The stable, per-job status shape the running-jobs console renders as a live panel — phase + %-bar, last
/// checkpoint / resumable-from, stale badge, recent error, and a per-run timeline. Mirrors §8 of the
/// background-jobs standard so a non-engineer sees "Ingest, phase 3/6, 45%, last checkpoint 2 min ago".
/// </summary>
/// <param name="Job">The job name.</param>
/// <param name="State">One of <c>queued</c> / <c>running</c> / <c>completed</c> / <c>failed</c> /
/// <c>cancelled</c> / <c>idle</c> (idle = never run).</param>
/// <param name="Progress">The latest progress snapshot, or null if none reported yet.</param>
/// <param name="Checkpoint">The last checkpoint written, or null if none.</param>
/// <param name="LastSuccessAt">When the job last completed successfully, or null.</param>
/// <param name="Stale">True when the job is watched and overdue past its cadence threshold.</param>
/// <param name="Lease">The current owner + expiry while running, or null when not running.</param>
/// <param name="RecentError">The most recent captured failure message, or null.</param>
/// <param name="Timeline">The per-run timeline, oldest event first.</param>
public sealed record JobStatus(
    string Job,
    string State,
    JobProgressView? Progress,
    JobCheckpointView? Checkpoint,
    DateTimeOffset? LastSuccessAt,
    bool Stale,
    JobLeaseView? Lease,
    string? RecentError,
    IReadOnlyList<JobTimelineEntry> Timeline);

/// <summary>Structured progress: a phase label and a done/total pair, with the derived percentage.</summary>
/// <param name="Phase">Free-form phase label (e.g. <c>leaders/FR</c>).</param>
/// <param name="Done">Items processed so far.</param>
/// <param name="Total">Total items in this phase, or 0 when unknown.</param>
/// <param name="Pct">Derived <c>done*100/total</c>, clamped to 0..100 (0 when total is 0).</param>
/// <param name="UpdatedAt">When this snapshot was reported.</param>
public sealed record JobProgressView(string Phase, long Done, long Total, int Pct, DateTimeOffset UpdatedAt);

/// <summary>The last checkpoint: when it was written and the phase the job would resume from.</summary>
/// <param name="At">When the checkpoint was saved.</param>
/// <param name="ResumableFrom">The phase a reclaim would continue from (the progress phase, or <c>start</c>).</param>
public sealed record JobCheckpointView(DateTimeOffset At, string ResumableFrom);

/// <summary>The live lease: who owns the running run and when their claim expires.</summary>
/// <param name="Owner">The owning instance id.</param>
/// <param name="ExpiresAt">When the lease lapses if not heartbeated.</param>
public sealed record JobLeaseView(string Owner, DateTimeOffset ExpiresAt);

/// <summary>One event on a run's timeline (queued, started, checkpoint, completed, ...).</summary>
/// <param name="At">When the event occurred.</param>
/// <param name="Event">The event name.</param>
/// <param name="Phase">The phase in scope at the time, when relevant.</param>
public sealed record JobTimelineEntry(DateTimeOffset At, string Event, string? Phase = null);

/// <summary>A job the watchdog found stale, with the numbers behind the alarm.</summary>
/// <param name="Job">The stale job's name.</param>
/// <param name="LastSuccessAt">When it last succeeded, or null if never.</param>
/// <param name="StaleFor">How long it has been overdue past its threshold.</param>
/// <param name="Threshold">The cadence staleness threshold it breached.</param>
public sealed record StaleJob(
    string Job, DateTimeOffset? LastSuccessAt, TimeSpan StaleFor, TimeSpan Threshold);
