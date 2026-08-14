using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Status;
using Microsoft.Extensions.Logging;

namespace Dloizides.Jobs.Runtime;

/// <summary>
/// The single place a persistence point turns into a backplane publish — always AFTER the store write, and
/// always fail-soft. Push is never load-bearing (persist first, push second), so a transport blip is logged
/// at Debug and swallowed: it must never fault the job body, the runner poll or the trigger. The next poll or
/// a reconnect-then-snapshot heals the missed event.
/// </summary>
internal static class JobStatusPublisher
{
    /// <summary>Publish <paramref name="evt"/>, catching and logging any transport fault so the caller's path
    /// is never disturbed.</summary>
    public static async Task PublishSafeAsync(
        IJobStatusBackplane backplane, JobStatusEvent evt, ILogger? logger, CancellationToken cancellationToken)
    {
        try
        {
            await backplane.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(
                ex,
                "Job status backplane publish for {Job}/{Kind} failed; push is best-effort, poll or reconnect "
                + "will heal it.", evt.Job, evt.Kind);
        }
    }
}
