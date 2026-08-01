namespace Core.Prompts.Utils;

/// <summary>
/// 动态关键词词表配置模型 — 从 ~/.jcc/keyword-sections.json 加载
/// </summary>
public sealed record DynamicKeywordConfig
{
    /// <summary>
    /// 关键词 Section 配置字典 — Key 为 Section 名称，Value 为关键词列表
    /// </summary>
    public Dictionary<string, DynamicKeywordSection> Sections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 单个关键词 Section 配置
/// </summary>
public sealed record DynamicKeywordSection
{
    /// <summary>
    /// 触发关键词列表（最小词根，如 "睡觉" 而非 "我去睡觉了"）
    /// </summary>
    public List<string> Keywords { get; init; } = [];

    /// <summary>
    /// 是否启用，默认 true
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 自定义注入内容（可选）— 为空时使用内置 Section 内容
    /// </summary>
    public string? CustomContent { get; init; }
}
