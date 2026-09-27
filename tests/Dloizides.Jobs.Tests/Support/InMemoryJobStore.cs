using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Model;

namespace Dloizides.Jobs.Tests.Support;

/// <summary>
/// A minimal, lock-guarded <see cref="IJobStore"/> so the core runtime can be exercised without a database.
/// Honours single-flight per job name and compare-and-set on the owner, which is all the pause tests need;
/// the real concurrency guarantees are covered against SQLite in Dloizides.Jobs.EntityFrameworkCore.Tests.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly object _gate = new();
    private readonly List<JobRun> _runs = [];

    public IReadOnlyList<JobRun> Runs
    {
        get
        {
            lock (_gate)
            {
                return _runs.ToList();
            }
        }
    }

    /// <summary>Seed a row directly, bypassing the trigger (e.g. a run queued before a pause landed).</summary>
    public void Seed(JobRun run)
    {
        lock (_gate)
        {
            _runs.Add(run);
        }
    }

    public Task<JobEnqueueResult> EnqueueAsync(JobRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var occupier = _runs.FirstOrDefault(
                r => r.JobName == run.JobName && JobRunOutcomes.OccupiesSlot(r.Outcome));
            if (occupier is not null)
            {
                return Task.FromResult(JobEnqueueResult.AlreadyRunning(occupier));
            }

            _runs.Add(run);
            return Task.FromResult(JobEnqueueResult.Queued(run));
        }
    }

    public Task<JobRun?> FindOccupyingRunAsync(string jobName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _runs.FirstOrDefault(r => r.JobName == jobName && JobRunOutcomes.OccupiesSlot(r.Outcome)));
        }
    }

    public Task<JobRun?> ClaimNextAsync(
        string owner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var run = _runs
                .Where(r => r.Outcome == JobRunOutcomes.Queued)
                .OrderBy(r => r.TriggeredAt)
                .FirstOrDefault();
            if (run is null)
            {
                return Task.FromResult<JobRun?>(null);
            }

            run.Outcome = JobRunOutcomes.Running;
            run.ClaimedBy = owner;
            run.LeaseExpiresAt = leaseExpiresAt;
            run.StartedAt ??= now;
            return Task.FromResult<JobRun?>(run);
        }
    }

    public Task<bool> HeartbeatAsync(
        Guid runId, string owner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
        Task.FromResult(Mutate(runId, owner, r => r.LeaseExpiresAt = leaseExpiresAt));

    public Task<bool> SaveCheckpointAsync(
        Guid runId, string owner, string checkpoint, CancellationToken cancellationToken) =>
        Task.FromResult(Mutate(runId, owner, r => r.Checkpoint = checkpoint));

    public Task<bool> ReportProgressAsync(
        Guid runId, string owner, string progress, CancellationToken cancellationToken) =>
        Task.FromResult(Mutate(runId, owner, r => r.Progress = progress));

    public Task<bool> CompleteAsync(
        Guid runId, string owner, string outcome, string? error, DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(Mutate(runId, owner, r =>
        {
            r.Outcome = outcome;
            r.Error = error;
            r.CompletedAt = completedAt;
            r.ClaimedBy = null;
            r.LeaseExpiresAt = null;
        }));

    public Task<JobRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs.FirstOrDefault(r => r.Id == runId));
        }
    }

    public Task<DateTimeOffset?> GetLastSuccessAtAsync(string jobName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs
                .Where(r => r.JobName == jobName && r.Outcome == JobRunOutcomes.Completed)
                .Max(r => r.CompletedAt));
        }
    }

    public Task<JobRun?> GetLastFinishedRunAsync(string jobName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs
                .Where(r => r.JobName == jobName && JobRunOutcomes.IsTerminal(r.Outcome))
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<JobRun>> GetRecentRunsAsync(
        string jobName, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<JobRun> recent = _runs
                .Where(r => r.JobName == jobName)
                .OrderByDescending(r => r.TriggeredAt)
                .Take(limit)
                .ToList();
            return Task.FromResult(recent);
        }
    }

    private bool Mutate(Guid runId, string owner, Action<JobRun> change)
    {
        lock (_gate)
        {
            var run = _runs.FirstOrDefault(r => r.Id == runId);
            if (run is null || run.Outcome != JobRunOutcomes.Running || run.ClaimedBy != owner)
            {
                return false;
            }

            change(run);
            return true;
        }
    }
}
