using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dloizides.Jobs.Extensions;

/// <summary>
/// The fluent surface passed to the <c>AddDloizidesJobs</c> callback: register jobs, tune options, and
/// select a store (the store selector — e.g. <c>UseEntityFrameworkStore</c> — is an extension shipped by a
/// storage package such as <c>Dloizides.Jobs.EntityFrameworkCore</c>).
/// </summary>
public sealed class JobsBuilder
{
    /// <summary>Construct the builder over a service collection.</summary>
    public JobsBuilder(IServiceCollection services) => Services = services;

    /// <summary>The underlying service collection — the seam storage packages extend to register an
    /// <see cref="IJobStore"/>.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The options override captured by <see cref="Configure"/>, applied after config binding.</summary>
    internal Action<JobsOptions>? OptionsConfigurator { get; private set; }

    /// <summary>
    /// Register a checkpointable job. Jobs are scoped (a job may depend on scoped services), and duplicate
    /// registrations of the same implementation are collapsed.
    /// </summary>
    public JobsBuilder AddJob<TJob>()
        where TJob : class, ICheckpointableJob
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<ICheckpointableJob, TJob>());
        return this;
    }

    /// <summary>Override the runner/watchdog options in code (applied after any config binding).</summary>
    public JobsBuilder Configure(Action<JobsOptions> configure)
    {
        OptionsConfigurator = configure;
        return this;
    }
}
