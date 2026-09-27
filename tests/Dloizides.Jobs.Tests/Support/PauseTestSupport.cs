using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Extensions;
using Dloizides.Jobs.Hosting;
using Dloizides.Jobs.Metrics;
using Dloizides.Jobs.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dloizides.Jobs.Tests.Support;

/// <summary>Counts how many times each probe job body ran (registered as a singleton).</summary>
public sealed class RunLog
{
    private readonly ConcurrentDictionary<string, int> _runs = new(StringComparer.Ordinal);

    public void Record(string job) => _runs.AddOrUpdate(job, 1, (_, n) => n + 1);

    public int Count(string job) => _runs.TryGetValue(job, out var n) ? n : 0;
}

public sealed class NightlyJob(RunLog log) : ICheckpointableJob
{
    public const string JobName = "nightly";

    public string Name => JobName;

    public JobCadence Cadence => JobCadence.None;

    public Task RunAsync(IJobContext context, CancellationToken cancellationToken)
    {
        log.Record(JobName);
        return Task.CompletedTask;
    }
}

public sealed class BrokenJob(RunLog log) : ICheckpointableJob
{
    public const string JobName = "broken";

    public string Name => JobName;

    public JobCadence Cadence => JobCadence.None;

    public Task RunAsync(IJobContext context, CancellationToken cancellationToken)
    {
        log.Record(JobName);
        throw new InvalidOperationException("boom");
    }
}

/// <summary>
/// Core runtime over the in-memory store, with the <c>Jobs</c> section bound from a caller-supplied
/// configuration root so a test can change <c>Jobs:Paused</c> and reload it the way a ConfigMap update does.
/// </summary>
public sealed class PauseHarness : IDisposable
{
    private PauseHarness(ServiceProvider provider, InMemoryJobStore store, RunLog log, string service)
    {
        Provider = provider;
        Store = store;
        Log = log;
        Service = service;
    }

    public ServiceProvider Provider { get; }

    public InMemoryJobStore Store { get; }

    public RunLog Log { get; }

    public string Service { get; }

    public JobRunnerHostedService Runner => Provider.GetRequiredService<JobRunnerHostedService>();

    public static PauseHarness Create(IConfigurationRoot configuration)
    {
        var store = new InMemoryJobStore();
        var log = new RunLog();
        var service = $"svc-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(log);
        services.AddDloizidesJobs(
            jobs =>
            {
                jobs.AddJob<NightlyJob>();
                jobs.AddJob<BrokenJob>();
                jobs.Services.AddSingleton<IJobStore>(store);
                jobs.Configure(o => o.ServiceName = service);
            },
            configuration.GetSection("Jobs"));
        return new PauseHarness(services.BuildServiceProvider(), store, log, service);
    }

    public static IConfigurationRoot MemoryConfig(params string[] paused)
    {
        var values = paused.Select((name, i) => new KeyValuePair<string, string?>($"Jobs:Paused:{i}", name));
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public async Task<JobTriggerResult> TriggerAsync(string job, string source)
    {
        using var scope = Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IJobTrigger>()
            .TriggerAsync(job, source, "tester", null, null, default).ConfigureAwait(false);
    }

    public void Dispose() => Provider.Dispose();
}

/// <summary>An <see cref="Microsoft.Extensions.Logging.ILogger"/> that keeps warning-or-worse messages.</summary>
public sealed class WarningCapture : Microsoft.Extensions.Logging.ILogger
{
    private readonly ConcurrentQueue<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings.ToList();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
        {
            _warnings.Enqueue(formatter(state, exception));
        }
    }
}

/// <summary>Listens to the jobs meter like an exporter, keeping only one service's measurements.</summary>
public sealed class PauseMeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly string _service;
    private readonly ConcurrentQueue<(string Instrument, string Job, double Value)> _measurements = new();

    public PauseMeterCapture(string service)
    {
        _service = service;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == JobMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _listener.Start();
    }

    /// <summary>The value one gauge reports for one job RIGHT NOW (a fresh observation), or null.</summary>
    public double? Latest(string instrument, string job)
    {
        _measurements.Clear();
        _listener.RecordObservableInstruments();
        var all = _measurements.Where(m => m.Instrument == instrument && m.Job == job).ToList();
        return all.Count == 0 ? null : all[^1].Value;
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? job = null;
        string? service = null;
        foreach (var tag in tags)
        {
            if (tag.Key == JobMetricTags.Job)
            {
                job = tag.Value as string;
            }
            else if (tag.Key == JobMetricTags.Service)
            {
                service = tag.Value as string;
            }
        }

        if (job is not null && service == _service)
        {
            _measurements.Enqueue((instrument.Name, job, value));
        }
    }
}
