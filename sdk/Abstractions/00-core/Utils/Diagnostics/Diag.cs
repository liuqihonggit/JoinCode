namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 诊断日志统一入口 — 控制启动/运行时诊断输出（[WIRE] [STEP] [MAIN] [BRIDGE-CTOR] [SKILL-CTOR] [DI] [READY] [CliSession] [TokenBudget] [RUN] 等）
/// 默认隐藏，JCC_VERBOSE=1/true/yes 时显示
/// 对齐 TS 版 verbose 模式，避免污染用户控制台
/// </summary>
public static class Diag
{
    /// <summary>
    /// 是否启用诊断输出 — 从 JCC_VERBOSE 环境变量初始化一次（不可变）
    /// 接受真值: "1", "true", "yes"（大小写不敏感）
    /// </summary>
    public static bool IsVerbose { get; } = IsTruthy(Environment.GetEnvironmentVariable(JccEnvVar.Verbose.ToValue()));

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
