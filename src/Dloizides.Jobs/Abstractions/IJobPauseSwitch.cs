namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// Reads the live pause list (<c>Jobs:Paused</c>). The trigger refuses an unattended (<c>scheduled</c> /
/// <c>system</c>) run of a paused job, the runner cancels one that was queued before the pause landed, and
/// the meter exports <c>jobs_paused</c>. A service's own scheduler loop can ask it too, to skip a tick.
/// </summary>
public interface IJobPauseSwitch
{
    /// <summary>Whether <paramref name="jobName"/> is paused right now (ordinal match).</summary>
    bool IsPaused(string jobName);

    /// <summary>The job names paused right now.</summary>
    IReadOnlyCollection<string> PausedJobs { get; }
}
