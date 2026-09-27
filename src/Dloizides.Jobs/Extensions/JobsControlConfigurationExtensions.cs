using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Dloizides.Jobs.Extensions;

/// <summary>
/// Adds the mounted <c>jobs-control</c> ConfigMap as a live configuration source (JOBS-CTL-1d). The ConfigMap
/// carries one key, <see cref="FileName"/>, holding <c>{ "Jobs": { "Paused": [ "job-name" ] } }</c>, mounted as
/// a volume at <see cref="DefaultDirectory"/>.
/// </summary>
public static class JobsControlConfigurationExtensions
{
    /// <summary>Where the <c>jobs-control</c> ConfigMap volume is mounted by default.</summary>
    public const string DefaultDirectory = "/etc/jobs-control";

    /// <summary>The ConfigMap data key, and so the file name inside the mount.</summary>
    public const string FileName = "jobs-control.json";

    /// <summary>
    /// Add <paramref name="directory"/>/<see cref="FileName"/> as an optional JSON source that reloads on
    /// change. Uses a POLLING watcher on purpose: kubelet updates a ConfigMap volume by swapping a symlink,
    /// which an inotify watcher does not reliably report. When the directory is absent (local dev, a service
    /// without the mount) nothing is added and nothing is paused.
    /// </summary>
    public static IConfigurationBuilder AddDloizidesJobsControl(
        this IConfigurationBuilder builder, string directory = DefaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return builder;
        }

        // Lives for the process, like the configuration root that owns the source.
        var files = new PhysicalFileProvider(Path.GetFullPath(directory))
        {
            UsePollingFileWatcher = true,
            UseActivePolling = true,
        };
        return builder.AddJsonFile(files, FileName, optional: true, reloadOnChange: true);
    }
}
