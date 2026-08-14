using Dloizides.Jobs.Abstractions;

namespace Dloizides.Jobs.Backplane;

/// <summary>
/// One candidate backplane, keyed by the string a consumer puts in <c>Jobs:Status:Backplane</c>. Each
/// transport package (and the core, for <c>None</c>/<c>InMemory</c>) contributes a registration; at resolve
/// time the runtime picks the one whose <see cref="Key"/> matches the configured value (case-insensitive,
/// last registration wins) and builds it via <see cref="Factory"/>. This is the seam that keeps the selection
/// OPEN: a new transport is a new registration plus a config line, never a change to the core's resolver.
/// </summary>
/// <param name="Key">The config value this registration answers to (e.g. <c>Postgres</c>).</param>
/// <param name="Factory">Builds the backplane from the resolved provider — invoked once, since the resolved
/// <see cref="IJobStatusBackplane"/> is a singleton.</param>
public sealed record JobStatusBackplaneRegistration(
    string Key, Func<IServiceProvider, IJobStatusBackplane> Factory);
