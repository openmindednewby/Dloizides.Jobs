namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// Handed to a running job body — the ONLY way the body touches its run row. It hides the lease, the
/// owner id and the compare-and-set writes: the body just loads its resume state, saves checkpoints, and
/// reports progress. The heartbeat runs automatically for the life of <c>RunAsync</c>.
/// </summary>
public interface IJobContext
{
    /// <summary>This run's id — useful for the job's own structured logging.</summary>
    Guid RunId { get; }

    /// <summary>The run's opaque trigger input (e.g. a source name), or null when it takes none.</summary>
    string? Argument { get; }

    /// <summary>
    /// Rehydrate the resume state saved by the last <see cref="SaveCheckpointAsync"/>, or null on a fresh
    /// run (nothing checkpointed yet). On a reclaimed run this returns the state as of the last checkpoint,
    /// so the body can continue from there instead of restarting.
    /// </summary>
    T? LoadCheckpoint<T>();

    /// <summary>
    /// Persist opaque resume state. Call at regular intervals — every N items or M seconds — so a reclaim
    /// loses at most one interval of work. A write that finds the run no longer owned by this worker (it was
    /// reclaimed) is a benign no-op: it neither throws nor clobbers the new owner.
    /// </summary>
    Task SaveCheckpointAsync<T>(T state, CancellationToken cancellationToken);

    /// <summary>
    /// Report structured progress for the UI and the watchdog. The percentage is derived as
    /// <c>done*100/total</c>. Like a checkpoint write, a report on a reclaimed run is a benign no-op.
    /// </summary>
    Task ReportProgressAsync(string phase, long done, long total, CancellationToken cancellationToken);
}
