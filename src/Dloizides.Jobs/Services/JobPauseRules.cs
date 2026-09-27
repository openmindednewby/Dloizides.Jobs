using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Model;

namespace Dloizides.Jobs.Services;

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
