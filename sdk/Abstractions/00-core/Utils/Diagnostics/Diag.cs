namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 诊断日志统一入口 — 控制启动/运行时诊断输出（[WIRE] [STEP] [MAIN] [BRIDGE-CTOR] [SKILL-CTOR] [DI] [READY] [CliSession] [TokenBudget] [RUN] 等）
/// 默认隐藏，JCC_VERBOSE=1/true/yes 或 --verbose CLI 参数时显示
/// 对齐 TS 版 verbose 模式，避免污染用户控制台
/// </summary>
public static class Diag
{
    /// <summary>
    /// 环境变量初始化的 verbose 标志 — 从 JCC_VERBOSE 读取一次（不可变）
    /// 接受真值: "1", "true", "yes"（大小写不敏感）
    /// </summary>
    private static readonly bool _envEnabled = IsTruthy(Environment.GetEnvironmentVariable(JccEnvVar.Verbose.ToValue()));

    /// <summary>
    /// 运行时 verbose 覆盖标志 — 由 --verbose CLI 参数通过 EnableVerbose() 设置
    /// 决策: 使用可空 bool? 区分"未设置"与"显式禁用"，当前仅支持启用（true）
    /// </summary>
    private static bool _runtimeEnabled;

    /// <summary>
    /// 是否启用诊断输出 — 环境变量 JCC_VERBOSE 或 --verbose CLI 参数任一为真即激活
    /// </summary>
    public static bool IsVerbose => _envEnabled || _runtimeEnabled;

    /// <summary>
    /// 运行时启用诊断输出 — 供 --verbose CLI 参数调用
    /// 调用后 IsVerbose 永久为 true，等效于 JCC_VERBOSE=1
    /// 必须在任何 Diag.WriteLine 调用前调用（通常在 ParseArgs 中）
    /// </summary>
    public static void EnableVerbose() => _runtimeEnabled = true;

    /// <summary>
    /// 输出生命周期标记到 stderr — 始终输出，不受 verbose 控制
    /// 用于 [READY]/[DONE]/[ALIVE]/[EXIT] 等 E2E 测试依赖的进程状态标记
    /// </summary>
    public static void WriteLifecycle(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
    }

    /// <summary>
    /// 输出诊断行到 stderr — 仅在 IsVerbose=true 时输出
    /// 用法: Diag.WriteLine("[STEP] ApiKeyCheck start");
    /// </summary>
    public static void WriteLine(string? message = null)
    {
        if (!IsVerbose) return;
        if (message is null)
            Console.Error.WriteLine();
        else
            Console.Error.WriteLine(message);
    }

    /// <summary>
    /// 输出带格式的诊断行 — 仅在 IsVerbose=true 时输出
    /// 用法: Diag.WriteLine($"[WIRE] ISkillService OK ({elapsed}ms)");
    /// </summary>
    public static void WriteLine(FormattableString message)
    {
        if (!IsVerbose) return;
        Console.Error.WriteLine(message);
    }

    private static bool IsTruthy(string? value)
        => value is "1" or "true" or "yes" or "TRUE" or "True" or "YES" or "Yes";
}
