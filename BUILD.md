# Build & Publish — Dloizides.Jobs

## Build + test

The core has no tests of its own; it is exercised end-to-end by the test suite in
`Dloizides.Jobs.EntityFrameworkCore` (against SQLite, where the compare-and-set genuinely fires).

```bash
dotnet build Dloizides.Jobs.sln -c Release        # multi-targets net8.0;net10.0, 0 warnings
```

## Publish to nuget.org

The API key auto-loads from `SaaS/.env.local` (`NUGET_API_KEY`) — never pass `-ApiKey`, never hand-roll
`dotnet nuget push`.

```powershell
cd NuGetPackages/Dloizides.Jobs
.\publish.ps1 -NoBump          # ship the <Version> in Directory.Build.props
# or
.\publish.ps1 -Bump patch      # bump, then publish
```

Propagation is ~3-4 min; poll `https://api.nuget.org/v3-flatcontainer/dloizides.jobs/index.json` before a
consumer restores. Publish this package **before** `Dloizides.Jobs.EntityFrameworkCore`, which depends on it.
