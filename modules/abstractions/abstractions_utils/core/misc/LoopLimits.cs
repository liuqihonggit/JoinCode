namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 循环上限 — 两套 ReAct 循环共用的迭代/回灌上限唯一数据源。
/// <para>原硬编码 128（QueryEngine 配置默认 + QueryLoopMiddleware）与 5（Stop hook 回灌）统一委托到这里，
/// 通过 JCC_MAX_TOOL_CALL_ITERATIONS / JCC_MAX_STOP_HOOK_REPLY_ROUNDS 覆盖。</para>
/// </summary>
public static class LoopLimits {
    /// <summary>默认单回合最大工具调用轮次。</summary>
    public const int DefaultMaxToolCallIterations = 128;

    /// <summary>默认 Stop hook 回灌轮次上限。</summary>
    public const int DefaultMaxStopHookReplyRounds = 5;

    /// <summary>单回合最大工具调用轮次 — 未设置或非正数时回退默认值。</summary>
    public static int MaxToolCallIterations => ReadPositive(
        JccEnvVar.MaxToolCallIterations.ToValue(), DefaultMaxToolCallIterations);

    /// <summary>Stop hook 回灌轮次上限 — 未设置或非正数时回退默认值。</summary>
    public static int MaxStopHookReplyRounds => ReadPositive(
        JccEnvVar.MaxStopHookReplyRounds.ToValue(), DefaultMaxStopHookReplyRounds);

    private static int ReadPositive(string name, int fallback) {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
