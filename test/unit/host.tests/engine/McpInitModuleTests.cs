namespace JoinCode.App.Modules.Tests;

/// <summary>
/// McpInitModule 刷新错误降级测试（修复 CI E2E：McpCommand_ShouldListServers 撞
/// ObjectDisposedException"锁 'AsyncLock#2' 已释放"）— 根因：CLI 单命令模式退出释放容器
/// （LocalToolRegistry.DisposeAsync 释放内部 AsyncLock#2）与后台插件链就绪钩子
/// RefreshKernelPluginsAsync 竞态；原 catch 一律 LogError（fail 级）进 stderr 触发 E2E NoErrors 断言红。
/// 降级语义：ObjectDisposedException = 退出竞态良性噪音（进程将亡刷新无意义）→ LogDebug 静默，
/// 对齐 McpPluginBootstrap"宿主关闭取消静默"先例；其余异常保持 LogError。
/// </summary>
public class McpInitModuleTests
{
    /// <summary>ObjectDisposedException — 降级 Debug 不打 Error（退出竞态良性）。</summary>
    [Fact]
    public void HandleRefreshError_ObjectDisposed_DowngradedToDebug()
    {
        var logger = new SpyLogger();
        var ex = new ObjectDisposedException(nameof(AsyncLock), "锁 'AsyncLock#2' 已释放");

        McpInitModule.HandleRefreshError(ex, logger);

        Assert.Empty(logger.ErrorCalls);
        Assert.Contains(logger.DebugCalls, c => c.Contains("退出竞态"));
    }

    /// <summary>其他异常 — 保持 Error 级（真实故障不得静默；消息模板固定，异常文本经 state 传递）。</summary>
    [Fact]
    public void HandleRefreshError_OtherException_KeepsErrorLevel()
    {
        var logger = new SpyLogger();
        var ex = new InvalidOperationException("工具注册表状态损坏");

        McpInitModule.HandleRefreshError(ex, logger);

        Assert.Contains("[MCP] 刷新 kernel.Plugins 失败", logger.ErrorCalls);
        Assert.Empty(logger.DebugCalls);
    }

    /// <summary>logger null — 两分支均不抛（DI 无日志场景）。</summary>
    [Fact]
    public void HandleRefreshError_NullLogger_NoThrow()
    {
        McpInitModule.HandleRefreshError(new ObjectDisposedException("x"), null);
        McpInitModule.HandleRefreshError(new InvalidOperationException("y"), null);
    }

    /// <summary>记录型 logger 假体 — 按级别分桶捕获消息。</summary>
    private sealed class SpyLogger : ILogger<McpInitModule>
    {
        public List<string> ErrorCalls { get; } = [];
        public List<string> DebugCalls { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Error) ErrorCalls.Add(message);
            if (logLevel == LogLevel.Debug) DebugCalls.Add(message);
        }
    }
}
