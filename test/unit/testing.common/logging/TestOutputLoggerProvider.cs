
namespace Testing.Common.Logging;

/// <summary>
/// 测试输出日志提供程序 - 将日志输出到 xUnit 测试输出
/// </summary>
public sealed class TestOutputLoggerProvider : ILoggerProvider {
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// 初始化测试输出日志提供程序
    /// </summary>
    /// <param name="output">测试输出辅助器</param>
    public TestOutputLoggerProvider(ITestOutputHelper output) {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>创建指定类别的日志记录器</summary>
    public ILogger CreateLogger(string categoryName) {
        return new TestOutputLogger(_output, categoryName);
    }

    /// <summary>释放资源</summary>
    public void Dispose() { }
}

/// <summary>
/// 测试输出日志记录器
/// </summary>
public sealed class TestOutputLogger : ILogger {
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    /// <summary>
    /// 初始化测试输出日志记录器
    /// </summary>
    /// <param name="output">测试输出辅助器</param>
    /// <param name="categoryName">类别名称</param>
    public TestOutputLogger(ITestOutputHelper output, string categoryName) {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    }

    /// <summary>开始日志作用域（返回 null，不支持作用域）</summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>判断指定日志级别是否启用（始终返回 true）</summary>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>记录日志 — 将消息写入测试输出，异常时降级到 Trace.WriteLine</summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        try {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null) {
                _output.WriteLine($"Exception: {exception}");
            }
        } catch (Exception ex) {
            // 忽略测试输出异常（测试可能已结束）
            System.Diagnostics.Trace.WriteLine($"测试日志输出异常（测试可能已结束）: {ex.Message}");
        }
    }
}

/// <summary>
/// 测试输出日志记录器（泛型版本）
/// </summary>
public sealed class TestOutputLogger<T> : ILogger<T>, ILogger {
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    /// <summary>
    /// 初始化测试输出日志记录器
    /// </summary>
    /// <param name="output">测试输出辅助器</param>
    public TestOutputLogger(ITestOutputHelper output) {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _categoryName = typeof(T).Name;
    }

    /// <summary>开始日志作用域（返回 null，不支持作用域）</summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>判断指定日志级别是否启用（始终返回 true）</summary>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>记录日志 — 将消息写入测试输出，异常时降级到 Trace.WriteLine</summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        try {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null) {
                _output.WriteLine($"Exception: {exception}");
            }
        } catch (Exception ex2) {
            // 忽略测试输出异常（测试可能已结束）
            System.Diagnostics.Trace.WriteLine($"测试日志输出异常（测试可能已结束）: {ex2.Message}");
        }
    }
}
