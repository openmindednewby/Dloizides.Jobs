using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// The watchdog: periodically checks every WATCHED job (one with a <see cref="Model.JobCadence"/> that has
/// a staleness threshold) and raises an alarm when its <c>lastSuccessAt</c> exceeds the threshold. This is
/// the alarm that was missing on 2026-08-13, when a 2-day-stale ingest never paged.
/// </summary>
public interface IJobStalenessMonitor
{
    /// <summary>
    /// Run one staleness sweep and return the jobs found stale (having raised the alarm for each). The
    /// deterministic seam: the background loop calls it on its interval, tests call it directly.
    /// </summary>
    Task<IReadOnlyList<StaleJob>> CheckOnceAsync(CancellationToken cancellationToken);
}
