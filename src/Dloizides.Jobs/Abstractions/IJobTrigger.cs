using Dloizides.Jobs.Model;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// Accepts an on-demand trigger for a registered job: records provenance, enforces single-flight across
/// replicas, and queues the run for the runner. The one place all of that happens, so a new job inherits
/// it rather than repeating it.
/// </summary>
public interface IJobTrigger
{
    /// <summary>
    /// Try to queue a run of <paramref name="jobName"/>. Returns an explicit result — accepted, unknown job,
    /// or already-running (with the occupying run) — never a silent no-op. <paramref name="argument"/> is
    /// the run's opaque input; it joins the single-flight key only for a job whose
    /// <see cref="ICheckpointableJob.SingleFlightScope"/> is <see cref="SingleFlightScope.PerArgument"/>.
    /// </summary>
    Task<JobTriggerResult> TriggerAsync(
        string jobName,
        string triggerSource,
        string triggeredBy,
        Guid? tenantId,
        string? argument,
        CancellationToken cancellationToken);
}

/// <summary>Why a trigger did or did not queue a run.</summary>
public enum JobTriggerStatus
{
    /// <summary>A run was queued.</summary>
    Accepted,

    /// <summary>No job is registered under that name.</summary>
    UnknownJob,

    /// <summary>A run of this job already occupies the single-flight slot.</summary>
    AlreadyRunning,
}

/// <summary>The outcome of <see cref="IJobTrigger.TriggerAsync"/>.</summary>
/// <param name="Status">Accepted / UnknownJob / AlreadyRunning.</param>
/// <param name="Run">The queued run (Accepted) or the occupying run (AlreadyRunning); null for UnknownJob.</param>
public readonly record struct JobTriggerResult(JobTriggerStatus Status, JobRun? Run)
{
    /// <summary>A run was queued.</summary>
    public static JobTriggerResult Accepted(JobRun run) => new(JobTriggerStatus.Accepted, run);

    /// <summary>No such job.</summary>
    public static JobTriggerResult UnknownJob() => new(JobTriggerStatus.UnknownJob, null);

    /// <summary>The slot was occupied by <paramref name="occupier"/>.</summary>
    public static JobTriggerResult AlreadyRunning(JobRun occupier) =>
        new(JobTriggerStatus.AlreadyRunning, occupier);
}
