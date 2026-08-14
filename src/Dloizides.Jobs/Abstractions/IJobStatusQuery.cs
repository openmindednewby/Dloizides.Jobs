using Dloizides.Jobs.Status;

namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// Reads job status for the API and UI in the stable §8 shape. One call per job, or all registered jobs at
/// once for the console's running-jobs panel.
/// </summary>
public interface IJobStatusQuery
{
    /// <summary>The live status of a single job, or null if no such job is registered.</summary>
    Task<JobStatus?> GetAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>The live status of every registered job, in registration order.</summary>
    Task<IReadOnlyList<JobStatus>> GetAllAsync(CancellationToken cancellationToken);
}
