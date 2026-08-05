namespace Guard.Hooks.Tests;

/// <summary>
/// AsyncHookRegistry 多级报错回归测试
/// 验证钩子 JSON 输出解析失败时以 LogWarning 到达日志（原为 Trace.WriteLine，生产环境无 listener 即不可见），且不崩溃。
/// </summary>
public sealed class HookAsyncRegistryLoggingTests
{
    private sealed class CapturingLogger : ILogger<AsyncHookRegistry>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private static IAsyncHookProcess CreateProcess(string stdout)
    {
        var mock = new Mock<IAsyncHookProcess>();
        mock.SetupGet(p => p.Status).Returns(AsyncHookProcessStatus.Completed);
        mock.SetupGet(p => p.ExitCode).Returns(0);
        mock.Setup(p => p.GetStdoutAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stdout);
        mock.Setup(p => p.GetStderrAsync(It.IsAny<CancellationToken>())).ReturnsAsync(string.Empty);
        return mock.Object;
    }

    [Fact]
    public async Task MalformedHookJson_LogsWarning_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var logger = new CapturingLogger();
        var registry = new AsyncHookRegistry(logger);

        registry.Register(new PendingAsyncHook
        {
            ProcessId = "p1",
            HookId = "h1",
            HookName = "test-hook",
            HookEvent = HookEvent.SessionStart,
            Command = "cmd",
            Process = CreateProcess("{\"a\":}")
        });

        var responses = await registry.CheckForResponsesAsync(cts.Token).ConfigureAwait(true);

        responses.Should().HaveCount(1);
        logger.Warnings.Should().Contain(m => m.Contains("JSON"));
    }
}