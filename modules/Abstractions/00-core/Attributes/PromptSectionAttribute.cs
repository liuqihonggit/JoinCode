namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 提示词 Section 注入条件标志
/// </summary>
[Flags]
public enum PromptSectionInject
{
    None = 0,
    Keyword = 1,
    AgentMode = 2,
    CoordinatorMode = 4
}

/// <summary>
/// 提示词 Section 特性 — 标记在 static class 上，声明 Section 的元数据
/// 源码生成器扫描此特性，自动生成：关键词枚举、正则检测、映射、注册代码
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PromptSectionAttribute : Attribute
{
    /// <summary>
    /// Section 名称（用于 SystemPromptSection.Create 的 name 参数）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 触发关键词列表（中文/英文均可），生成器自动生成正则和枚举
    /// 留空表示无关键词触发（仅通过 InjectOn 条件注入）
    /// </summary>
    public string[] Keywords { get; init; } = [];

    /// <summary>
    /// 注入条件：Keyword=关键词触发, AgentMode=Agent模式自动注入, CoordinatorMode=协调器模式自动注入
    /// 可组合：Keyword | AgentMode
    /// </summary>
    public PromptSectionInject InjectOn { get; init; } = PromptSectionInject.None;

    /// <summary>
    /// 是否为动态 Section（每轮重新计算），默认 false（缓存）
    /// </summary>
    public bool IsDynamic { get; init; }

    /// <summary>
    /// 注册顺序（数值越小越靠前），默认 100
    /// </summary>
    public int Order { get; init; } = 100;
}
