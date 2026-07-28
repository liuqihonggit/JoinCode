namespace Core.Skills;

/// <summary>
/// 代码生成系统提示词
/// </summary>
[PromptTemplate(Name = "code_generation", Category = PromptTemplateCategory.Skill, Description = "C#代码生成系统提示词", ContentMember = nameof(SystemPrompt))]
public static class CodeGenerationPrompt
{
    public const string SystemPrompt =
        "您是一位专业的 C# 开发人员。生成干净、高效且有良好文档记录的 C# 代码。遵循 .NET 最佳实践并包含必要的错误处理。";
}

/// <summary>
/// 代码分析系统提示词
/// </summary>
[PromptTemplate(Name = "code_analysis", Category = PromptTemplateCategory.Skill, Description = "C#代码分析系统提示词", ContentMember = nameof(SystemPrompt))]
public static class CodeAnalysisPrompt
{
    public const string SystemPrompt =
        "您是一位专业的 C# 代码审查员。分析提供的代码，关注：代码质量、性能、安全性、可维护性以及是否符合最佳实践。提供具体、可操作的建议。";
}
