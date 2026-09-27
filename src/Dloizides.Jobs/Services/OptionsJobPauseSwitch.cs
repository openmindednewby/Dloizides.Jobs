using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
using Dloizides.Jobs.Model;
using Microsoft.Extensions.Options;

namespace Dloizides.Jobs.Services;

/// <summary>
/// The default <see cref="IJobPauseSwitch"/>: reads <see cref="JobPauseOptions"/> through
/// <see cref="IOptionsMonitor{TOptions}"/> on every call, so a reloaded ConfigMap applies without a restart.
/// </summary>
public sealed class OptionsJobPauseSwitch : IJobPauseSwitch
{
    private readonly IOptionsMonitor<JobPauseOptions> _options;

    /// <summary>Construct over the live options.</summary>
    public OptionsJobPauseSwitch(IOptionsMonitor<JobPauseOptions> options) => _options = options;

    /// <inheritdoc />
    public IReadOnlyCollection<string> PausedJobs => Current();

    /// <inheritdoc />
    public bool IsPaused(string jobName) => Current().Contains(jobName);

    private HashSet<string> Current() =>
        new(_options.CurrentValue.Paused.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.Ordinal);
}

/// <summary>Which runs the pause switch holds back: only UNATTENDED ones. A human trigger always runs.</summary>
internal static class JobPauseRules
{
    /// <summary>The error recorded on a queued unattended run cancelled because its job is paused.</summary>
    internal const string CancelledReason =
        "Paused via Jobs:Paused: unattended runs are not executed while paused (a manual trigger still runs).";

    /// <summary><c>scheduled</c> and <c>system</c> runs are unattended; <c>manual</c> and <c>api</c> are not.</summary>
    internal static bool IsUnattended(string? triggerSource) =>
        triggerSource is JobTriggerSources.Scheduled or JobTriggerSources.System;

    /// <summary>Whether <paramref name="run"/> must not execute under the current pause list.</summary>
    internal static bool Suppresses(IJobPauseSwitch pause, JobRun run) =>
        IsUnattended(run.TriggerSource) && pause.IsPaused(run.JobName);
}
