using System.Runtime.InteropServices;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Data;
using EggIncognito.Runner.Devices;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.Harvest;
using EggIncognito.Runner.Posting;
using EggIncognito.Runner.Runners;
using EggIncognito.Runner.State;
using EggIncognito.Runner.Trigger;

namespace EggIncognito.Runner;

public static class Program {
    public static async Task<int> Main(string[] args) {
        var options = RunnerOptions.FromEnvironment();
        Directory.CreateDirectory(options.ApkStashDir);

        using var shutdown = new CancellationTokenSource();
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => {
            ctx.Cancel = true;
            shutdown.Cancel();
        });
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            shutdown.Cancel();
        };
        var ct = shutdown.Token;

        var http = new HttpClient();
        var poster = new EventPoster(http, options.EventUrl, options.EventSecret);
        var clientVersion = new LibegincClientVersionReader();
        var deps = new RunnerDeps(
            new CSharpProtoExtractor(), clientVersion, options.ApkStashDir, options.IosBinaryPath,
            options.PreviousClientVersion, options.Package,
            evt => poster.PostAsync(evt).GetAwaiter().GetResult());

        var runnerDb = RunnerDb.FromEnv(key => RunnerOptions.Env(key));
        var devices = RunnerDeviceSource.Read(options.DevicesDir);
        var set = RunnerSet.Build(devices, deps, () => LegacyRunner(deps, options));
        if (set.Runners.Count == 0) {
            Console.Error.WriteLine("no runnable devices configured (set DEVICES_DIR or PLATFORM+target)");
            return 1;
        }

        if (args.Contains("--once")) return RunOnce(set, args.Contains("--force"));

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var platforms = BuildPlatforms(loggerFactory);
        var harvester = await StartHarvesterAsync(runnerDb, platforms, loggerFactory, ct);
        var trigger = await StartTriggerAsync(options, set, poster, http, clientVersion, runnerDb, harvester, platforms,
            loggerFactory, ct);

        await WatchAsync(options, set, runnerDb, platforms, harvester, loggerFactory, ct);
        Console.WriteLine("runner shutting down");
        await StopTriggerAsync(trigger);
        return 0;
    }

    private static int RunOnce(RunnerSet set, bool force) {
        foreach (var r in set.Runners) {
            var outcome = r.RunOnce(force);
            Console.WriteLine($"{r.Platform} once force={force}: {outcome.Detail} build={outcome.Build}");
        }

        return 0;
    }

    private static DevicePlatforms BuildPlatforms(ILoggerFactory loggerFactory) {
        var procRunner = new ProcessRunner();
        var appConfig = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var captureConfig = DeviceCaptureConfig.Bind(appConfig);
        var connections = new DeviceConnectionFactory(procRunner, captureConfig);
        return new DevicePlatforms([
            new AndroidPlatform(procRunner, appConfig, [], [], [], [], [],
                loggerFactory.CreateLogger<AndroidPlatform>()),
            new IosPlatform(connections, captureConfig, procRunner, [], [], [], [], [],
                loggerFactory.CreateLogger<IosPlatform>())
        ]);
    }

    private static async Task<HarvestScheduler?> StartHarvesterAsync(RunnerDb? runnerDb, DevicePlatforms platforms,
        ILoggerFactory loggerFactory, CancellationToken ct) {
        if (runnerDb is null) return null;

        var harvester = new HarvestScheduler(runnerDb, platforms, loggerFactory);
        using var resetCtx = runnerDb.NewContext();
        int stuck = await new DeviceStateStore(resetCtx).ResetRunningAsync(ct);
        if (stuck > 0) Console.WriteLine($"cleared {stuck} interrupted harvest(s)");
        return harvester;
    }

    private static async Task<WebApplication?> StartTriggerAsync(RunnerOptions options, RunnerSet set,
        EventPoster poster, HttpClient http, IClientVersionReader clientVersion, RunnerDb? runnerDb,
        HarvestScheduler? harvester, DevicePlatforms platforms, ILoggerFactory loggerFactory, CancellationToken ct) {
        if (options.TriggerSecret.Length == 0) return null;

        var handler = new DeviceResyncHandler(options.TriggerSecret, set.ById);
        var extractHandler = new ApkPureExtractHandler(
            options.TriggerSecret, new ApkPureDownloader(http),
            new CSharpProtoExtractor(), clientVersion,
            new ClientVersionState(Path.Combine(options.ApkStashDir, "clientversion-apkpure.txt"),
                options.PreviousClientVersion),
            evt => poster.PostAsync(evt));

        var trigger = runnerDb is not null && harvester is not null
            ? TriggerListener.Build(options.TriggerUrls, handler, extractHandler,
                new DeviceProbeApi(options.TriggerSecret, runnerDb, platforms, TimeProvider.System, loggerFactory),
                new HarvestApi(options.TriggerSecret, runnerDb, harvester))
            : TriggerListener.Build(options.TriggerUrls, handler, extractHandler);

        await trigger.StartAsync(ct);
        Console.WriteLine($"resync trigger listening on {options.TriggerUrls}");
        return trigger;
    }

    private static async Task StopTriggerAsync(WebApplication? trigger) {
        if (trigger is null) return;

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try {
            await trigger.StopAsync(stopCts.Token);
        } catch (OperationCanceledException ex) {
            Console.Error.WriteLine($"trigger listener did not stop in time: {ex.Message}");
        }
    }

    private static async Task WatchAsync(RunnerOptions options, RunnerSet set, RunnerDb? runnerDb,
        DevicePlatforms platforms, HarvestScheduler? harvester, ILoggerFactory loggerFactory, CancellationToken ct) {
        var sweepLogger = loggerFactory.CreateLogger("RunnerProbeSweep");
        Console.WriteLine($"runner watching {set.Runners.Count} device(s) every {options.PollIntervalSeconds}s");
        while (!ct.IsCancellationRequested) {
            if (!await TickRunnersAsync(set, ct)) break;
            if (runnerDb is not null) {
                try {
                    await RunnerProbeSweep.RunAsync(runnerDb, platforms, TimeProvider.System, sweepLogger, ct);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Console.Error.WriteLine($"probe sweep error: {ex.Message}");
                }
            }

            if (harvester is not null) {
                try {
                    await harvester.PokeAllAsync(ct);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Console.Error.WriteLine($"harvest poke error: {ex.Message}");
                }
            }

            try {
                await Task.Delay(options.PollIntervalSeconds * 1000, ct);
            } catch (OperationCanceledException) {
                break;
            }
        }
    }

    private static async Task<bool> TickRunnersAsync(RunnerSet set, CancellationToken ct) {
        foreach (var r in set.Runners) {
            if (ct.IsCancellationRequested) return false;
            try {
                var tick = Task.Run(() => r.RunOnce(force: false));
                var done = await Task.WhenAny(tick, Task.Delay(Timeout.Infinite, ct));
                if (done != tick) return false;
                var outcome = await tick;
                if (outcome.Emitted) Console.WriteLine($"{r.Platform} emitted build {outcome.Build}");
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                Console.Error.WriteLine($"{r.Platform} tick error: {ex.Message}");
            }
        }

        return true;
    }

    private static IDeviceRunner? LegacyRunner(RunnerDeps deps, RunnerOptions options) {
        string platform = RunnerOptions.Env("PLATFORM");
        if (platform.Length == 0) return null;

        string target = RunnerOptions.Env("ADB_TARGET", "127.0.0.1:5555");
        string stateFile = RunnerOptions.Env("STATE_FILE", $"state-{platform}.json");
        return platform switch {
            "android" => new AndroidRunner(
                new AdbClient(target), deps.Proto, new VersionState(stateFile),
                deps.ClientVersion,
                new ClientVersionState(Path.Combine(deps.ApkStashDir, $"clientversion-{platform}.txt"),
                    deps.PrevClientVersion),
                options.Package, deps.ApkStashDir, deps.OnNewVersion),
            "ios" => new IosRunner(options.IosBinaryPath, new VersionState(stateFile), options.Package,
                deps.OnNewVersion),
            _ => null
        };
    }
}
