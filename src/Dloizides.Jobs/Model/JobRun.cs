namespace Dloizides.Jobs.Model;

/// <summary>
/// One execution of a background job, carrying its PROVENANCE (who caused it, how, when), the LEASE that
/// keeps it single-flight and recoverable across replicas, and its durable PROGRESS — a checkpoint it can
/// resume from and a progress snapshot the UI and watchdog read. A row here is both the history entry and
/// the live running-job record: the trigger inserts it, the runner claims, checkpoints and completes it.
/// </summary>
/// <remarks>
/// <para>
/// SINGLE-FLIGHT: the row IS the lease. A partial unique index on <see cref="JobName"/> filtered to the
/// unfinished outcomes (<c>queued</c> and <c>running</c>) means the database itself rejects a second
/// concurrent run of the same job — two replicas racing to trigger produce one insert and one unique
/// violation. That index is the guarantee; an in-memory "is it running?" flag could not be, because a
/// service runs multiple replicas.
/// </para>
/// <para>
/// This is a plain POCO with no persistence attributes: the mapping (jsonb columns, the partial index,
/// the row-version) lives in <c>Dloizides.Jobs.EntityFrameworkCore</c> so the core stays storage-agnostic.
/// </para>
/// </remarks>
public class JobRun
{
    /// <summary>Stable identity of this run.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which job this run is of — the single-flight key the trigger, lease and status feed agree on.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>How this run came to be — a <see cref="JobTriggerSources"/> value (scheduled / manual / api).</summary>
    public string TriggerSource { get; set; } = JobTriggerSources.System;

    /// <summary>WHO caused this run: a subject claim for a manual trigger, or <c>system</c> for the timer.</summary>
    public string TriggeredBy { get; set; } = JobTriggerSources.SystemActor;

    /// <summary>When the trigger was ACCEPTED — distinct from <see cref="StartedAt"/>, since a run is queued
    /// to a background runner and may wait before a worker picks it up.</summary>
    public DateTimeOffset TriggeredAt { get; set; }

    /// <summary>The tenant whose admin triggered this run, or null for a platform-wide/scheduled run. These
    /// jobs are deployment-wide, so this records ACCOUNTABILITY, not scope.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The run's single opaque trigger input (e.g. <c>source=wikidata</c>), or null when it takes
    /// none. NOT part of the single-flight key for a <see cref="SingleFlightScope.Global"/> job: a different
    /// argument still contends for the one slot. For a <see cref="SingleFlightScope.PerArgument"/> job it is
    /// copied into <see cref="SingleFlightKey"/>.</summary>
    public string? Argument { get; set; }

    /// <summary>
    /// The second half of the single-flight key, stamped by the trigger: the empty string for a
    /// <see cref="SingleFlightScope.Global"/> job, and <c>Argument ?? ""</c> for a
    /// <see cref="SingleFlightScope.PerArgument"/> job. Never null, so a null argument cannot slip past a
    /// unique index that treats NULLs as distinct. Only persisted when the per-argument mapping is enabled.
    /// </summary>
    public string SingleFlightKey { get; set; } = string.Empty;

    /// <summary>One of <see cref="JobRunOutcomes"/>: <c>queued</c> -&gt; <c>running</c> -&gt;
    /// <c>completed</c>/<c>failed</c>/<c>cancelled</c>. The unfinished values are what the partial unique
    /// index filters on, so this field carries the concurrency guarantee.</summary>
    public string Outcome { get; set; } = JobRunOutcomes.Queued;

    /// <summary>When a worker actually began executing, or null while still queued. Preserved across a
    /// reclaim so elapsed time reflects the whole run, not just the resumed segment.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the run reached a terminal outcome, or null while queued/running.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The captured failure message when <see cref="Outcome"/> is <c>failed</c> — the real error,
    /// never swallowed.</summary>
    public string? Error { get; set; }

    /// <summary>Which instance holds the lease on this run, or null when unclaimed — an opaque per-process
    /// id (host + run-unique token) that makes a reclaim readable in the log.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>When the owner's lease expires. Heartbeated forward while the run progresses; if the owner
    /// dies the lease lapses and the run becomes reclaimable instead of stranded <c>running</c> forever.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Opaque resume state the job writes at intervals (e.g. <c>{ phase, cursor }</c>), mapped as
    /// jsonb by the EF store. On a reclaim the runner hands this back so the job continues where it left
    /// off rather than restarting. Null on a fresh run.</summary>
    public string? Checkpoint { get; set; }

    /// <summary>A serialized <c>{ phase, done, total, updatedAt }</c> snapshot for the UI and the watchdog,
    /// mapped as jsonb by the EF store. Null until the job first reports progress.</summary>
    public string? Progress { get; set; }
}
