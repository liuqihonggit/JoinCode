namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 诊断日志统一入口 — 控制启动/运行时诊断输出（[WIRE] [STEP] [MAIN] [BRIDGE-CTOR] [SKILL-CTOR] [DI] [READY] [CliSession] [TokenBudget] [RUN] 等）
/// 默认隐藏，JCC_DEBUGLOG=1/true/yes 或 --debuglog CLI 参数时显示
/// 对齐 TS 版 debuglog 模式，避免污染用户控制台
/// </summary>
public static class Diag
{
    private static readonly bool _envEnabled = IsTruthy(Environment.GetEnvironmentVariable(JccEnvVar.DebugLog.ToValue()));

    private static readonly bool _diTraceEnabled = Environment.GetEnvironmentVariable(JccEnvVar.DiTrace.ToValue()) == "1";

    private static bool _runtimeEnabled;

    /// <summary>
    /// 诊断输出目标 — JCC_DIAG_TARGET 环境变量控制（stderr/stdout/both）
    /// 默认 stderr，debug 阶段可设为 stdout 或 both 避免管道缓冲问题
    /// </summary>
    private static readonly string _diagTarget =
        (Environment.GetEnvironmentVariable("JCC_DIAG_TARGET") ?? "stderr").ToLowerInvariant();

    /// <summary>
    /// 诊断行输出事件 — 每次 WriteLine/WriteLifecycle 输出时触发
    /// 用于外部订阅者（如 DoctorSseClient）捕获诊断行并转发
    /// </summary>
    public static event EventHandler<string>? DiagnosticLineWritten;

    public static bool IsDebugLog => _envEnabled || _runtimeEnabled;

    public static void EnableDebugLog() => _runtimeEnabled = true;

    public static void WriteLifecycle(string message)
    {
        WriteToTargets(message);
        DiagnosticLineWritten?.Invoke(null, message);
    }

    public static void WriteLine(string? message = null)
    {
        if (message is null)
        {
            if (IsDebugLog) WriteToTargets(string.Empty);
            DiagnosticLineWritten?.Invoke(null, string.Empty);
        }
        else
        {
            if (IsDebugLog) WriteToTargets(message);
            DiagnosticLineWritten?.Invoke(null, message);
        }
    }

    public static void WriteLine(FormattableString message)
    {
        var formatted = message.ToString();
        if (IsDebugLog) WriteToTargets(formatted);
        DiagnosticLineWritten?.Invoke(null, formatted);
    }

    public static void WriteDiTrace(string message)
    {
        if (!_diTraceEnabled) return;
        WriteToTargets(message);
    }

    /// <summary>
    /// 错误诊断日志 — 无条件输出到 stderr，确保 E2E 测试和 CI 能捕获完整错误信息
    /// 用于关键错误处理点（ChatErrorHandlingMiddleware、PermissionAwareToolExecutor 等）
    /// 不受 JCC_DEBUGLOG 控制，因为错误必须始终可见
    /// </summary>
    /// <param name="context">错误上下文描述（如 "[ChatErrorHandling] Turn=1"）</param>
    /// <param name="exception">异常对象（null 时只输出 context）</param>
    public static void WriteError(string context, Exception? exception = null)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        if (exception is null)
        {
            var line = $"[DIAG-ERR] {timestamp} {context}";
            WriteToTargets(line);
            DiagnosticLineWritten?.Invoke(null, line);
            return;
        }

        // 主异常
        var mainLine = $"[DIAG-ERR] {timestamp} {context}: {exception.GetType().Name}: {exception.Message}";
        WriteToTargets(mainLine);
        DiagnosticLineWritten?.Invoke(null, mainLine);

        // 堆栈（截断到 2000 字符避免 stderr 爆炸）
        var stack = exception.StackTrace;
        if (!string.IsNullOrEmpty(stack))
        {
            var stackPreview = stack.Length > 2000 ? stack[..2000] + "...(truncated)" : stack;
            WriteToTargets($"[DIAG-ERR-STACK] {stackPreview}");
        }

        // 内部异常链（最多 5 层）
        var inner = exception.InnerException;
        var depth = 0;
        while (inner is not null && depth < 5)
        {
            depth++;
            WriteToTargets($"[DIAG-ERR-INNER-{depth}] {inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
        }
    }

    private static void WriteToTargets(string message)
    {
        switch (_diagTarget)
        {
            case "stdout":
                Console.Out.WriteLine(message);
                Console.Out.Flush();
                break;
            case "both":
                Console.Error.WriteLine(message);
                Console.Error.Flush();
                Console.Out.WriteLine(message);
                Console.Out.Flush();
                break;
            default:
                Console.Error.WriteLine(message);
                Console.Error.Flush();
                break;
        }
    }

    private static bool IsTruthy(string? value)
        => value is "1" or "true" or "yes" or "TRUE" or "True" or "YES" or "Yes";
}
