namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 诊断日志统一入口 — 控制启动/运行时诊断输出（[WIRE] [STEP] [MAIN] [BRIDGE-CTOR] [SKILL-CTOR] [DI] [READY] [CliSession] [TokenBudget] [RUN] 等）
/// 默认隐藏，JCC_VERBOSE=1/true/yes 或 --verbose CLI 参数时显示
/// 对齐 TS 版 verbose 模式，避免污染用户控制台
/// </summary>
public static class Diag
{
    private static readonly bool _envEnabled = IsTruthy(Environment.GetEnvironmentVariable(JccEnvVar.Verbose.ToValue()));

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

    public static bool IsVerbose => _envEnabled || _runtimeEnabled;

    public static void EnableVerbose() => _runtimeEnabled = true;

    public static void WriteLifecycle(string message)
    {
        WriteToTargets(message);
        DiagnosticLineWritten?.Invoke(null, message);
    }

    public static void WriteLine(string? message = null)
    {
        if (!IsVerbose) return;
        if (message is null)
            WriteToTargets(string.Empty);
        else
        {
            WriteToTargets(message);
            DiagnosticLineWritten?.Invoke(null, message);
        }
    }

    public static void WriteLine(FormattableString message)
    {
        if (!IsVerbose) return;
        var formatted = message.ToString();
        WriteToTargets(formatted);
        DiagnosticLineWritten?.Invoke(null, formatted);
    }

    public static void WriteDiTrace(string message)
    {
        if (!_diTraceEnabled) return;
        WriteToTargets(message);
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
