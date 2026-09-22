using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Model;
using Microsoft.Extensions.Options;

namespace Dloizides.Jobs.Metrics;

/// <summary>
/// The jobs meter (<see cref="MeterName"/>): last success, run duration, running, stale, progress and
/// failures for every job, each tagged <c>job</c> + <c>service</c>. The runner, the job context and the
/// watchdog feed it; an exporter (OpenTelemetry, or prometheus-net's meter adapter via
/// <c>Dloizides.Jobs.AspNetCore</c>'s <c>AddDloizidesJobsPrometheusExport()</c>) puts it on <c>/metrics</c>.
/// </summary>
/// <remarks>
/// Gauges are per PROCESS: with several replicas, <c>jobs_running</c> is 1 only on the pod holding the lease
/// (sum by job across pods), and <c>jobs_last_success_timestamp_seconds</c> is seeded on every pod by the
/// watchdog sweep (take the max). A value that was never observed is not reported at all rather than as 0,
/// so "never succeeded" is not confused with "succeeded in 1970".
/// </remarks>
public sealed class JobMetrics : IDisposable
{
    /// <summary>The meter name an exporter subscribes to. It is also the exported series prefix:
    /// prometheus-net's meter adapter names a series <c>{meter}_{instrument}</c>, so this must stay <c>jobs</c>
    /// for the scrape to read <c>jobs_*</c> (it read <c>dloizides_jobs_jobs_*</c> while this was
    /// <c>Dloizides.Jobs</c>). See <see cref="JobMetricNames"/>.</summary>
    public const string MeterName = "jobs";

    private const string UnknownService = "unknown";
    private const string OtelServiceNameVariable = "OTEL_SERVICE_NAME";

    private readonly Meter _meter;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _failures;
    private readonly ConcurrentDictionary<string, JobGaugeState> _jobs = new(StringComparer.Ordinal);

    /// <summary>Create the meter and its instruments. The <c>service</c> tag comes from
    /// <see cref="JobsOptions.ServiceName"/>, then <c>OTEL_SERVICE_NAME</c>, then the entry assembly name.</summary>
    public JobMetrics(IOptions<JobsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ServiceName = ResolveServiceName(options.Value.ServiceName);
        _meter = new Meter(MeterName, typeof(JobMetrics).Assembly.GetName().Version?.ToString());

        _meter.CreateObservableGauge(
            JobMetricNames.LastSuccessTimestampSeconds, () => Observe(s => s.LastSuccessUnixSeconds),
            description: "Unix time of the job's last successful completion.");
        _meter.CreateObservableGauge(
            JobMetricNames.Running, () => Observe(s => s.Running),
            description: "1 while this process runs the job, else 0.");
        _meter.CreateObservableGauge(
            JobMetricNames.Stale, () => Observe(s => s.Stale),
            description: "1 when a watched job is overdue past its cadence threshold, else 0.");
        _meter.CreateObservableGauge(
            JobMetricNames.ProgressRatio, () => Observe(s => s.Progress),
            description: "done/total of the current (or last) run, 0..1.");
        _duration = _meter.CreateHistogram<double>(
            JobMetricNames.RunDurationSeconds, description: "Wall-clock duration of finished runs, in seconds.");
        _failures = _meter.CreateCounter<long>(
            JobMetricNames.FailuresTotal, description: "Runs that finished failed.");
    }

    /// <summary>The value of the <c>service</c> tag on every measurement.</summary>
    public string ServiceName { get; }

    /// <summary>A run of <paramref name="job"/> was claimed by this process. Resets the progress gauge to 0,
    /// so a new run does not keep showing the previous run's final ratio until its first progress report.</summary>
    public void RecordRunStarted(string job)
    {
        var state = State(job);
        state.Progress = 0d;
        state.Running = 1;
    }

    /// <summary>The job reported <paramref name="done"/> of <paramref name="total"/>; ignored without a total.</summary>
    public void RecordProgress(string job, long done, long total)
    {
        if (total > 0)
        {
            State(job).Progress = Math.Clamp((double)done / total, 0d, 1d);
        }
    }

    /// <summary>
    /// A run reached a terminal outcome. Records the duration when <paramref name="startedAt"/> is known,
    /// the last success + full progress on <c>completed</c>, and one failure on <c>failed</c>.
    /// </summary>
    public void RecordRunFinished(string job, string outcome, DateTimeOffset? startedAt, DateTimeOffset completedAt)
    {
        var state = State(job);
        state.Running = 0;
        if (startedAt is { } started)
        {
            _duration.Record(Math.Max(0d, (completedAt - started).TotalSeconds), Tags(job));
        }

        if (outcome == JobRunOutcomes.Completed)
        {
            state.RaiseLastSuccess(completedAt.ToUnixTimeSeconds());
            state.Progress = 1d;
        }
        else if (outcome == JobRunOutcomes.Failed)
        {
            _failures.Add(1, Tags(job));
        }
    }

    /// <summary>This process stopped running the job without recording an outcome (shutdown, or reclaimed).</summary>
    public void RecordRunInterrupted(string job) => State(job).Running = 0;

    /// <summary>Seed the last success from the store (the watchdog does this each sweep, so a restarted pod
    /// reports it before its next run). Never moves the value backwards.</summary>
    public void RecordLastSuccess(string job, DateTimeOffset? lastSuccessAt)
    {
        if (lastSuccessAt is null)
        {
            return;
        }

        State(job).RaiseLastSuccess(lastSuccessAt.Value.ToUnixTimeSeconds());
    }

    /// <summary>Set <c>jobs_stale</c> for a watched job: 1 when overdue, 0 once it recovers.</summary>
    public void RecordStale(string job, bool stale) => State(job).Stale = stale ? 1 : 0;

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private static string ResolveServiceName(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(OtelServiceNameVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return Assembly.GetEntryAssembly()?.GetName().Name ?? UnknownService;
    }

    private JobGaugeState State(string job) => _jobs.GetOrAdd(job, _ => new JobGaugeState());

    private KeyValuePair<string, object?>[] Tags(string job) =>
        [new(JobMetricTags.Job, job), new(JobMetricTags.Service, ServiceName)];

    private IEnumerable<Measurement<double>> Observe(Func<JobGaugeState, double> read)
    {
        foreach (var (job, state) in _jobs)
        {
            var value = read(state);
            if (!double.IsNaN(value))
            {
                yield return new Measurement<double>(value, Tags(job));
            }
        }
    }

    /// <summary>Latest gauge values for one job; NaN = never observed, so not reported.</summary>
    private sealed class JobGaugeState
    {
        private double _lastSuccess = double.NaN;
        private double _running = double.NaN;
        private double _stale = double.NaN;
        private double _progress = double.NaN;

        public double LastSuccessUnixSeconds => Volatile.Read(ref _lastSuccess);

        public double Running
        {
            get => Volatile.Read(ref _running);
            set => Volatile.Write(ref _running, value);
        }

        public double Stale
        {
            get => Volatile.Read(ref _stale);
            set => Volatile.Write(ref _stale, value);
        }

        public double Progress
        {
            get => Volatile.Read(ref _progress);
            set => Volatile.Write(ref _progress, value);
        }

        /// <summary>Move the last success forward to <paramref name="seconds"/>, never backwards. A
        /// compare-exchange loop, so the watchdog's seed and a run's completion racing each other cannot
        /// overwrite a newer value with an older one.</summary>
        public void RaiseLastSuccess(double seconds)
        {
            var current = Volatile.Read(ref _lastSuccess);
            while (double.IsNaN(current) || seconds > current)
            {
                var observed = Interlocked.CompareExchange(ref _lastSuccess, seconds, current);
                if (observed.Equals(current))
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
