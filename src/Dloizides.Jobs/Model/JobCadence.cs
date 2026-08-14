namespace Dloizides.Jobs.Model;

/// <summary>
/// A job's expected schedule and the staleness threshold the watchdog alarms on. A job that has not
/// succeeded within <see cref="StalenessThreshold"/> is stale — the missing alarm on 2026-08-13, where a
/// 2-day-stale ingest never paged, is exactly what this drives.
/// </summary>
/// <param name="ExpectedEvery">How often the job is expected to succeed. Informational for the UI; the
/// watchdog decides on <see cref="StalenessThreshold"/>.</param>
/// <param name="StalenessThreshold">How long <c>lastSuccessAt</c> may age before the job is stale. A
/// non-positive value means the job is NOT watched (an on-demand job with no cadence).</param>
public readonly record struct JobCadence(TimeSpan ExpectedEvery, TimeSpan StalenessThreshold)
{
    /// <summary>An unwatched cadence — a purely on-demand job the staleness monitor skips.</summary>
    public static readonly JobCadence None = new(TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>Whether the staleness monitor should alarm on this job. False for on-demand jobs.</summary>
    public bool IsWatched => StalenessThreshold > TimeSpan.Zero;

    /// <summary>
    /// A cadence that expects a success every <paramref name="every"/> and alarms after
    /// <paramref name="staleAfter"/> (default: twice <paramref name="every"/>, so a single missed cycle is
    /// tolerated before paging).
    /// </summary>
    public static JobCadence Every(TimeSpan every, TimeSpan? staleAfter = null) =>
        new(every, staleAfter ?? every + every);
}
