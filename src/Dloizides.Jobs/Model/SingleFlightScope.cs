namespace Dloizides.Jobs.Model;

/// <summary>
/// What a job's single-flight slot is keyed on. A job declares it through
/// <see cref="Abstractions.ICheckpointableJob.SingleFlightScope"/>; the default is <see cref="Global"/>.
/// </summary>
public enum SingleFlightScope
{
    /// <summary>One queued-or-running run per job name, whatever the argument. The default, and the only
    /// behaviour before 1.2.0.</summary>
    Global = 0,

    /// <summary>
    /// One queued-or-running run per (job name, argument), e.g. a per-user import keyed by user id, where one
    /// user's run must not block another's. A null and an empty argument share ONE slot (both key as the empty
    /// string), so a missing argument can never bypass the lock. Requires the per-argument mapping
    /// (<c>ApplyJobRunConfiguration(isNpgsql, perArgumentSingleFlight: true)</c>) and its migration.
    /// </summary>
    PerArgument = 1,
}
