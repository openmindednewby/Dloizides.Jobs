using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Backplane;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Hosting;
using Dloizides.Jobs.Metrics;
using Dloizides.Jobs.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        // The live pause switch (JOBS-CTL-1d): Jobs:Paused through IOptionsMonitor, so a reloaded source
        // (the mounted jobs-control ConfigMap) applies with no restart.
        AddPauseSwitch(services, configurationSection);

        // The jobs meter (JOBS-VIS-1): one per container, fed by the runner, the job context and the watchdog.
        services.TryAddSingleton<JobMetrics>();

        // The default alarm; a service can register its own IJobStalenessAlarm to page or post instead.
        services.TryAddSingleton<IJobStalenessAlarm, LoggingJobStalenessAlarm>();

        AddStatusBackplane(services);

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

    /// <summary>
    /// Bind <see cref="JobPauseOptions"/> live. With a section, bind that section. Without one (the bare
    /// overload), bind the <c>Jobs</c> section of the container's <see cref="IConfiguration"/> when the host
    /// registered one — still reload-aware — and otherwise leave nothing paused rather than fail to resolve.
    /// </summary>
    private static void AddPauseSwitch(IServiceCollection services, IConfiguration? configurationSection)
    {
        var pauseOptions = services.AddOptions<JobPauseOptions>();
        if (configurationSection is not null)
        {
            pauseOptions.Bind(configurationSection);
        }
        else
        {
            pauseOptions.Configure<IServiceProvider>((o, sp) =>
                sp.GetService<IConfiguration>()?.GetSection(JobsOptions.SectionName).Bind(o));
            services.AddSingleton<IOptionsChangeTokenSource<JobPauseOptions>>(sp =>
                sp.GetService<IConfiguration>() is { } configuration
                    ? new ConfigurationChangeTokenSource<JobPauseOptions>(
                        configuration.GetSection(JobsOptions.SectionName))
                    : new StaticChangeTokenSource());
        }

        services.TryAddSingleton<IJobPauseSwitch, OptionsJobPauseSwitch>();
    }

    /// <summary>A change-token source that never fires — for a container with no <see cref="IConfiguration"/>.</summary>
    private sealed class StaticChangeTokenSource : IOptionsChangeTokenSource<JobPauseOptions>
    {
        public string? Name => Options.DefaultName;

        public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken() =>
            Microsoft.Extensions.FileProviders.NullChangeToken.Singleton;
    }

    /// <summary>
    /// Register the always-present <c>None</c> backplane candidate and the config-driven resolver. The
    /// resolved <see cref="IJobStatusBackplane"/> is a singleton that, at first use, reads
    /// <c>Jobs:Status:Backplane</c> and picks the matching <see cref="JobStatusBackplaneRegistration"/>
    /// (case-insensitive, last registration wins). An unrecognised value falls back to <c>None</c> with a
    /// warning rather than failing startup — push is opt-in and never load-bearing.
    /// </summary>
    private static void AddStatusBackplane(IServiceCollection services)
    {
        // The floor: 'None' is always a candidate, so selection never has an empty set to choose from.
        services.AddSingleton(new JobStatusBackplaneRegistration(
            JobStatusBackplanes.None, _ => NullJobStatusBackplane.Instance));

        services.TryAddSingleton<IJobStatusBackplane>(ResolveBackplane);
    }

    private static IJobStatusBackplane ResolveBackplane(IServiceProvider sp)
    {
        var configured = sp.GetRequiredService<IOptions<JobsOptions>>().Value.Status.Backplane;
        var registrations = sp.GetServices<JobStatusBackplaneRegistration>().ToList();

        // Last registration wins so a consumer can override a package-provided candidate for the same key.
        var match = registrations.LastOrDefault(
            r => string.Equals(r.Key, configured, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match.Factory(sp);
        }

        var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(ServiceCollectionExtensions).FullName!);
        logger?.LogWarning(
            "No job status backplane is registered for Jobs:Status:Backplane='{Configured}'. Known keys: {Keys}. "
            + "Falling back to '{Fallback}' (poll-only).",
            configured,
            string.Join(", ", registrations.Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase)),
            JobStatusBackplanes.None);
        return NullJobStatusBackplane.Instance;
    }
}
