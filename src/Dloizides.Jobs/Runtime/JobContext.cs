using System.Text.Json;
using Dloizides.Jobs.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dloizides.Jobs.Runtime;

/// <summary>
/// The <see cref="IJobContext"/> handed to a running job. Checkpoint and progress writes go through a FRESH
/// DI scope each time — the job's own unit-of-work (its DbContext, its transaction) is busy for the
/// duration, so the context never shares a store instance with the body. Each write is a compare-and-set
/// keyed on this run's id and owner: if the run was reclaimed, the write is a benign no-op (returns false),
/// never a throw.
/// </summary>
public sealed class JobContext : IJobContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly string _owner;
    private readonly TimeProvider _time;
    private string? _checkpoint;

    /// <summary>
    /// Construct the context for one run.
    /// </summary>
    /// <param name="scopes">Scope factory for the fresh-scope checkpoint/progress writes.</param>
    /// <param name="runId">The run being executed.</param>
    /// <param name="owner">This runner's lease owner id — the compare-and-set key.</param>
    /// <param name="argument">The run's opaque trigger input.</param>
    /// <param name="initialCheckpoint">The checkpoint the run carried at claim time (non-null on a resume).</param>
    /// <param name="time">The clock used to stamp checkpoints.</param>
    public JobContext(
        IServiceScopeFactory scopes,
        Guid runId,
        string owner,
        string? argument,
        string? initialCheckpoint,
        TimeProvider time)
    {
        _scopes = scopes;
        RunId = runId;
        _owner = owner;
        Argument = argument;
        _checkpoint = initialCheckpoint;
        _time = time;
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
        }
    }

    /// <inheritdoc />
    public async Task ReportProgressAsync(string phase, long done, long total, CancellationToken cancellationToken)
    {
        var snapshot = new ProgressSnapshot(phase, done, total, _time.GetUtcNow());
        var json = JobJson.Serialize(snapshot);

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        await store.ReportProgressAsync(RunId, _owner, json, cancellationToken).ConfigureAwait(false);
    }
}
