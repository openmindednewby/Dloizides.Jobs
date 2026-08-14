namespace Dloizides.Jobs.Model;

/// <summary>How a <see cref="JobRun"/> came to be. Stored as-is, so the wire value and the column agree.</summary>
public static class JobTriggerSources
{
    /// <summary>The background scheduler fired it — no human involved.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>An admin pressed the button in the console (an interactive session).</summary>
    public const string Manual = "manual";

    /// <summary>A machine caller triggered it (an API key).</summary>
    public const string Api = "api";

    /// <summary>The default <see cref="JobRun.TriggerSource"/> for a run whose origin is a background process.</summary>
    public const string System = "system";

    /// <summary>The <see cref="JobRun.TriggeredBy"/> value for a run nobody triggered by hand.</summary>
    public const string SystemActor = "system";

    /// <summary>Whether <paramref name="value"/> is a source a caller may legitimately supply.</summary>
    public static bool IsKnown(string? value) => value is Scheduled or Manual or Api or System;
}
