using Dloizides.Jobs.Model;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// A background job the runner can execute, checkpoint, and RESUME. Implement one per job and register it
/// with <c>AddJob&lt;T&gt;()</c>; the runner owns the lease, heartbeat, single-flight and conflict-safe
/// transitions so the body only has to do the work and checkpoint its progress.
/// </summary>
/// <remarks>
/// AUTHOR RESPONSIBILITY — idempotency per checkpoint. Because a reclaim resumes from the last checkpoint,
/// re-running the segment AFTER that checkpoint must be safe (upsert, don't blindly insert). The runner
/// guarantees at-least-once execution of the tail; the job guarantees the tail is replay-safe.
/// </remarks>
public interface ICheckpointableJob
{
    /// <summary>The job's stable name — the single-flight key and the identity the trigger, lease, status
    /// feed and watchdog all agree on. Must be unique across registered jobs.</summary>
    string Name { get; }

    /// <summary>The expected schedule and staleness threshold that arm the watchdog. Use
    /// <see cref="JobCadence.None"/> for a purely on-demand job.</summary>
    JobCadence Cadence { get; }

    /// <summary>What this job's single-flight slot is keyed on. Defaults to <see cref="SingleFlightScope.Global"/>
    /// (one run per job name); override with <see cref="SingleFlightScope.PerArgument"/> to allow one run per
    /// distinct argument. Opt-in: an existing job that does not override this is unchanged.</summary>
    SingleFlightScope SingleFlightScope => SingleFlightScope.Global;

    /// <summary>
    /// The long-running body. Call <see cref="IJobContext.SaveCheckpointAsync"/> at intervals so a reclaim
    /// loses at most one interval of work, and <see cref="IJobContext.ReportProgressAsync"/> so the UI and
    /// watchdog can see how far it got. On resume, <see cref="IJobContext.LoadCheckpoint"/> returns the last
    /// saved state; on a fresh run it returns null.
    /// </summary>
    Task RunAsync(IJobContext context, CancellationToken cancellationToken);
}
