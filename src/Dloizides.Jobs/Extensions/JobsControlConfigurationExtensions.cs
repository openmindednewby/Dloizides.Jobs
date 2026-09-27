using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

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
    /// <remarks>
    /// FAILS OPEN: an unreadable or invalid file — at boot or on a later reload — is logged as a warning and
    /// treated as an empty source, so nothing is paused and the host still starts. A bad ConfigMap edit must
    /// never take a service down; the cost is that a pause does not apply until the file is valid again.
    /// Configuration is built before DI, so the warning goes to <paramref name="logger"/> when given, else to
    /// standard error (which the pod log captures).
    /// </remarks>
    public static IConfigurationBuilder AddDloizidesJobsControl(
        this IConfigurationBuilder builder, string directory = DefaultDirectory, ILogger? logger = null)
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
        return builder.AddJsonFile(source =>
        {
            source.FileProvider = files;
            source.Path = FileName;
            source.Optional = true;
            source.ReloadOnChange = true;
            source.OnLoadException = context =>
            {
                context.Ignore = true;
                ReportLoadFailure(logger, directory, context.Exception);
            };
        });
    }

    private static void ReportLoadFailure(ILogger? logger, string directory, Exception exception)
    {
        const string message =
            "jobs-control file {File} could not be loaded; treating it as empty, so NO job is paused until it is "
            + "valid again.";
        var file = Path.Combine(directory, FileName);
        if (logger is not null)
        {
            logger.LogWarning(exception, message, file);
            return;
        }

        Console.Error.WriteLine(
            $"warn: Dloizides.Jobs: jobs-control file {file} could not be loaded ({exception.Message}); "
            + "treating it as empty, so NO job is paused until it is valid again.");
    }
}
