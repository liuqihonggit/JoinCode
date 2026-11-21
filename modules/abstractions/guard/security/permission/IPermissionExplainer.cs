namespace JoinCode.Abstractions.Security.Permission;

/// <summary>
/// 权限解释器 — 权限确认时向模型做一次**旁路**查询，解释「这条命令做什么、我为什么要跑、风险多大」
/// （对齐基准 <c>utils/permissions/permissionExplainer.ts</c> 的 <c>generatePermissionExplanation</c>）。
/// <para><b>旁路语义</b>：不进会话历史、不占主循环的轮次，失败/关闭一律返回 <c>null</c>
/// （基准同样 <c>catch(() =&gt; null)</c>）——解释是锦上添花，绝不能因为它挂掉而卡住确认。</para>
/// <para><b>懒加载</b>：调用方只在用户真正按下 ctrl+e 时才调用 <see cref="ExplainAsync"/>，
/// 对齐基准注释「避免为永不查看的解释消耗 token」。</para>
/// </summary>
public interface IPermissionExplainer {
    /// <summary>是否启用 — 对应 <c>permissionExplainerEnabled</c>（默认 true）。
    /// 关闭时调用方不应显示解释入口（对齐基准同时门控键位 isActive 与请求）。</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 生成解释 — 一次性的旁路模型调用；模型输出不可解析、超时、取消、未配置模型一律返回 <c>null</c>。
    /// </summary>
    /// <param name="request">待解释的工具调用。</param>
    /// <param name="cancellationToken">取消令牌（用户收起了对话框/退出会话时取消）。</param>
    /// <returns>解释结果，或 <c>null</c>（调用方渲染「解释不可用」）。</returns>
    Task<PermissionExplanation?> ExplainAsync(PermissionExplainRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 解释请求 — 对齐基准 <c>GenerateExplanationParams</c> 的前三项。
/// <para><b>与基准的差异（如实记录）</b>：基准还传 <c>messages</c> 供解释器抽取最近 3 条 assistant 消息；
/// 本仓库的解释器在引擎侧运行，自己就能读到会话消息，故不由此处传入 —— 少一层跨层搬运。</para>
/// </summary>
/// <param name="ToolName">工具名（如 <c>Bash</c>）。</param>
/// <param name="ToolInput">工具输入的可读形态。本仓库确认请求只带文本描述（无结构化 JSON 参数），
/// 因此这里传确认提示里那段可读文本，解释器提示词中如实说明。</param>
/// <param name="ToolDescription">工具描述（可选）。</param>
public sealed record PermissionExplainRequest(string ToolName, string ToolInput, string? ToolDescription = null);

/// <summary>风险等级 — 对齐基准 <c>RiskLevel</c> 三档（取值即基准字符串，供模型 JSON 反查）。</summary>
public enum PermissionRiskLevel {
    /// <summary>低风险（安全的开发操作）。</summary>
    [EnumValue("LOW")]
    Low,

    /// <summary>中风险（可恢复的改动）。</summary>
    [EnumValue("MEDIUM")]
    Medium,

    /// <summary>高风险（危险或不可撤回）。</summary>
    [EnumValue("HIGH")]
    High,
}

/// <summary>
/// 解释结果 — 对齐基准 <c>PermissionExplanation</c> 四字段（少一个都不完整：
/// 只有「做什么」没有「为什么」用户无法判断是否是模型在乱跑，只有风险没有等级无法快速扫读）。
/// </summary>
public sealed record PermissionExplanation {
    /// <summary>风险等级（渲染为三色：低=成功色 / 中=警告色 / 高=错误色）。</summary>
    public required PermissionRiskLevel RiskLevel { get; init; }

    /// <summary>这条命令做什么（1-2 句）。</summary>
    public required string Explanation { get; init; }

    /// <summary>我为什么要跑它（基准要求以 "I" 开头，本仓库为中文界面，取同义表述）。</summary>
    public required string Reasoning { get; init; }

    /// <summary>可能出什么问题（一句话，基准限制 15 词内）。</summary>
    public required string Risk { get; init; }
}
