namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// WorkflowPluginHost.InitializeAsync 退出竞态噪音降级测试（Bug 7 修复）：
/// 根因：CLI 空配置快速退出 → AsyncHostWrapper.DisposeAsync 取消后台插件链 →
/// 插件 InitializeAsync 内 GetRequiredService 撞 ObjectDisposedException（容器已释放）→
/// 原 catch 一律 LogError(ex,...) 打印完整堆栈给用户。
/// 降级语义：ObjectDisposedException = 退出竞态良性噪音（进程将亡初始化无意义）→ LogDebug 静默，
/// 对齐 McpInitModule.HandleRefreshError 先例；其余异常保持 Error 但只传 Message 不传异常对象（不打印堆栈）。
/// </summary>
public sealed class WorkflowPluginHostInitErrorDowngradeTests
{
    /// <summary>ObjectDisposedException — 降级 Debug 不打 Error（退出竞态良性噪音）。</summary>
    [Fact]
    public async Task InitializeAsync_ObjectDisposedException_DowngradedToDebug()
    {
        var logger = new SpyLogger();
        var plugin = new StubWorkflowPlugin("disposed-plugin")
        {
            InitException = new ObjectDisposedException(nameof(IServiceProvider), "Cannot access a disposed object.")
        };
        await using var host = new WorkflowPluginHost(plugin, logger: logger);

        await host.LoadAsync(CancellationToken.None);
        var result = await host.InitializeAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        Assert.Empty(logger.ErrorCalls);
        Assert.Contains(logger.DebugCalls, c => c.Contains("退出竞态"));
    }

    /// <summary>其他异常 — 保持 Error 级但不传异常对象（不打印堆栈给用户）。</summary>
    [Fact]
    public async Task InitializeAsync_OtherException_KeepsErrorLevel_NoExceptionObject()
    {
        var logger = new SpyLogger();
        var plugin = new StubWorkflowPlugin("other-fail-plugin")
        {
            InitException = new InvalidOperationException("插件内部错误")
        };
        await using var host = new WorkflowPluginHost(plugin, logger: logger);

        await host.LoadAsync(CancellationToken.None);
        var result = await host.InitializeAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        Assert.Contains(logger.ErrorCalls, c => c.Contains("other-fail-plugin"));
        Assert.False(logger.LastErrorHasException, "Error 日志不应传入异常对象（避免打印堆栈给用户）");
    }

    /// <summary>记录型 logger 假体 — 按级别分桶捕获消息 + 异常对象传入标记。</summary>
    private sealed class SpyLogger : ILogger
    {
        public List<string> ErrorCalls { get; } = [];
        public List<string> DebugCalls { get; } = [];
        public bool LastErrorHasException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Error)
            {
                ErrorCalls.Add(message);
                LastErrorHasException = exception is not null;
            }
            if (logLevel == LogLevel.Debug)
                DebugCalls.Add(message);
        }
    }
}
