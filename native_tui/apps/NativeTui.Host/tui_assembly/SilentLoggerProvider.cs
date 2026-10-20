namespace NativeTui.Host;

/// <summary>
/// TUI 静默日志 provider（修复：引擎 Console 日志直写 stderr 与 inline 渲染交错 —
/// 相对光标追踪错乱，旧帧输入框线对/footer 被错位固化成多条横向残影）。
/// 错误反馈走 EngineTurnClient Error 事件链（● 回合执行失败），引擎日志全静默。
/// 自研实现 — 跨 modules 包版本统一下 NullLoggerProvider 解析不可靠（ADR 0140 Host 层自持组装件）。
/// </summary>
internal sealed class SilentLoggerProvider : ILoggerProvider
{
    /// <summary>共享单例（无状态）。</summary>
    internal static readonly SilentLoggerProvider Instance = new();

    /// <summary>静默 logger 单例。</summary>
    private static readonly ILogger SilentLogger = new NullLogger();

    private SilentLoggerProvider() { }

    /// <summary>创建静默 logger。</summary>
    public ILogger CreateLogger(string categoryName) => SilentLogger;

    /// <summary>无资源。</summary>
    public void Dispose() { }

    /// <summary>全吞 logger — 全级别禁用、零输出。</summary>
    private sealed class NullLogger : ILogger
    {
        /// <summary>无 scope。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>全级别禁用。</summary>
        public bool IsEnabled(LogLevel logLevel) => false;

        /// <summary>零输出。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
