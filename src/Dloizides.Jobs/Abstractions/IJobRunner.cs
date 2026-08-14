namespace Dloizides.Jobs.Abstractions;

/// <summary>
/// The hosted service that drives jobs: it polls the store, claims one run under a single-flight lease,
/// heartbeats that lease automatically while the body runs, hands the body an <see cref="IJobContext"/> to
/// checkpoint and report progress, completes via compare-and-set (0-row = benign no-op), and reclaims
/// runs whose owner died — handing a reclaimed run's checkpoint back so the job RESUMES.
/// </summary>
/// <remarks>
/// Registered as an <c>IHostedService</c> by <c>AddDloizidesJobs</c>. This interface exists so a test can
/// drive a single poll deterministically via <see cref="RunOnceAsync"/> instead of waiting on the timer.
/// </remarks>
public interface IJobRunner
{
    /// <summary>
    /// Claim and execute at most one runnable job, then return. True when a run was executed (or attempted);
    /// false when nothing was claimable this tick. The deterministic seam the background loop calls on its
    /// interval and tests call directly.
    /// </summary>
    Task<bool> RunOnceAsync(CancellationToken cancellationToken);
}
