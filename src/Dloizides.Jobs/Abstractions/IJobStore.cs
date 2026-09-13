using Dloizides.Jobs.Model;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// The persistence seam — the single place the runner, trigger and status query touch storage. The
/// default implementation is EF Core + a relational database (<c>Dloizides.Jobs.EntityFrameworkCore</c>),
/// but the seam keeps the runner storage-shaped-but-swappable and, above all, TESTABLE.
/// </summary>
/// <remarks>
/// EVERY MUTATION IS COMPARE-AND-SET. Each write below is conditioned on the state the caller observed
/// (the outcome, the owner, the lease). "0 rows affected" is never an error — it means another party moved
/// the row first (a concurrent reclaim or completion) and is a BENIGN no-op: the caller drops the row
/// cleanly and carries on. That is the rule that ends the reclaim-vs-complete error loop, and it is why the
/// mutating methods return <see cref="bool"/> or a nullable row rather than throwing on contention.
/// <para>
/// SINGLE-FLIGHT SCOPE IS AN IMPLEMENTATION CONCERN, NOT A GUARANTEE OF THIS CONTRACT.
/// <see cref="SingleFlightScope.PerArgument"/> is honoured only by <c>EfJobStore</c> configured with
/// <c>perArgumentSingleFlight: true</c> (and its migration). Any other store — including <c>EfJobStore</c> on
/// the default mapping and custom stores that ignore the key — runs every job GLOBAL, one slot per job name.
/// <c>EfJobStore</c>'s misconfiguration guard keys on a non-empty argument, not on the job's scope, so a
/// PerArgument job enqueued with a null argument on the default mapping is NOT flagged and silently runs
/// global. Under the per-argument mapping a null argument shares the one <c>""</c> slot with every other
/// null/empty-argument run of that job.
/// </para>
/// </remarks>
public interface IJobStore
{
    // --- Trigger / enqueue -----------------------------------------------------------------------------

    /// <summary>
    /// Insert a queued run, enforcing single-flight. If a run of this job already occupies the slot (queued
    /// or running under a live lease) this does NOT insert and reports the occupier. A lapsed occupier is
    /// reclaimed (failed) and the slot freed first. Returns the accepted run, or the occupier when rejected.
    /// </summary>
    Task<JobEnqueueResult> EnqueueAsync(JobRun run, CancellationToken cancellationToken);

    /// <summary>The run currently holding this job's single-flight slot (queued or running), if any.</summary>
    Task<JobRun?> FindOccupyingRunAsync(string jobName, CancellationToken cancellationToken);

    // --- Runner lifecycle (all compare-and-set) --------------------------------------------------------

    /// <summary>
    /// Atomically claim the oldest claimable run — a <c>queued</c> one, or a <c>running</c> one whose lease
    /// lapsed (reclaim). Sets it <c>running</c> under <paramref name="owner"/> with a fresh lease and, for a
    /// fresh run, stamps <see cref="JobRun.StartedAt"/> (preserved on a reclaim). Returns the claimed run —
    /// carrying any existing <see cref="JobRun.Checkpoint"/> so the runner can RESUME — or null when nothing
    /// is claimable or another worker won the race.
    /// </summary>
    Task<JobRun?> ClaimNextAsync(
        string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken);

    /// <summary>Push the lease forward while the run is still <c>running</c> under <paramref name="owner"/>.
    /// False = we no longer own it; stop heartbeating.</summary>
    Task<bool> HeartbeatAsync(
        Guid runId, string owner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken);

    /// <summary>Persist resume state while the run is still owned. False = reclaimed; benign no-op.</summary>
    Task<bool> SaveCheckpointAsync(
        Guid runId, string owner, string checkpoint, CancellationToken cancellationToken);

    /// <summary>Persist the progress snapshot while the run is still owned. False = reclaimed; benign no-op.</summary>
    Task<bool> ReportProgressAsync(
        Guid runId, string owner, string progress, CancellationToken cancellationToken);

    /// <summary>
    /// Record the terminal outcome as a compare-and-set: only while the run is still <c>running</c> under
    /// <paramref name="owner"/>. Releases the lease so the next poll need not wait out the TTL. True = we
    /// recorded it; false = it was reclaimed out from under us and the reclaimer's outcome stands.
    /// </summary>
    Task<bool> CompleteAsync(
        Guid runId, string owner, string outcome, string? error, DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    // --- Status / query --------------------------------------------------------------------------------

    /// <summary>Load a single run by id, or null.</summary>
    Task<JobRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>When this job last reached <c>completed</c> — the true data-freshness marker the watchdog
    /// measures against, distinct from the last run of any outcome. Null if it never succeeded.</summary>
    Task<DateTimeOffset?> GetLastSuccessAtAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>The most recent finished run of this job (any terminal outcome) — the console's "last run".</summary>
    Task<JobRun?> GetLastFinishedRunAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>The most recent runs of this job, newest first, for the per-run history/timeline.</summary>
    Task<IReadOnlyList<JobRun>> GetRecentRunsAsync(string jobName, int limit, CancellationToken cancellationToken);
}

/// <summary>The outcome of <see cref="IJobStore.EnqueueAsync"/>.</summary>
/// <param name="Accepted">True when the run was queued; false when the slot was already occupied.</param>
/// <param name="Run">The accepted run when <paramref name="Accepted"/>, otherwise the occupying run.</param>
public readonly record struct JobEnqueueResult(bool Accepted, JobRun Run)
{
    /// <summary>A run was queued.</summary>
    public static JobEnqueueResult Queued(JobRun run) => new(true, run);

    /// <summary>The slot was already occupied; <paramref name="occupier"/> holds it.</summary>
    public static JobEnqueueResult AlreadyRunning(JobRun occupier) => new(false, occupier);
}
