
namespace Core.Skills.BuiltIn;

public sealed class RememberSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "remember",
            Description = "保存重要信息到记忆",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["content"] = new() { Type = "string", Description = "要记住的内容", Required = true },
                ["category"] = new() { Type = "string", Description = "分类标签（如：避坑指南、成功经验、架构决策）", Required = false, Validation = new ParameterValidation { EnumValues = new List<string> { "避坑指南", "成功经验", "架构决策", "性能优化", "安全规范", "其他" }.AsReadOnly() } },
                ["importance"] = new() { Type = "string", Description = "重要程度", Required = false, DefaultValue = "medium", Validation = new ParameterValidation { EnumValues = new List<string> { "low", "medium", "high", "critical" }.AsReadOnly() } }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "extract_info", Type = SkillStepType.Prompt, Description = "提取关键信息", Prompt = "从以下内容中提取关键信息：\n\n{{content}}\n\n请提取：\n1. 核心知识点\n2. 适用的技术栈/场景\n3. 关键约束或前提条件\n4. 可能的例外情况", Next = "categorize" },
                new() { Id = "categorize", Type = SkillStepType.Prompt, Description = "分类标记", Prompt = "对提取的信息进行分类标记：\n\n分类：{{category}}\n重要程度：{{importance}}\n\n请确定：\n1. 主要分类（如果未指定）\n2. 子分类\n3. 关联的已有记忆\n4. 搜索关键词", Next = "persist" },
                new() { Id = "persist", Type = SkillStepType.Tool, Tool = "memory_save", Description = "持久化存储", Prompt = "保存记忆：\n内容：{{content}}\n分类：{{category}}\n重要程度：{{importance}}", Next = "confirm" },
                new() { Id = "confirm", Type = SkillStepType.Prompt, Description = "确认保存", Prompt = "确认记忆已保存：\n\n1. 保存的内容摘要\n2. 分类和标签\n3. 重要程度\n4. 保存时间\n5. 关联记忆数量\n\n如保存失败，请说明原因并建议替代方案。" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 60,
            Tags = new List<string> { "memory", "remember", "persist" }.AsReadOnly(),
            Permissions = new List<string> { "memory.write" }.AsReadOnly()
        };
    }
}
