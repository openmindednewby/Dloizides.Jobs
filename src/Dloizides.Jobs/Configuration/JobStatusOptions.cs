namespace Dloizides.Jobs.Configuration;

/// <summary>
/// The real-time status PUSH tunables — the <c>Jobs:Status</c> sub-section. Both seams default to the safe,
/// always-available choice: no backplane (poll-only) and, when the ASP.NET wire is mounted, SSE. Opt into
/// push by setting <see cref="Backplane"/>; the recommended pairing is <c>Postgres</c> + <c>Sse</c>.
/// </summary>
public sealed class JobStatusOptions
{
    /// <summary>
    /// Which cross-pod fan-out transport to use — a <see cref="JobStatusBackplanes"/> value (or a custom key
    /// a third-party registration added). Default <see cref="JobStatusBackplanes.None"/> (poll-only). An
    /// unrecognised value falls back to <c>None</c> with a warning rather than failing startup.
    /// </summary>
    public string Backplane { get; set; } = JobStatusBackplanes.None;

    /// <summary>
    /// Which browser wire the <c>Dloizides.Jobs.AspNetCore</c> <c>MapJobStatusStream</c> mounts — <c>Sse</c>
    /// (default), <c>WebSocket</c>, or <c>Both</c>. Read by the ASP.NET package; ignored by the core.
    /// </summary>
    public string Wire { get; set; } = "Sse";

    /// <summary>
    /// The Postgres <c>NOTIFY</c> channel the <c>Postgres</c> backplane publishes on and every replica
    /// <c>LISTEN</c>s to. All replicas of one deployment must agree; distinct deployments sharing a database
    /// should use distinct channels. Default <c>dloizides_jobs</c>.
    /// </summary>
    public string Channel { get; set; } = "dloizides_jobs";
}
