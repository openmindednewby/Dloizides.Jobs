namespace Dloizides.Jobs.Model;

/// <summary>
/// The lifecycle states of a <see cref="JobRun"/>. Stored as-is, so the wire value, the column and the
/// single-flight index filter all agree.
/// </summary>
public static class JobRunOutcomes
{
    /// <summary>Accepted and queued; no worker has claimed it yet. Occupies the single-flight slot.</summary>
    public const string Queued = "queued";

    /// <summary>A worker holds the lease and is executing it. The other value the single-flight index
    /// filters on.</summary>
    public const string Running = "running";

    /// <summary>Finished successfully — the state that advances <c>lastSuccessAt</c> and clears the
    /// watchdog.</summary>
    public const string Completed = "completed";

    /// <summary>Finished with a captured error in <see cref="JobRun.Error"/>.</summary>
    public const string Failed = "failed";

    /// <summary>Terminated on request before completing.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The states that occupy the single-flight slot — a job is "already running" in either.</summary>
    public static bool OccupiesSlot(string? outcome) => outcome is Queued or Running;

    /// <summary>Whether <paramref name="outcome"/> is a terminal (finished) state.</summary>
    public static bool IsTerminal(string? outcome) => outcome is Completed or Failed or Cancelled;
}
