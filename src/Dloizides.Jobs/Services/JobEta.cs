namespace Dloizides.Jobs.Services;

/// <summary>
/// The server-side ETA, from the run's OWN observed rate: remaining = (total - done) * elapsed / done.
/// Ported from aml-v2 <c>src/screens/jobs/progressReadout.ts</c> <c>etaText</c>, which stays as the
/// client fallback. Withheld whenever it would be a guess.
/// </summary>
public static class JobEta
{
    /// <summary>Below this much observed runtime the rate is noise (aml-v2 <c>MIN_OBSERVED_MS</c> = 5 min).</summary>
    public static readonly TimeSpan MinimumObserved = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The projected completion instant, or null when there is no total (or a zero one), nothing done yet,
    /// the run is already done, the start is unknown, or less than <see cref="MinimumObserved"/> has elapsed.
    /// </summary>
    public static DateTimeOffset? Estimate(long done, long total, DateTimeOffset? startedAt, DateTimeOffset now)
    {
        var hasWorkLeft = total > 0 && done > 0 && done < total;
        if (!hasWorkLeft || startedAt is null)
        {
            return null;
        }

        var elapsed = now - startedAt.Value;
        if (elapsed < MinimumObserved)
        {
            return null;
        }

        var remainingTicks = (double)(total - done) * elapsed.Ticks / done;
        return now + TimeSpan.FromTicks((long)remainingTicks);
    }
}
