namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 自举修改请求 — 提交给 Guard 审核的修改内容
/// </summary>
public sealed record BootstrapModificationRequest
{
    /// <summary>修改类型</summary>
    public required BootstrapFixType ModificationType { get; init; }

    /// <summary>目标文件路径</summary>
    public required string TargetPath { get; init; }

    /// <summary>原始内容</summary>
    public required string OriginalContent { get; init; }

    /// <summary>提议的修改后内容</summary>
    public required string ProposedContent { get; init; }

    /// <summary>修改理由（LLM 生成的推理）</summary>
    public required string Justification { get; init; }
}

/// <summary>
/// 自举修复类型
/// </summary>
public enum BootstrapFixType
{
    /// <summary>源码修改</summary>
    SourceCodePatch,

    /// <summary>配置修改</summary>
    ConfigChange,

    /// <summary>提示词调整</summary>
    PromptAdjustment,

    /// <summary>规则阈值调整</summary>
    RuleAdjustment,

    /// <summary>复合修复</summary>
    CompositeFix
}

/// <summary>
/// Guard 审核决策
/// </summary>
public sealed record GuardDecision
{
    /// <summary>是否批准</summary>
    public required bool Approved { get; init; }

    /// <summary>拒绝/批准原因</summary>
    public string? Reason { get; init; }

    /// <summary>警告列表</summary>
    public IEnumerable<string> Warnings { get; init; } = [];
}

/// <summary>
/// 自举安全守卫 — 审核自修改内容，防止 Agent 破坏自身
/// </summary>
public interface IBootstrapGuard
{
    /// <summary>
    /// 审核修改请求
    /// </summary>
    Task<GuardDecision> ReviewAsync(
        BootstrapModificationRequest request,
        CancellationToken ct = default);
}
