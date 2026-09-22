using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dloizides.Jobs.Runtime;

/// <summary>
/// The serialization contract for the jsonb <see cref="Model.JobRun.Checkpoint"/> and
/// <see cref="Model.JobRun.Progress"/> columns. Centralised so the writer (the job context) and the reader
/// (the status query) never disagree on shape or casing.
/// </summary>
public static class JobJson
{
    /// <summary>camelCase, no indentation, nulls omitted — a compact jsonb payload.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serialize a value with the shared options.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserialize a value with the shared options, or default on null/blank/invalid.</summary>
    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>
/// The stored envelope around a checkpoint: WHEN it was written plus the job's opaque state. Wrapping lets
/// the status query surface <c>checkpoint.at</c> without a separate column, while <c>LoadCheckpoint&lt;T&gt;</c>
/// unwraps and returns just the state.
/// </summary>
/// <param name="At">When the checkpoint was saved.</param>
/// <param name="State">The job's opaque resume state.</param>
public sealed record CheckpointEnvelope(DateTimeOffset At, JsonElement State);

/// <summary>The persisted progress snapshot — the exact fields the UI reads (plus the derived pct on read).</summary>
/// <param name="Phase">Free-form phase label.</param>
/// <param name="Done">Items processed so far.</param>
/// <param name="Total">Total items in this phase, or 0 when unknown.</param>
/// <param name="UpdatedAt">When this snapshot was reported.</param>
public sealed record ProgressSnapshot(string Phase, long Done, long Total, DateTimeOffset UpdatedAt)
{
    /// <summary>Every phase this run has entered, oldest first, with its start and (once left) end — the
    /// per-phase timing. Null on snapshots written before 1.3.0.</summary>
    public IReadOnlyList<JobPhaseSpan>? Phases { get; init; }
}

/// <summary>One phase of a run as persisted in the progress snapshot.</summary>
/// <param name="Phase">The phase label as reported.</param>
/// <param name="StartedAt">When the run first reported this phase.</param>
/// <param name="EndedAt">When the run moved to the next phase, or null while it is the current one.</param>
public sealed record JobPhaseSpan(string Phase, DateTimeOffset StartedAt, DateTimeOffset? EndedAt = null);
