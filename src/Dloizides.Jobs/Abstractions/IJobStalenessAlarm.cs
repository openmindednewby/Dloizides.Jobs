using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// Where a staleness alarm goes. The default logs a warning (which flows to the Daily Environment Report /
/// status hub); a service can replace it to page, post to the status hub directly, or raise a metric.
/// </summary>
public interface IJobStalenessAlarm
{
    /// <summary>Raise the alarm for one stale job. Called once per sweep per stale job.</summary>
    Task RaiseAsync(StaleJob job, CancellationToken cancellationToken);
}
