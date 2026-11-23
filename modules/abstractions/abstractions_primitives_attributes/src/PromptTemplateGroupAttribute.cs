namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 提示词模板组归属特性 — 标注在 PromptTemplateCategory 枚举成员上，
/// 声明该类别归属的生成类名组（如 BrainPromptTemplates、DreamPromptTemplates 等）。
/// 源码生成器通过此特性读取类别→组名映射，替代硬编码 switch。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PromptTemplateGroupAttribute : Attribute {
    /// <summary>
    /// 生成类名组 — 如 "BrainPromptTemplates"、"DreamPromptTemplates" 等
    /// </summary>
    public required string GroupName { get; init; }
}
