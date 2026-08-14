using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Model;
using Dloizides.Jobs.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dloizides.Jobs.Hosting;

/// <summary>
/// Executes checkpointable jobs. Each poll: claim the oldest runnable job under a single-flight lease,
/// heartbeat that lease automatically while the body runs, hand the body an <see cref="IJobContext"/> to
/// checkpoint/report, then finalise via a COMPARE-AND-SET where "0 rows" is a benign no-op — the rule that
/// ends the reclaim-vs-complete error loop. A run whose owner dies is reclaimed and RESUMED from its
/// checkpoint by a later poll (on this or another replica).
/// </summary>
public sealed class JobRunnerHostedService : BackgroundService, IJobRunner
{
    private readonly IServiceScopeFactory _scopes;
    private readonly JobsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<JobRunnerHostedService> _logger;
    private readonly string _owner;

    /// <summary>Construct the runner. One lease owner id is minted per instance (see <see cref="JobOwnerId"/>).</summary>
    public JobRunnerHostedService(
        IServiceScopeFactory scopes,
        IOptions<JobsOptions> options,
        TimeProvider time,
        ILogger<JobRunnerHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _owner = JobOwnerId.New();
    }

    /// <summary>This runner's lease owner id — the compare-and-set key on every claim, heartbeat and complete.</summary>
    public string Owner => _owner;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A poll must never die on an unexpected fault — log and try again next interval. Per-run
                // failures are captured on the row; this guards the LOOP itself.
                _logger.LogError(ex, "Job runner poll failed; retrying on the next interval.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        var now = _time.GetUtcNow();
        var run = await store
            .ClaimNextAsync(_owner, now, now + _options.LeaseTtl, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return false;
        }

        var job = JobResolver.Find(scope.ServiceProvider, run.JobName);
        if (job is null)
        {
            // The run names a job this build no longer registers (a rollback, say). Fail it rather than
            // leave it holding the single-flight slot forever.
            await store.CompleteAsync(
                run.Id, _owner, JobRunOutcomes.Failed,
                $"No job is registered under the name '{run.JobName}'.", _time.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        await ExecuteClaimedAsync(store, job, run, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ExecuteClaimedAsync(
        IJobStore store, ICheckpointableJob job, JobRun run, CancellationToken cancellationToken)
    {
        if (run.ClaimedBy is not null && !string.Equals(run.ClaimedBy, _owner, StringComparison.Ordinal))
        {
            // Defensive: the store handed back a run this owner does not hold. Never work it.
            return;
        }

        var context = new JobContext(_scopes, run.Id, _owner, run.Argument, run.Checkpoint, _time);
        var resumed = run.Checkpoint is not null;
        if (resumed)
        {
            _logger.LogInformation(
                "Resuming job {JobName} run {RunId} from its last checkpoint.", run.JobName, run.Id);
        }

        string outcome;
        string? error;

        // The heartbeat is stopped (its scope disposes) BEFORE we finalise. It writes on its own contexts, so
        // a tick landing at the same instant as the completion would be the very reclaim-vs-complete race this
        // design removes — closing it first keeps the two writers off the row simultaneously.
        var heartbeat = StartHeartbeat(run.Id, cancellationToken);
        try
        {
            await job.RunAsync(context, cancellationToken).ConfigureAwait(false);
            outcome = JobRunOutcomes.Completed;
            error = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown mid-run (SIGTERM cancelled us). Do NOT finalise: leave the run 'running' so
            // its lease lapses and another poll RECLAIMS and RESUMES it from the last checkpoint. Marking it
            // failed here would discard recoverable progress — exactly the "restart from scratch" regression.
            await heartbeat.DisposeAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "Job {JobName} run {RunId} interrupted by shutdown; leaving it for reclaim-and-resume.",
                run.JobName, run.Id);
            return;
        }
        catch (Exception ex)
        {
            outcome = JobRunOutcomes.Failed;
            error = ex.Message;
            _logger.LogWarning(ex, "Job {JobName} run {RunId} failed.", run.JobName, run.Id);
        }
        finally
        {
            await heartbeat.DisposeAsync().ConfigureAwait(false);
        }

        var recorded = await store
            .CompleteAsync(run.Id, _owner, outcome, error, _time.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (!recorded)
        {
            // Reclaimed out from under us before we could record the outcome. The reclaimer's state stands;
            // drop this run cleanly and let the poll continue. Never a throw, never a retry loop.
            _logger.LogInformation(
                "Job {JobName} run {RunId} was reclaimed before this runner could record '{Outcome}'; "
                + "dropping it cleanly.", run.JobName, run.Id, outcome);
        }
    }

    /// <summary>
    /// Push the lease forward on its own timer while the body runs, each tick on a FRESH scope (the run's
    /// own unit-of-work is busy). A long job outlives the TTL many times over, so without this its own run
    /// would look abandoned and be reclaimed mid-flight.
    /// </summary>
    private IAsyncDisposable StartHeartbeat(Guid runId, CancellationToken cancellationToken)
    {
        var timer = _time.CreateTimer(
            async _ =>
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
                    var expiry = _time.GetUtcNow() + _options.LeaseTtl;
                    var held = await store
                        .HeartbeatAsync(runId, _owner, expiry, cancellationToken)
                        .ConfigureAwait(false);
                    if (!held)
                    {
                        _logger.LogDebug("Heartbeat for run {RunId} found the lease lost; another owner holds it.", runId);
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // A missed heartbeat is survivable (the TTL has slack); a throwing timer callback is not.
                    _logger.LogDebug(ex, "Heartbeat for run {RunId} failed; retrying next tick.", runId);
                }
            },
            state: null,
            dueTime: _options.HeartbeatInterval,
            period: _options.HeartbeatInterval);

        return timer;
    }
}
