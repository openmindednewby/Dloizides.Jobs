using System.Text.Json;
using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dloizides.Jobs.Runtime;

/// <summary>
/// The <see cref="IJobContext"/> handed to a running job. Checkpoint and progress writes go through a FRESH
/// DI scope each time — the job's own unit-of-work (its DbContext, its transaction) is busy for the
/// duration, so the context never shares a store instance with the body. Each write is a compare-and-set
/// keyed on this run's id and owner: if the run was reclaimed, the write is a benign no-op (returns false),
/// never a throw.
/// </summary>
/// <remarks>
/// PERSIST FIRST, PUSH SECOND. Each write persists to the store and, ONLY when the compare-and-set actually
/// landed (the run is still owned), notifies the <see cref="IJobStatusBackplane"/> so streaming clients see
/// the change. A reclaimed run neither advances its in-memory state NOR pushes — a stale push is never
/// emitted. The publish is fail-soft: it can never fault the job body.
/// </remarks>
public sealed class JobContext : IJobContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly string _jobName;
    private readonly string _owner;
    private readonly TimeProvider _time;
    private readonly IJobStatusBackplane _backplane;
    private readonly ILogger? _logger;
    private string? _checkpoint;

    /// <summary>
    /// Construct the context for one run.
    /// </summary>
    /// <param name="scopes">Scope factory for the fresh-scope checkpoint/progress writes.</param>
    /// <param name="jobName">The job's name — the key carried on published status events.</param>
    /// <param name="runId">The run being executed.</param>
    /// <param name="owner">This runner's lease owner id — the compare-and-set key.</param>
    /// <param name="argument">The run's opaque trigger input.</param>
    /// <param name="initialCheckpoint">The checkpoint the run carried at claim time (non-null on a resume).</param>
    /// <param name="time">The clock used to stamp checkpoints.</param>
    /// <param name="backplane">The status backplane notified AFTER each persisted change (fail-soft).</param>
    /// <param name="logger">Optional logger for a swallowed publish fault.</param>
    public JobContext(
        IServiceScopeFactory scopes,
        string jobName,
        Guid runId,
        string owner,
        string? argument,
        string? initialCheckpoint,
        TimeProvider time,
        IJobStatusBackplane backplane,
        ILogger? logger)
    {
        _scopes = scopes;
        _jobName = jobName;
        RunId = runId;
        _owner = owner;
        Argument = argument;
        _checkpoint = initialCheckpoint;
        _time = time;
        _backplane = backplane;
        _logger = logger;
    }

    /// <inheritdoc />
    public Guid RunId { get; }

    /// <inheritdoc />
    public string? Argument { get; }

    /// <inheritdoc />
    public T? LoadCheckpoint<T>()
    {
        var envelope = JobJson.Deserialize<CheckpointEnvelope>(_checkpoint);
        if (envelope is null)
        {
            return default;
        }

        // The state was captured as a JsonElement in the envelope; re-materialise it as T.
        return envelope.State.Deserialize<T>(JobJson.Options);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync<T>(T state, CancellationToken cancellationToken)
    {
        var stateElement = JsonSerializer.SerializeToElement(state, JobJson.Options);
        var envelope = new CheckpointEnvelope(_time.GetUtcNow(), stateElement);
        var json = JobJson.Serialize(envelope);

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var owned = await store.SaveCheckpointAsync(RunId, _owner, json, cancellationToken).ConfigureAwait(false);

        // Keep the local copy in step ONLY when the write actually landed — a reclaimed run must not have
        // its resume state advanced in memory past what storage records.
        if (owned)
        {
            _checkpoint = json;

            // Persist-first-push-second: the store write above committed; only now notify, and only because
            // we still own the run. A reclaimed run (owned == false) pushes nothing.
            await PublishAsync(JobStatusEventKinds.Checkpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ReportProgressAsync(string phase, long done, long total, CancellationToken cancellationToken)
    {
        var snapshot = new ProgressSnapshot(phase, done, total, _time.GetUtcNow());
        var json = JobJson.Serialize(snapshot);

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        var owned = await store.ReportProgressAsync(RunId, _owner, json, cancellationToken).ConfigureAwait(false);

        // Persist-first-push-second: notify only after the progress row committed, and only while still owned.
        if (owned)
        {
            await PublishAsync(JobStatusEventKinds.Progress, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PublishAsync(string kind, CancellationToken cancellationToken)
    {
        var evt = new JobStatusEvent(_jobName, kind, RunId, _time.GetUtcNow());
        return JobStatusPublisher.PublishSafeAsync(_backplane, evt, _logger, cancellationToken);
    }
}
