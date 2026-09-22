using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Metrics;
using Dloizides.Jobs.Status;
using Microsoft.Extensions.Logging;

namespace Dloizides.Jobs.Services;

/// <summary>
/// The default staleness alarm: a structured warning. It flows through the service's normal logging into
/// the Daily Environment Report and the Katastasi status hub. A service that needs to page or post directly
/// registers its own <see cref="IJobStalenessAlarm"/> to replace this.
/// </summary>
public sealed class LoggingJobStalenessAlarm : IJobStalenessAlarm
{
    private readonly ILogger<LoggingJobStalenessAlarm> _logger;
    private readonly JobMetrics? _metrics;

    /// <summary>Construct the alarm (log only).</summary>
    public LoggingJobStalenessAlarm(ILogger<LoggingJobStalenessAlarm> logger) => _logger = logger;

    /// <summary>Construct the alarm that ALSO sets <c>jobs_stale</c> = 1 (the DI default since 1.3.0).</summary>
    public LoggingJobStalenessAlarm(ILogger<LoggingJobStalenessAlarm> logger, JobMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public Task RaiseAsync(StaleJob job, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "STALE JOB: {JobName} has not succeeded within its {Threshold} threshold — last success "
            + "{LastSuccessAt}, overdue by {StaleFor}.",
            job.Job, job.Threshold, job.LastSuccessAt, job.StaleFor);
        _metrics?.RecordStale(job.Job, stale: true);
        return Task.CompletedTask;
    }
}
