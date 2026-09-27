using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Extensions;
using Dloizides.Jobs.Metrics;
using Dloizides.Jobs.Model;
using Dloizides.Jobs.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Dloizides.Jobs.Tests;

/// <summary>
/// JOBS-CTL-1d "Dloizides.Jobs 1.4 ConfigMap pause switch, AML first" — package ACs. Owner decision
/// (JOBS-CTL-1 Q3): paused = no scheduled runs; a manual <see cref="IJobTrigger"/> call still runs it once.
/// </summary>
public sealed class JobPauseSwitchAcceptanceTests
{
    private static readonly TimeSpan FileReloadDeadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FileReloadPoll = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void AC_1_PausedList_WhenConfigurationReloads_TakesEffectWithoutRestart()
    {
        var config = PauseHarness.MemoryConfig();
        using var harness = PauseHarness.Create(config);
        var pause = harness.Provider.GetRequiredService<IJobPauseSwitch>();
        pause.IsPaused(NightlyJob.JobName).ShouldBeFalse();

        config["Jobs:Paused:0"] = NightlyJob.JobName;
        config.Reload();

        pause.IsPaused(NightlyJob.JobName).ShouldBeTrue();
        pause.IsPaused(BrokenJob.JobName).ShouldBeFalse();

        config["Jobs:Paused:0"] = null;
        config.Reload();

        pause.IsPaused(NightlyJob.JobName).ShouldBeFalse();
    }

    [Fact]
    public async Task AC_1_JobsControlFile_WhenRewrittenInPlace_IsPickedUpLive()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"jobs-control-{Guid.NewGuid():N}"));
        var file = Path.Combine(dir.FullName, JobsControlConfigurationExtensions.FileName);
        await File.WriteAllTextAsync(file, """{ "Jobs": { "Paused": [] } }""");
        try
        {
            var config = new ConfigurationBuilder().AddDloizidesJobsControl(dir.FullName).Build();
            using var harness = PauseHarness.Create(config);
            var pause = harness.Provider.GetRequiredService<IJobPauseSwitch>();
            pause.IsPaused(NightlyJob.JobName).ShouldBeFalse();

            await File.WriteAllTextAsync(file, """{ "Jobs": { "Paused": [ "nightly" ] } }""");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

            var deadline = DateTime.UtcNow + FileReloadDeadline;
            while (!pause.IsPaused(NightlyJob.JobName) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(FileReloadPoll);
            }

            pause.IsPaused(NightlyJob.JobName).ShouldBeTrue();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AC_1_JobsControlDirectory_WhenNotMounted_LeavesNothingPaused()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"jobs-control-missing-{Guid.NewGuid():N}");

        var config = new ConfigurationBuilder().AddDloizidesJobsControl(missing).Build();
        using var harness = PauseHarness.Create(config);

        harness.Provider.GetRequiredService<IJobPauseSwitch>().IsPaused(NightlyJob.JobName).ShouldBeFalse();
    }

    [Fact]
    public async Task AC_1_JobsControlFile_WhenInvalidAtBoot_HostStartsWithNothingPausedAndWarns()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"jobs-control-{Guid.NewGuid():N}"));
        await File.WriteAllTextAsync(
            Path.Combine(dir.FullName, JobsControlConfigurationExtensions.FileName), "{ not json");
        var log = new WarningCapture();
        try
        {
            var config = new ConfigurationBuilder().AddDloizidesJobsControl(dir.FullName, log).Build();
            using var harness = PauseHarness.Create(config);

            harness.Provider.GetRequiredService<IJobPauseSwitch>().IsPaused(NightlyJob.JobName).ShouldBeFalse();
            log.Warnings.ShouldHaveSingleItem().ShouldContain(JobsControlConfigurationExtensions.FileName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AC_1_JobsControlFile_WhenRewrittenInvalid_FailsOpenAndWarns()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"jobs-control-{Guid.NewGuid():N}"));
        var file = Path.Combine(dir.FullName, JobsControlConfigurationExtensions.FileName);
        await File.WriteAllTextAsync(file, """{ "Jobs": { "Paused": [ "nightly" ] } }""");
        var log = new WarningCapture();
        try
        {
            var config = new ConfigurationBuilder().AddDloizidesJobsControl(dir.FullName, log).Build();
            using var harness = PauseHarness.Create(config);
            var pause = harness.Provider.GetRequiredService<IJobPauseSwitch>();
            pause.IsPaused(NightlyJob.JobName).ShouldBeTrue();

            await File.WriteAllTextAsync(file, "{ not json");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));

            var deadline = DateTime.UtcNow + FileReloadDeadline;
            while ((pause.IsPaused(NightlyJob.JobName) || log.Warnings.Count == 0) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(FileReloadPoll);
            }

            pause.IsPaused(NightlyJob.JobName).ShouldBeFalse();
            log.Warnings.ShouldNotBeEmpty();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AC_1_BareOverloadWithoutSection_BindsJobsPausedFromHostConfigurationLive()
    {
        var config = PauseHarness.MemoryConfig(NightlyJob.JobName);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddDloizidesJobs(jobs =>
        {
            jobs.AddJob<NightlyJob>();
            jobs.Services.AddSingleton<IJobStore>(new InMemoryJobStore());
        });
        using var provider = services.BuildServiceProvider();
        var pause = provider.GetRequiredService<IJobPauseSwitch>();
        pause.IsPaused(NightlyJob.JobName).ShouldBeTrue();

        config["Jobs:Paused:0"] = null;
        config.Reload();

        pause.IsPaused(NightlyJob.JobName).ShouldBeFalse();
    }

    [Fact]
    public void AC_1_BareOverloadWithoutAnyConfiguration_PausesNothing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDloizidesJobs(jobs => jobs.Services.AddSingleton<IJobStore>(new InMemoryJobStore()));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobPauseSwitch>().PausedJobs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(JobTriggerSources.Scheduled)]
    [InlineData(JobTriggerSources.System)]
    public async Task AC_2_UnattendedTrigger_WhenJobPaused_ReturnsPausedAndQueuesNothing(string source)
    {
        using var harness = PauseHarness.Create(PauseHarness.MemoryConfig(NightlyJob.JobName));

        var result = await harness.TriggerAsync(NightlyJob.JobName, source);

        result.Status.ShouldBe(JobTriggerStatus.Paused);
        result.Run.ShouldBeNull();
        harness.Store.Runs.ShouldBeEmpty();
        (await harness.Runner.RunOnceAsync(default)).ShouldBeFalse();
        harness.Log.Count(NightlyJob.JobName).ShouldBe(0);
    }

    [Theory]
    [InlineData(JobTriggerSources.Manual)]
    [InlineData(JobTriggerSources.Api)]
    public async Task AC_2_ManualTrigger_WhenJobPaused_RunsOnceAndReportsCompleted(string source)
    {
        using var harness = PauseHarness.Create(PauseHarness.MemoryConfig(NightlyJob.JobName));

        var result = await harness.TriggerAsync(NightlyJob.JobName, source);
        (await harness.Runner.RunOnceAsync(default)).ShouldBeTrue();
        (await harness.Runner.RunOnceAsync(default)).ShouldBeFalse();

        result.Status.ShouldBe(JobTriggerStatus.Accepted);
        harness.Log.Count(NightlyJob.JobName).ShouldBe(1);
        harness.Store.Runs.Single().Outcome.ShouldBe(JobRunOutcomes.Completed);
    }

    [Fact]
    public async Task AC_2_ManualTrigger_WhenPausedJobThrows_ReportsFailedNormally()
    {
        using var harness = PauseHarness.Create(PauseHarness.MemoryConfig(BrokenJob.JobName));

        var result = await harness.TriggerAsync(BrokenJob.JobName, JobTriggerSources.Manual);
        await harness.Runner.RunOnceAsync(default);

        result.Status.ShouldBe(JobTriggerStatus.Accepted);
        var run = harness.Store.Runs.Single();
        run.Outcome.ShouldBe(JobRunOutcomes.Failed);
        run.Error.ShouldBe("boom");
    }

    [Fact]
    public async Task AC_2_ScheduledRunQueuedBeforeThePause_WhenClaimed_IsCancelledNotExecuted()
    {
        var config = PauseHarness.MemoryConfig();
        using var harness = PauseHarness.Create(config);
        (await harness.TriggerAsync(NightlyJob.JobName, JobTriggerSources.Scheduled))
            .Status.ShouldBe(JobTriggerStatus.Accepted);

        config["Jobs:Paused:0"] = NightlyJob.JobName;
        config.Reload();
        (await harness.Runner.RunOnceAsync(default)).ShouldBeTrue();

        harness.Log.Count(NightlyJob.JobName).ShouldBe(0);
        var run = harness.Store.Runs.Single();
        run.Outcome.ShouldBe(JobRunOutcomes.Cancelled);
        run.Error.ShouldNotBeNull().ShouldContain("Jobs:Paused");
    }

    [Fact]
    public async Task AC_3_PausedGauge_WhenJobPausedThenResumed_ReportsOneThenZero()
    {
        var config = PauseHarness.MemoryConfig(NightlyJob.JobName);
        using var harness = PauseHarness.Create(config);
        using var capture = new PauseMeterCapture(harness.Service);
        _ = harness.Provider.GetRequiredService<JobMetrics>();

        capture.Latest(JobMetricNames.Paused, NightlyJob.JobName).ShouldBe(1d);
        capture.Latest(JobMetricNames.Paused, BrokenJob.JobName).ShouldBe(0d);

        config["Jobs:Paused:0"] = null;
        config.Reload();

        capture.Latest(JobMetricNames.Paused, NightlyJob.JobName).ShouldBe(0d);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AC_3_PausedWatchedJob_IsNeverStaleAndItsClockRestartsOnResume()
    {
        var config = PauseHarness.MemoryConfig();
        using var harness = PauseHarness.Create(config);
        using var capture = new PauseMeterCapture(harness.Service);
        var monitor = harness.Provider.GetRequiredService<IJobStalenessMonitor>();
        var longAgo = DateTimeOffset.UtcNow.AddDays(-3);
        harness.Store.Seed(new JobRun
        {
            JobName = WatchedNightlyJob.JobName,
            TriggerSource = JobTriggerSources.Scheduled,
            TriggeredAt = longAgo,
            StartedAt = longAgo,
            CompletedAt = longAgo,
            Outcome = JobRunOutcomes.Completed,
        });

        // Control: unpaused, 3 days past a 2h threshold -> stale. Proves the probe can see staleness at all.
        (await monitor.CheckOnceAsync(default)).ShouldHaveSingleItem().Job.ShouldBe(WatchedNightlyJob.JobName);
        capture.Latest(JobMetricNames.Stale, WatchedNightlyJob.JobName).ShouldBe(1d);

        config["Jobs:Paused:0"] = WatchedNightlyJob.JobName;
        config.Reload();
        (await monitor.CheckOnceAsync(default)).ShouldBeEmpty();
        capture.Latest(JobMetricNames.Stale, WatchedNightlyJob.JobName).ShouldBe(0d);

        config["Jobs:Paused:0"] = null;
        config.Reload();
        (await monitor.CheckOnceAsync(default)).ShouldBeEmpty();
        capture.Latest(JobMetricNames.Stale, WatchedNightlyJob.JobName).ShouldBe(0d);
    }

    [Fact]
    public void AC_3_PausedGauge_ExportsAsJobsPaused()
    {
        $"{JobMetrics.MeterName}_{JobMetricNames.Paused}".ShouldBe("jobs_paused");
    }

    [Fact]
    public async Task AC_5_ScheduledTrigger_WhenNothingPaused_RunsAsBefore()
    {
        using var harness = PauseHarness.Create(PauseHarness.MemoryConfig());

        var result = await harness.TriggerAsync(NightlyJob.JobName, JobTriggerSources.Scheduled);
        var duplicate = await harness.TriggerAsync(NightlyJob.JobName, JobTriggerSources.Scheduled);
        await harness.Runner.RunOnceAsync(default);

        result.Status.ShouldBe(JobTriggerStatus.Accepted);
        duplicate.Status.ShouldBe(JobTriggerStatus.AlreadyRunning);
        harness.Log.Count(NightlyJob.JobName).ShouldBe(1);
        harness.Store.Runs.Single().Outcome.ShouldBe(JobRunOutcomes.Completed);
    }
}
