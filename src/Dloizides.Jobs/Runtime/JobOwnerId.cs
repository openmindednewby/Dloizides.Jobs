namespace Dloizides.Jobs.Runtime;

/// <summary>
/// Mints a per-process lease owner id: the host name (the pod name in k8s) plus a run-unique token, so a
/// restart of the same host reads as a DIFFERENT owner and cannot accidentally "keep" a lease its previous
/// process held. Legible on purpose — a reclaim log line names who lost the row.
/// </summary>
public static class JobOwnerId
{
    /// <summary>A fresh owner id for this runner instance.</summary>
    public static string New() => $"{Environment.MachineName}:{Guid.NewGuid():N}";
}
