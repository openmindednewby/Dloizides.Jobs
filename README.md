# Dloizides.Jobs

The fleet-wide standard for **checkpointed, resumable, conflict-safe, watchdog-alarmed, UI-visible
background jobs** — the storage-agnostic core. One shared vehicle so no service re-invents its job runner.

> Every long-running background job is **single-flight, lease-recovered, checkpointed (resumes where it
> left off), conflict-safe, watchdog-alarmed, and visible in the UI**.

Pair this with a storage package for persistence — **[`Dloizides.Jobs.EntityFrameworkCore`](https://github.com/openmindednewby/Dloizides.Jobs.EntityFrameworkCore)**
provides the EF Core + relational store (jsonb checkpoint/progress, the single-flight index, and the
compare-and-set transitions).

## The failure this prevents

A long ingest was reclaimed on a deploy, **restarted from scratch**, and the runner then **error-looped**
on a reclaim-vs-complete race for hours — healthy pods, zero progress, days-stale with no alarm. Two gaps:
**progress was not durable** (a reclaim meant "start over", not "resume") and **transitions were brittle**
(a losing optimistic-concurrency write threw and killed the whole poll). This package fixes the class.

## What you implement

```csharp
public sealed class IngestJob : ICheckpointableJob
{
    public string Name => "ingest";

    // Watched hourly; the watchdog alarms if it hasn't succeeded in 2h.
    public JobCadence Cadence => JobCadence.Every(TimeSpan.FromHours(1), TimeSpan.FromHours(2));

    public async Task RunAsync(IJobContext ctx, CancellationToken ct)
    {
        // Resume from the last checkpoint, or start fresh.
        var state = ctx.LoadCheckpoint<IngestState>() ?? new IngestState(Cursor: 0);

        for (var cursor = state.Cursor; cursor < Total; cursor++)
        {
            await ProcessAsync(cursor, ct);              // idempotent per checkpoint (upsert, don't blind-insert)
            if (cursor % 100 == 0)
            {
                await ctx.ReportProgressAsync("import", cursor, Total, ct);
                await ctx.SaveCheckpointAsync(new IngestState(cursor), ct);
            }
        }
    }
}

public sealed record IngestState(int Cursor);
```

## Wiring (Program.cs)

```csharp
builder.AddDloizidesJobs(jobs =>
{
    jobs.AddJob<IngestJob>();
    jobs.UseEntityFrameworkStore<AppDbContext>();   // from Dloizides.Jobs.EntityFrameworkCore
    jobs.Configure(o => o.PollInterval = TimeSpan.FromSeconds(5));
});
```

Trigger a run, and read status for the console:

```csharp
await trigger.TriggerAsync("ingest", JobTriggerSources.Manual, user.Sub, tenantId, argument: null, ct);
var status = await statusQuery.GetAsync("ingest", ct);   // §8 UI shape: phase, %, checkpoint, stale, timeline
```

## Public API surface

| Type | Role |
|------|------|
| `ICheckpointableJob` | The job you implement — `Name`, `Cadence`, `RunAsync(IJobContext, ct)`. |
| `IJobContext` | Handed to the body: `LoadCheckpoint<T>()`, `SaveCheckpointAsync<T>()`, `ReportProgressAsync(phase, done, total, ct)`. Heartbeat is automatic. |
| `IJobStore` | The persistence seam (every mutation is compare-and-set; 0 rows = benign no-op). Supplied by a storage package. |
| `IJobRunner` | Hosted service: poll → single-flight claim → lease + auto-heartbeat → run → checkpoint → CAS complete → reclaim-and-resume. `RunOnceAsync` is the deterministic seam. |
| `IJobStalenessMonitor` / `IJobStalenessAlarm` | The watchdog and where its alarm goes (default logs a warning). |
| `IJobStatusQuery` | The §8 UI JSON for the running-jobs console (the poll path). |
| `IJobStatusBackplane` | Opt-in real-time PUSH fan-out (`PublishAsync` + `Subscribe`). `None` (default) / `InMemory` in core; `Postgres` in the EF package. Selected by `Jobs:Status:Backplane`. |
| `IJobTrigger` | On-demand trigger with provenance + single-flight. |
| `JobRun`, `JobRunOutcomes`, `JobTriggerSources`, `JobCadence` | The model + vocabulary. |
| `AddDloizidesJobs(...)` | The one adoption entrypoint. |

## Real-time status (opt-in)

Status is durable and pollable by default. To PUSH changes live, select a **backplane** and (for the browser
wire) add [`Dloizides.Jobs.AspNetCore`](https://github.com/openmindednewby/Dloizides.Jobs.AspNetCore):

```csharp
builder.AddDloizidesJobs(jobs =>
{
    jobs.AddJob<IngestJob>();
    jobs.UseEntityFrameworkStore<AppDbContext>();
    jobs.UsePostgresStatusBackplane<AppDbContext>();  // or jobs.UseInMemoryStatusBackplane()
});
```

```jsonc
"Jobs": { "Status": { "Backplane": "Postgres", "Wire": "Sse" } }
```

**Persist first, push second.** The runtime writes to the store exactly as before and publishes a lightweight
event *after* the write commits (at enqueue / claim / progress / checkpoint / complete), only when the
compare-and-set actually landed. Push is never load-bearing — a dropped notification heals on the next poll,
so a push client and a poll client always read the same `JobStatus`. Adding a new transport is a
`AddStatusBackplane(key, factory)` call plus one config value; the core resolver never changes.

## Guarantees

- **Single-flight** — one queued-or-running run per job name across every replica (a DB constraint, via the store).
- **Lease + heartbeat** — a dead owner's run becomes reclaimable; a live owner keeps its claim across missed beats.
- **Resume** — a reclaimed run continues from its last checkpoint, not from scratch.
- **Conflict-safe** — every transition is compare-and-set; "0 rows affected" is a benign no-op, never a poll-killing throw.
- **Watched** — a job overdue past its cadence raises an alarm.
- **Author responsibility** — jobs must be idempotent per checkpoint (re-running the tail after the last checkpoint is safe).

Standard: `BaseClient/docs/code-standards/background-jobs.md`.

## License

MIT
