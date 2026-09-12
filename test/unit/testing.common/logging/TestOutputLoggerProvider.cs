
namespace Testing.Common.Logging;

/// <summary>
/// 测试输出日志提供程序 - 将日志输出到 xUnit 测试输出
/// </summary>
public sealed class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public TestOutputLoggerProvider(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestOutputLogger(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// 测试输出日志记录器
/// </summary>
public sealed class TestOutputLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public TestOutputLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch (Exception ex)
        {
            // 忽略测试输出异常（测试可能已结束）
            System.Diagnostics.Trace.WriteLine($"测试日志输出异常（测试可能已结束）: {ex.Message}");
        }
    }
}

/// <summary>
/// 测试输出日志记录器（泛型版本）
/// </summary>
public sealed class TestOutputLogger<T> : ILogger<T>, ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public TestOutputLogger(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _categoryName = typeof(T).Name;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch (Exception ex2)
        {
            // 忽略测试输出异常（测试可能已结束）
            System.Diagnostics.Trace.WriteLine($"测试日志输出异常（测试可能已结束）: {ex2.Message}");
        }
    }
}
