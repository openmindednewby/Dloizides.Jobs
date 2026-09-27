namespace Dloizides.Jobs.Configuration;

/// <summary>
/// The runtime pause switch (JOBS-CTL-1d): the job names listed under <c>Jobs:Paused</c>. Bound through
/// <c>IOptionsMonitor</c>, so a change to the source — the mounted <c>jobs-control</c> ConfigMap, see
/// <c>AddDloizidesJobsControl</c> — takes effect on the next check with no restart. Kept apart from
/// <see cref="JobsOptions"/>, which is read once at startup.
/// </summary>
public sealed class JobPauseOptions
{
    /// <summary>
    /// Job names (<see cref="Abstractions.ICheckpointableJob.Name"/>, ordinal match) that must not run on
    /// schedule. A paused job still runs when triggered by hand (<c>manual</c> / <c>api</c>).
    /// </summary>
    public List<string> Paused { get; set; } = [];
}
