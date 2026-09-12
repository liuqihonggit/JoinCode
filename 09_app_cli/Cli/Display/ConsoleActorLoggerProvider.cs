namespace JoinCode.Cli.Display;

/// <summary>
/// 自定义 LoggerProvider — 直接输出到 ConsoleActor，确保 logger 输出经过 Actor 串行化。
/// <para>替代 AddConsole（AnsiLogConsole 在构造时捕获 Console.Out，可能绕过 SetOut 重定向）。</para>
/// <para>格式对齐 AddConsole 的 simple formatter：warn: CategoryName[EventId]\n      Message</para>
/// <para>架构决策见 ADR 0100。</para>
/// </summary>
public sealed class ConsoleActorLoggerProvider : ILoggerProvider
{
    private readonly ConsoleActor _actor;
    private readonly LogLevel _minLevel;

    public ConsoleActorLoggerProvider(ConsoleActor actor, LogLevel minLevel = LogLevel.Warning)
    {
        _actor = actor;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string name) => new ConsoleActorLogger(name, _actor, _minLevel);

    public void Dispose() { }
}

internal sealed class ConsoleActorLogger : ILogger
{
    private readonly string _name;
    private readonly ConsoleActor _actor;
    private readonly LogLevel _minLevel;

    internal ConsoleActorLogger(string name, ConsoleActor actor, LogLevel minLevel)
    {
        _name = name;
        _actor = actor;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var levelStr = logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "????"
        };

        var sb = new StringBuilder();
        sb.Append(levelStr).Append(": ").Append(_name);
        if (eventId.Id != 0)
            sb.Append('[').Append(eventId.Id).Append(']');
        sb.Append('\n').Append("      ").Append(message);

        if (exception is not null)
        {
            sb.Append('\n').Append("      ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
            if (exception.StackTrace is not null)
            {
                var stackLines = exception.StackTrace.Split('\n');
                foreach (var line in stackLines)
                    sb.Append('\n').Append("      ").Append(line.TrimStart());
            }
        }

        _actor.WriteLine(sb.ToString());
    }
}
