namespace Host.Tests.Engine;

/// <summary>
/// EngineApplicationBuilder 引擎构建器测试 — ADR 0143 引擎库（从 cli ApplicationBuilder 拆分引擎部分）。
/// 契约：① 模块按 Order 升序执行 ConfigureServices/ConfigureAsync，config+options 注册为单例；
/// ② 宿主注入的 ILoggerProvider 生效（ClearProviders 后仅保留注入 provider）。
/// </summary>
public sealed class EngineApplicationBuilderTests {
    [Fact]
    public async Task BuildHost_ModulesExecuteInOrder_AndRegistersConfigAndOptionsSingletons() {
        var calls = new List<string>();
        var options = new CommandLineOptions { Prompt = "engine-test" };
        var config = new WorkflowConfig();

        var builder = new EngineApplicationBuilder()
            .UseModule(new RecordingModule("late", 80, calls))
            .UseModule(new RecordingModule("early", 20, calls))
            .UseModule<MarkerServiceModule>();

        using var host = builder.BuildHost(config, options);
        await builder.ConfigureModulesAsync(host.Services);

        // MarkerServiceModule(泛型重载, Order=100) 不写 calls，经 MarkerService 单例断言覆盖
        Assert.Equal(
            new[] {
                "early-services", "late-services",
                "early-configure", "late-configure",
            }, calls);
        Assert.Same(config, host.Services.GetRequiredService<WorkflowConfig>());
        Assert.Same(options, host.Services.GetRequiredService<CommandLineOptions>());
        Assert.NotNull(host.Services.GetRequiredService<MarkerService>());
    }

    [Fact]
    public void BuildHost_InjectedLoggerProvider_IsUsedByLoggerFactory() {
        var provider = new CapturingLoggerProvider();

        using var host = new EngineApplicationBuilder()
            .BuildHost(new WorkflowConfig(), new CommandLineOptions(), provider);

        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("engine-test")
            .LogWarning("hello-from-engine");

        var entry = Assert.Single(provider.Entries);
        Assert.Equal("engine-test", entry.Category);
        Assert.Equal("hello-from-engine", entry.Message);
    }

    private sealed class RecordingModule(string name, int order, List<string> calls) : IAppModule {
        public int Order => order;

        public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
            Assert.NotNull(context.Options);
            Assert.NotNull(context.Config);
            calls.Add($"{name}-services");
        }

        public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) {
            calls.Add($"{name}-configure");
            return Task.CompletedTask;
        }
    }

    private sealed class MarkerServiceModule : IAppModule {
        public int Order => 100;

        public void ConfigureServices(IServiceCollection services, AppModuleContext context)
            => services.AddSingleton<MarkerService>();

        public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class MarkerService { }

    private sealed record CapturedEntry(string Category, string Message);

    private sealed class CapturingLogger(string category, IList<CapturedEntry> entries) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            entries.Add(new CapturedEntry(category, formatter(state, exception)));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider {
        public IList<CapturedEntry> Entries { get; } = new List<CapturedEntry>();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose() { }
    }
}
