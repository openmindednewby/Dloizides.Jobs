namespace Dloizides.Jobs.Configuration;

/// <summary>
/// The well-known values for <c>Jobs:Status:Backplane</c>. The field is a plain string, not an enum, so a
/// future transport is a config value the core never had to know about — these are the ones shipped in the
/// box. Selection is case-insensitive.
/// </summary>
public static class JobStatusBackplanes
{
    /// <summary>No push; poll-only. The default — always works, zero dependencies.</summary>
    public const string None = "None";

    /// <summary>In-process fan-out (a single pod). Zero dependencies; NOT cross-replica.</summary>
    public const string InMemory = "InMemory";

    /// <summary>Postgres <c>LISTEN</c>/<c>NOTIFY</c> — cross-replica, no new infra. Provided by
    /// <c>Dloizides.Jobs.EntityFrameworkCore</c>. The recommended push transport when opting in.</summary>
    public const string Postgres = "Postgres";
}
