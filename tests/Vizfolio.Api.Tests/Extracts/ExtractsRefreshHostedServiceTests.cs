using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Vizfolio.Application.Extracts.Abstractions;
using Vizfolio.Application.Extracts.Models;
using Vizfolio.Infrastructure.Extracts;
using Vizfolio.Infrastructure.Extracts.Hosted;

namespace Vizfolio.Api.Tests.Extracts;

public sealed class ExtractsRefreshHostedServiceTests
{
    [Fact]
    public async Task Runs_securities_then_funds_then_relink_in_order_on_startup()
    {
        var log = new List<string>();
        var sp = BuildScope(log);
        var gate = new ImportRunGate();
        var options = WrapOptions(new GitHubExtractOptions
        {
            Schedule = new ExtractSchedule { Enabled = true, RunOnStartup = true, Interval = TimeSpan.FromMilliseconds(50), StartupDelay = TimeSpan.Zero }
        });

        var service = new ExtractsRefreshHostedService(sp, gate, options, NullLogger<ExtractsRefreshHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        var run = service.StartAsync(cts.Token);
        await WaitForAsync(() => log.Count >= 3);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);
        await run;

        log.Take(3).ShouldBe(["securities", "funds", "relink"]);
    }

    [Fact]
    public async Task Returns_immediately_when_schedule_is_disabled()
    {
        var log = new List<string>();
        var sp = BuildScope(log);
        var gate = new ImportRunGate();
        var options = WrapOptions(new GitHubExtractOptions
        {
            Schedule = new ExtractSchedule { Enabled = false, RunOnStartup = true }
        });

        var service = new ExtractsRefreshHostedService(sp, gate, options, NullLogger<ExtractsRefreshHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Skips_run_when_gate_is_already_held()
    {
        var log = new List<string>();
        var sp = BuildScope(log);
        var gate = new ImportRunGate();
        gate.TryAcquire(out var handle).ShouldBeTrue();
        var options = WrapOptions(new GitHubExtractOptions
        {
            Schedule = new ExtractSchedule { Enabled = true, RunOnStartup = true, Interval = TimeSpan.FromHours(1), StartupDelay = TimeSpan.Zero }
        });

        var service = new ExtractsRefreshHostedService(sp, gate, options, NullLogger<ExtractsRefreshHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        log.ShouldBeEmpty();
        handle!.Dispose();
    }

    private static IServiceScopeFactory BuildScope(List<string> log)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISecuritiesImporter>(_ => new RecordingSecuritiesImporter(log));
        services.AddScoped<IFundsImporter>(_ => new RecordingFundsImporter(log));
        services.AddScoped<IHoldingRelinker>(_ => new RecordingRelinker(log));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static IOptionsMonitor<GitHubExtractOptions> WrapOptions(GitHubExtractOptions options)
        => new StaticOptionsMonitor<GitHubExtractOptions>(options);

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            if (predicate()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition not met within timeout.");
    }

    private sealed class RecordingSecuritiesImporter(List<string> log) : ISecuritiesImporter
    {
        public Task<ImportResult> ImportAsync(SecuritiesImportOptions options, CancellationToken cancellationToken = default)
        {
            log.Add("securities");
            return Task.FromResult(ImportResult.Empty(TimeSpan.Zero));
        }
    }

    private sealed class RecordingFundsImporter(List<string> log) : IFundsImporter
    {
        public Task<ImportResult> ImportAsync(FundsImportOptions options, CancellationToken cancellationToken = default)
        {
            log.Add("funds");
            return Task.FromResult(ImportResult.Empty(TimeSpan.Zero));
        }
    }

    private sealed class RecordingRelinker(List<string> log) : IHoldingRelinker
    {
        public Task<int> RelinkAsync(CancellationToken cancellationToken = default)
        {
            log.Add("relink");
            return Task.FromResult(0);
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
