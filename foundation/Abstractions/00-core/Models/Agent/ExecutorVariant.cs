namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 执行者变体 — 不同专长的执行者 Agent
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 ExecutorVariantConstants + ExecutorVariantExtensions
/// </summary>
public enum ExecutorVariant
{
    /// <summary>
    /// 代码执行者 — 代码读写编辑
    /// </summary>
    [EnumValue("code")] Code,

    /// <summary>
    /// 搜索执行者 — 代码搜索导航（只读）
    /// </summary>
    [EnumValue("search")] Search,

    /// <summary>
    /// 探索执行者 — 快速代码库探索（只读，一次性）
    /// </summary>
    [EnumValue("explore")] Explore,

    /// <summary>
    /// 计划执行者 — 架构设计与实施计划（只读，一次性）
    /// </summary>
    [EnumValue("plan")] Plan,

    /// <summary>
    /// 医生执行者 — 自举复盘与修复（后台运行，Cron 调度）
    /// </summary>
    [EnumValue("doctor")] Doctor,

    /// <summary>
    /// 验证执行者 — 验证代码正确性、质量和安全性
    /// </summary>
    [EnumValue("verification")] Verification,

    /// <summary>
    /// 引导执行者 — 提供使用指导和最佳实践
    /// </summary>
    [EnumValue("claudeCodeGuide")] ClaudeCodeGuide,

    /// <summary>
    /// 上下文压缩执行者 — 智能压缩和管理上下文
    /// </summary>
    [EnumValue("contextCompression")] ContextCompression,

    [EnumValue("teammate")] Teammate
}

/// <summary>
/// 一次性执行者变体 — Explore/Plan 运行一次即返回报告，不会通过 SendMessage 继续
/// 结果中省略 agentId/SendMessage 提示，节省 token
/// </summary>
public static class OneShotExecutorVariants
{
    private static readonly FrozenSet<string> Variants = FrozenSet.Create(
        StringComparer.Ordinal,
        ExecutorVariant.Explore.ToValue(),
        ExecutorVariant.Plan.ToValue());

    /// <summary>
    /// 判断指定执行者变体是否为一次性执行者
    /// </summary>
    public static bool IsOneShot(ExecutorVariant variant) => Variants.Contains(variant.ToValue());

    /// <summary>
    /// 判断指定执行者变体字符串是否为一次性执行者
    /// </summary>
    public static bool IsOneShot(string variant) => Variants.Contains(variant);
}
