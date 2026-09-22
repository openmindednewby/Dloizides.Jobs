namespace Dloizides.Jobs.Metrics;

/// <summary>
/// The instrument names on the <see cref="JobMetrics.MeterName"/> meter. prometheus-net's meter adapter
/// exports each as <c>{meter name}_{instrument name}</c> (meter dots become underscores, lower-cased; no unit
/// suffix, no <c>_total</c> added), so with the meter named <c>jobs</c> the scrape carries exactly
/// <c>jobs_last_success_timestamp_seconds</c>, <c>jobs_run_duration_seconds</c>, <c>jobs_running</c>,
/// <c>jobs_stale</c>, <c>jobs_progress_ratio</c> and <c>jobs_failures_total</c> — the names the alert rules,
/// dashboards, notification-api's JobHealthCollector and the katastasi feeder query. Do not add a
/// <c>jobs_</c> prefix here: it would export as <c>jobs_jobs_*</c> (JOBS-VIS-1 "Make job statuses visible").
/// </summary>
public static class JobMetricNames
{
    /// <summary>Gauge: unix seconds of the last successful completion.</summary>
    public const string LastSuccessTimestampSeconds = "last_success_timestamp_seconds";

    /// <summary>Histogram: duration of finished runs, seconds.</summary>
    public const string RunDurationSeconds = "run_duration_seconds";

    /// <summary>Gauge: 1 while running in this process, else 0.</summary>
    public const string Running = "running";

    /// <summary>Gauge: 1 while a watched job is stale, else 0.</summary>
    public const string Stale = "stale";

    /// <summary>Gauge: progress done/total, 0..1.</summary>
    public const string ProgressRatio = "progress_ratio";

    /// <summary>Counter: failed runs.</summary>
    public const string FailuresTotal = "failures_total";
}

/// <summary>The tag keys on every jobs measurement.</summary>
public static class JobMetricTags
{
    /// <summary>The job name. NOTE: Prometheus renames a scraped <c>job</c> label to <c>exported_job</c>
    /// (it collides with the scrape-target label) unless the scrape config sets <c>honor_labels</c>.</summary>
    public const string Job = "job";

    /// <summary>The emitting service.</summary>
    public const string Service = "service";
}
