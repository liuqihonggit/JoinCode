namespace NativeTui.Core.Repl;

/// <summary>
/// 思考状态 — 对齐基准 Spinner.tsx 的 thinkingStatus 三态（'thinking' | number | null）。
/// 生命周期状态机（最短 2s 展示 + 2s 清除）属 M6 应用层；本类型仅为渲染输入。
/// </summary>
public abstract record ThinkingState
{
    /// <summary>未在思考。</summary>
    public sealed record Off : ThinkingState;

    /// <summary>思考中（effortSuffix 为推理力度后缀，如 " hard"）。</summary>
    public sealed record Active(string EffortSuffix) : ThinkingState;

    /// <summary>思考结束（展示持续时长 ms → "thought for Ns"）。</summary>
    public sealed record ThoughtFor(long DurationMs) : ThinkingState;

    /// <summary>默认关闭实例。</summary>
    public static readonly ThinkingState None = new Off();
}
