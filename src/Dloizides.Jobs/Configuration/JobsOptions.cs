namespace Dloizides.Jobs.Configuration;

/// <summary>
/// Tunables for the runner and the watchdog. Bind from the <see cref="SectionName"/> config section and/or
/// override in the <c>AddDloizidesJobs</c> callback.
/// </summary>
public sealed class JobsOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Jobs";

    /// <summary>How often the runner looks for a claimable run. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the owner pushes its lease forward while a job runs. Default 30s. Keep it well BELOW
    /// <see cref="LeaseTtl"/> — the ratio is the safety margin: at 30s against a 120s TTL the owner can miss
    /// three consecutive beats (a GC pause, a slow query) and still hold its claim. A 1:1 ratio would make
    /// every single missed beat a reclaim, spawning a duplicate run — the exact failure this prevents.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a claim or heartbeat holds a run for. Default 120s. A dead owner's run becomes
    /// reclaimable this long after its last successful heartbeat. Independent of run LENGTH — only the gap
    /// between consecutive beats matters.</summary>
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>How often the watchdog sweeps for stale jobs. Default 5 minutes.</summary>
    public TimeSpan StalenessCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many recent runs the status query includes in a job's timeline history. Default 20.</summary>
    public int HistoryLimit { get; set; } = 20;
}
