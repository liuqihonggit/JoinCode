namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 提示词模板分类
/// </summary>
public enum PromptTemplateCategory
{
    Memory,
    Agent,
    System,
    Dream,
    Mcp,
    Skill,
    Plan,
    Goal
}

/// <summary>
/// 提示词模板特性 — 标记在 static class 上，声明该类提供独立 LLM 调用的 system prompt 模板
/// 源码生成器扫描此特性，自动生成 PromptTemplateRegistration 注册代码
/// 与 [PromptSection] 的区别：PromptSection 是系统提示词的分区组件，PromptTemplate 是独立 LLM 调用的完整提示词
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PromptTemplateAttribute : Attribute
{
    /// <summary>
    /// 模板名称（唯一标识）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 模板分类
    /// </summary>
    public PromptTemplateCategory Category { get; init; }

    /// <summary>
    /// 模板描述
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// 内容成员名称 — 指定 static class 中返回提示词内容的字段或方法名
    /// 留空则自动检测：优先 GetContent() → GetPrompt() → Prompt 字段 → SystemPrompt 字段
    /// </summary>
    public string ContentMember { get; init; } = "";

    /// <summary>
    /// 是否需要运行时参数 — 为 true 时，生成器只注册元数据，不生成 GetContent 查找
    /// 消费者通过 GetAllTemplates() 查找模板名称，然后直接调用模板类的构建方法
    /// </summary>
    public bool HasParameters { get; init; }
}
