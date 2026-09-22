using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Model;
using Dloizides.Jobs.Runtime;
using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Services;

/// <summary>
/// Builds the §8 UI status shape from the store. State, progress %, last checkpoint / resumable-from, stale
/// badge, recent error and a per-run timeline — everything the running-jobs console renders — derived from
/// the shared rows so every replica answers the same.
/// </summary>
public sealed class DefaultJobStatusQuery : IJobStatusQuery
{
    private const string StartPhase = "start";
    private const string IdleState = "idle";
    private const string PhaseStartedEvent = "phase-started";
    private const string PhaseEndedEvent = "phase-ended";

    private readonly IServiceProvider _provider;
    private readonly IJobStore _store;
    private readonly TimeProvider _time;

    /// <summary>Construct the status query.</summary>
    public DefaultJobStatusQuery(IServiceProvider provider, IJobStore store, TimeProvider time)
    {
        _provider = provider;
        _store = store;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<JobStatus?> GetAsync(string jobName, CancellationToken cancellationToken)
    {
        var job = JobResolver.Find(_provider, jobName);
        return job is null ? null : await BuildAsync(job, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobStatus>> GetAllAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<JobStatus>();
        foreach (var job in JobResolver.All(_provider))
        {
            statuses.Add(await BuildAsync(job, cancellationToken).ConfigureAwait(false));
        }

        return statuses;
    }

    private async Task<JobStatus> BuildAsync(ICheckpointableJob job, CancellationToken cancellationToken)
    {
        var name = job.Name;
        var occupying = await _store.FindOccupyingRunAsync(name, cancellationToken).ConfigureAwait(false);
        var lastFinished = await _store.GetLastFinishedRunAsync(name, cancellationToken).ConfigureAwait(false);
        var lastSuccessAt = await _store.GetLastSuccessAtAsync(name, cancellationToken).ConfigureAwait(false);

        var current = occupying ?? lastFinished;
        var now = _time.GetUtcNow();
        var snapshot = JobJson.Deserialize<ProgressSnapshot>(current?.Progress);
        var progress = BuildProgress(snapshot);
        var checkpoint = BuildCheckpoint(current?.Checkpoint, progress?.Phase);
        var state = occupying?.Outcome ?? lastFinished?.Outcome ?? IdleState;
        var stale = IsStale(job.Cadence, lastSuccessAt, lastFinished?.CompletedAt);
        var lease = BuildLease(occupying);
        var recentError = lastFinished?.Outcome == JobRunOutcomes.Failed ? lastFinished.Error : current?.Error;
        var phases = BuildPhases(snapshot?.Phases, current?.CompletedAt, now);
        var timeline = BuildTimeline(current, checkpoint, progress?.Phase, phases);
        var eta = state == JobRunOutcomes.Running && progress is not null
            ? JobEta.Estimate(progress.Done, progress.Total, EtaBaseline(snapshot, current?.StartedAt), now)
            : null;

        return new JobStatus(name, state, progress, checkpoint, lastSuccessAt, stale, lease, recentError, timeline)
        {
            Phases = phases,
            EstimatedCompletion = eta,
        };
    }

    /// <summary>The instant the reported done/total is measured from: the current phase's start when the run
    /// reports phases (done/total describe that phase), else the run's start.</summary>
    private static DateTimeOffset? EtaBaseline(ProgressSnapshot? snapshot, DateTimeOffset? runStartedAt)
    {
        var spans = snapshot?.Phases;
        return spans is { Count: > 0 } ? spans[^1].StartedAt : runStartedAt;
    }

    private static IReadOnlyList<JobPhaseTiming> BuildPhases(
        IReadOnlyList<JobPhaseSpan>? spans, DateTimeOffset? runCompletedAt, DateTimeOffset now)
    {
        if (spans is null || spans.Count == 0)
        {
            return Array.Empty<JobPhaseTiming>();
        }

        // The last phase is still open in storage; a FINISHED run closes it at its completion instant.
        return spans
            .Select(s =>
            {
                var endedAt = s.EndedAt ?? runCompletedAt;
                return new JobPhaseTiming(s.Phase, s.StartedAt, endedAt, (endedAt ?? now) - s.StartedAt);
            })
            .ToList();
    }

    private static JobProgressView? BuildProgress(ProgressSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var pct = snapshot.Total > 0
            ? (int)Math.Clamp(snapshot.Done * 100 / snapshot.Total, 0, 100)
            : 0;
        return new JobProgressView(snapshot.Phase, snapshot.Done, snapshot.Total, pct, snapshot.UpdatedAt);
    }

    private static JobCheckpointView? BuildCheckpoint(string? json, string? progressPhase)
    {
        var envelope = JobJson.Deserialize<CheckpointEnvelope>(json);
        return envelope is null
            ? null
            : new JobCheckpointView(envelope.At, progressPhase ?? StartPhase);
    }

    private static JobLeaseView? BuildLease(JobRun? occupying)
    {
        if (occupying is null
            || occupying.Outcome != JobRunOutcomes.Running
            || occupying.ClaimedBy is null
            || occupying.LeaseExpiresAt is null)
        {
            return null;
        }

        return new JobLeaseView(occupying.ClaimedBy, occupying.LeaseExpiresAt.Value);
    }

    private bool IsStale(JobCadence cadence, DateTimeOffset? lastSuccessAt, DateTimeOffset? lastFinishedAt)
    {
        if (!cadence.IsWatched)
        {
            return false;
        }

        var reference = lastSuccessAt ?? lastFinishedAt;
        return reference is not null && _time.GetUtcNow() - reference.Value > cadence.StalenessThreshold;
    }

    private static IReadOnlyList<JobTimelineEntry> BuildTimeline(
        JobRun? run, JobCheckpointView? checkpoint, string? phase, IReadOnlyList<JobPhaseTiming> phases)
    {
        if (run is null)
        {
            return Array.Empty<JobTimelineEntry>();
        }

        var entries = new List<JobTimelineEntry> { new(run.TriggeredAt, "queued") };
        if (run.StartedAt is { } startedAt)
        {
            entries.Add(new JobTimelineEntry(startedAt, "started"));
        }

        if (checkpoint is not null)
        {
            entries.Add(new JobTimelineEntry(checkpoint.At, "checkpoint", phase));
        }

        foreach (var span in phases)
        {
            entries.Add(new JobTimelineEntry(span.StartedAt, PhaseStartedEvent, span.Phase));
            if (span.EndedAt is { } endedAt)
            {
                entries.Add(new JobTimelineEntry(endedAt, PhaseEndedEvent, span.Phase));
            }
        }

        if (run.CompletedAt is { } completedAt)
        {
            entries.Add(new JobTimelineEntry(completedAt, run.Outcome));
        }

        return entries.OrderBy(e => e.At).ToList();
    }
}
