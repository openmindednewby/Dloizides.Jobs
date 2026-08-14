using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Hosting;
using Dloizides.Jobs.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dloizides.Jobs.Extensions;

/// <summary>
/// The single adoption entrypoint: <c>AddDloizidesJobs</c>. Registers the runner, the watchdog, the
/// trigger, and the status query — everything except the STORE, which a storage package contributes via a
/// <see cref="JobsBuilder"/> extension (e.g. <c>UseEntityFrameworkStore</c>).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the jobs runtime and bind <see cref="JobsOptions"/> from the <c>Jobs</c> configuration
    /// section (the <c>configure</c> callback then overrides). Use this from <c>Program.cs</c>.
    /// </summary>
    public static TBuilder AddDloizidesJobs<TBuilder>(this TBuilder builder, Action<JobsBuilder> configure)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection(JobsOptions.SectionName);
        builder.Services.AddDloizidesJobs(configure, section);
        return builder;
    }

    /// <summary>
    /// Register the jobs runtime on a bare service collection, optionally binding options from
    /// <paramref name="configurationSection"/>. The host-builder overload above is the usual path; this one
    /// serves hosts without an <see cref="IHostApplicationBuilder"/> and tests.
    /// </summary>
    public static IServiceCollection AddDloizidesJobs(
        this IServiceCollection services,
        Action<JobsBuilder> configure,
        IConfiguration? configurationSection = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JobsOptions();
        configurationSection?.Bind(options);

        var jobsBuilder = new JobsBuilder(services);
        configure(jobsBuilder);
        jobsBuilder.OptionsConfigurator?.Invoke(options);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<JobsOptions>>(Options.Create(options));

        // The default alarm; a service can register its own IJobStalenessAlarm to page or post instead.
        services.TryAddSingleton<IJobStalenessAlarm, LoggingJobStalenessAlarm>();

        services.TryAddScoped<IJobTrigger, JobTriggerService>();
        services.TryAddScoped<IJobStatusQuery, DefaultJobStatusQuery>();

        // The runner and watchdog are singletons exposed BOTH as their deterministic interface (for tests
        // and status endpoints) and as hosted services (the background loops), resolving to one instance.
        services.TryAddSingleton<JobRunnerHostedService>();
        services.AddSingleton<IJobRunner>(sp => sp.GetRequiredService<JobRunnerHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<JobRunnerHostedService>());

        services.TryAddSingleton<JobStalenessMonitorHostedService>();
        services.AddSingleton<IJobStalenessMonitor>(sp => sp.GetRequiredService<JobStalenessMonitorHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<JobStalenessMonitorHostedService>());

        return services;
    }
}
