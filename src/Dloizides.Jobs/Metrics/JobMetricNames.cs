namespace Dloizides.Jobs.Metrics;

/// <summary>The instrument names on the <see cref="JobMetrics.MeterName"/> meter — already in Prometheus
/// form, so the exported series carry exactly these names (alert rules and dashboards key on them).</summary>
public static class JobMetricNames
{
    /// <summary>Gauge: unix seconds of the last successful completion.</summary>
    public const string LastSuccessTimestampSeconds = "jobs_last_success_timestamp_seconds";

    /// <summary>Histogram: duration of finished runs, seconds.</summary>
    public const string RunDurationSeconds = "jobs_run_duration_seconds";

    /// <summary>Gauge: 1 while running in this process, else 0.</summary>
    public const string Running = "jobs_running";

    /// <summary>Gauge: 1 while a watched job is stale, else 0.</summary>
    public const string Stale = "jobs_stale";

    /// <summary>Gauge: progress done/total, 0..1.</summary>
    public const string ProgressRatio = "jobs_progress_ratio";

    /// <summary>Counter: failed runs.</summary>
    public const string FailuresTotal = "jobs_failures_total";
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
