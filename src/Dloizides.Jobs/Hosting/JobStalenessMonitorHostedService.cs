using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Runtime;
using Dloizides.Jobs.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Jobs.Hosting;

/// <summary>
/// The staleness watchdog. On its interval it sweeps every WATCHED job and raises an alarm through
/// <see cref="IJobStalenessAlarm"/> when the job's last success is older than its cadence threshold — the
/// alarm that was missing on 2026-08-13, when a 2-day-stale ingest never paged.
/// </summary>
public sealed class JobStalenessMonitorHostedService : BackgroundService, IJobStalenessMonitor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly JobsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<JobStalenessMonitorHostedService> _logger;

    /// <summary>Construct the watchdog.</summary>
    public JobStalenessMonitorHostedService(
        IServiceScopeFactory scopes,
        IOptions<JobsOptions> options,
        TimeProvider time,
        ILogger<JobStalenessMonitorHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Job staleness sweep failed; retrying on the next interval.");
            }

            try
            {
                await Task.Delay(_options.StalenessCheckInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StaleJob>> CheckOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var alarm = scope.ServiceProvider.GetRequiredService<IJobStalenessAlarm>();
        var now = _time.GetUtcNow();

        var stale = new List<StaleJob>();
        foreach (var job in JobResolver.All(scope.ServiceProvider))
        {
            var cadence = job.Cadence;
            if (!cadence.IsWatched)
            {
                continue;
            }

            var lastSuccess = await store.GetLastSuccessAtAsync(job.Name, cancellationToken).ConfigureAwait(false);
            var reference = lastSuccess;
            if (reference is null)
            {
                // Never succeeded: measure against the last FINISHED run instead, so a job that has run and
                // failed repeatedly is flagged, while a freshly deployed job that has never run is not.
                var lastFinished = await store
                    .GetLastFinishedRunAsync(job.Name, cancellationToken)
                    .ConfigureAwait(false);
                reference = lastFinished?.CompletedAt;
            }

            if (reference is null)
            {
                continue;
            }

            var age = now - reference.Value;
            if (age <= cadence.StalenessThreshold)
            {
                continue;
            }

            var staleJob = new StaleJob(job.Name, lastSuccess, age - cadence.StalenessThreshold, cadence.StalenessThreshold);
            stale.Add(staleJob);
            await alarm.RaiseAsync(staleJob, cancellationToken).ConfigureAwait(false);
        }

        return stale;
    }
}
