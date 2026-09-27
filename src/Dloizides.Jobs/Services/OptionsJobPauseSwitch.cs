using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Configuration;
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
