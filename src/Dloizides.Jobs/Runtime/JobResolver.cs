using Dloizides.Jobs.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dloizides.Jobs.Runtime;

/// <summary>
/// Resolves the registered <see cref="ICheckpointableJob"/>s from a scope. Jobs are registered scoped (a
/// job may depend on scoped services), so the runner, the watchdog and the status query each enumerate
/// them from their own scope rather than holding singletons.
/// </summary>
public static class JobResolver
{
    /// <summary>Every registered job, in registration order.</summary>
    public static IReadOnlyList<ICheckpointableJob> All(IServiceProvider provider) =>
        provider.GetServices<ICheckpointableJob>().ToList();

    /// <summary>The registered job with this name, or null. Ordinal match on <see cref="ICheckpointableJob.Name"/>.</summary>
    public static ICheckpointableJob? Find(IServiceProvider provider, string name) =>
        provider.GetServices<ICheckpointableJob>()
            .FirstOrDefault(j => string.Equals(j.Name, name, StringComparison.Ordinal));

    /// <summary>Whether a job with this name is registered.</summary>
    public static bool Contains(IServiceProvider provider, string name) =>
        Find(provider, name) is not null;
}
