using System.Collections.Concurrent;
using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Metrics;
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
    private readonly IJobPauseSwitch? _pause;

    // Pause bookkeeping (JOBS-CTL-1d): jobs seen paused, and when each was first seen resumed. In memory on
    // purpose: after a pod restart the clock falls back to the last success, which can only over-report.
    private readonly ConcurrentDictionary<string, byte> _seenPaused = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _resumedAt = new(StringComparer.Ordinal);

    /// <summary>Construct the watchdog. With a <paramref name="pause"/> switch a paused job is never stale,
    /// and its staleness clock restarts when it is resumed.</summary>
    public JobStalenessMonitorHostedService(
        IServiceScopeFactory scopes,
        IOptions<JobsOptions> options,
        TimeProvider time,
        ILogger<JobStalenessMonitorHostedService> logger,
        IJobPauseSwitch? pause = null)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _pause = pause;
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
        var metrics = scope.ServiceProvider.GetService<JobMetrics>();
        var now = _time.GetUtcNow();

        var stale = new List<StaleJob>();
        foreach (var job in JobResolver.All(scope.ServiceProvider))
        {
            // Seed jobs_last_success_timestamp_seconds for EVERY job, so a restarted pod reports it before its
            // next run; the stale verdict below stays limited to watched jobs.
            var lastSuccess = await store.GetLastSuccessAtAsync(job.Name, cancellationToken).ConfigureAwait(false);
            metrics?.RecordLastSuccess(job.Name, lastSuccess);

            var cadence = job.Cadence;
            if (!cadence.IsWatched)
            {
                continue;
            }

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

            if (reference is null || IsPausedNow(job.Name, now))
            {
                // Never ran, or paused on purpose: a pause is not an outage, so never stale and never an alarm.
                metrics?.RecordStale(job.Name, stale: false);
                continue;
            }

            if (_resumedAt.TryGetValue(job.Name, out var resumedAt) && resumedAt > reference.Value)
            {
                // Resumed after a pause: the pause period does not count, the clock restarts at the resume.
                reference = resumedAt;
            }

            var age = now - reference.Value;
            if (age <= cadence.StalenessThreshold)
            {
                // Within threshold — including a job that just RECOVERED: this is what sets jobs_stale back to 0.
                metrics?.RecordStale(job.Name, stale: false);
                continue;
            }

            metrics?.RecordStale(job.Name, stale: true);

            var staleJob = new StaleJob(job.Name, lastSuccess, age - cadence.StalenessThreshold, cadence.StalenessThreshold);
            stale.Add(staleJob);
            await alarm.RaiseAsync(staleJob, cancellationToken).ConfigureAwait(false);
        }

        return stale;
    }

    /// <summary>Whether the job is paused now; records the first sweep that sees it resumed.</summary>
    private bool IsPausedNow(string job, DateTimeOffset now)
    {
        if (_pause is null)
        {
            return false;
        }

        if (_pause.IsPaused(job))
        {
            _seenPaused[job] = 0;
            return true;
        }

        if (_seenPaused.TryRemove(job, out _))
        {
            _resumedAt[job] = now;
        }

        return false;
    }
}
