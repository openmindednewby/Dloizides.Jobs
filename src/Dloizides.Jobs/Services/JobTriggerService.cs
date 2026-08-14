using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Model;
using Dloizides.Jobs.Runtime;
using Dloizides.Jobs.Status;
using Microsoft.Extensions.Logging;

namespace Dloizides.Jobs.Services;

/// <summary>
/// The default <see cref="IJobTrigger"/>. Validates the job is registered, stamps provenance, and enqueues
/// the run through the store — where the single-flight partial unique index (not this code) is the actual
/// guarantee that two racing replicas produce exactly one run.
/// </summary>
public sealed class JobTriggerService : IJobTrigger
{
    private readonly IServiceProvider _provider;
    private readonly IJobStore _store;
    private readonly TimeProvider _time;
    private readonly IJobStatusBackplane _backplane;
    private readonly ILogger<JobTriggerService> _logger;

    /// <summary>Construct the trigger service.</summary>
    public JobTriggerService(
        IServiceProvider provider,
        IJobStore store,
        TimeProvider time,
        IJobStatusBackplane backplane,
        ILogger<JobTriggerService> logger)
    {
        _provider = provider;
        _store = store;
        _time = time;
        _backplane = backplane;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JobTriggerResult> TriggerAsync(
        string jobName,
        string triggerSource,
        string triggeredBy,
        Guid? tenantId,
        string? argument,
        CancellationToken cancellationToken)
    {
        if (!JobResolver.Contains(_provider, jobName))
        {
            return JobTriggerResult.UnknownJob();
        }

        var run = new JobRun
        {
            JobName = jobName,
            TriggerSource = triggerSource,
            TriggeredBy = triggeredBy,
            TriggeredAt = _time.GetUtcNow(),
            TenantId = tenantId,
            Argument = argument,
            Outcome = JobRunOutcomes.Queued,
        };

        var result = await _store.EnqueueAsync(run, cancellationToken).ConfigureAwait(false);
        if (!result.Accepted)
        {
            _logger.LogInformation(
                "Trigger for job {JobName} found the single-flight slot already held by run {RunId}; "
                + "reporting already-running.", jobName, result.Run.Id);
            return JobTriggerResult.AlreadyRunning(result.Run);
        }

        _logger.LogInformation(
            "Job {JobName} triggered ({TriggerSource}) by {TriggeredBy} as run {RunId}.",
            jobName, triggerSource, triggeredBy, result.Run.Id);

        // Persist-first-push-second: the queued row committed in EnqueueAsync; now notify so a stream shows
        // the run appear immediately instead of on the next poll.
        var evt = new JobStatusEvent(
            jobName, JobStatusEventKinds.Queued, result.Run.Id, _time.GetUtcNow());
        await JobStatusPublisher.PublishSafeAsync(_backplane, evt, _logger, cancellationToken).ConfigureAwait(false);

        return JobTriggerResult.Accepted(result.Run);
    }
}
