
namespace Core.Skills.BuiltIn;

public sealed class HunterSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "hunter",
            Description = "深度搜索和追踪代码模式",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["target"] = new() { Type = "string", Description = "搜索目标（类名、函数名、模式、字符串等）", Required = true },
                ["scope"] = new() { Type = "string", Description = "搜索范围（如目录路径、文件类型过滤）", Required = false },
                ["depth"] = new() { Type = "integer", Description = "搜索深度（1=仅定义，2=含引用，3=含关联）", Required = false, DefaultValue = 2, Validation = new ParameterValidation { Min = 1, Max = 5 } }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "define_search", Type = SkillStepType.Prompt, Description = "定义搜索策略", Prompt = "为以下目标制定搜索策略：\n\n目标：{{target}}\n范围：{{scope}}\n深度：{{depth}}\n\n请制定：\n1. 搜索关键词列表\n2. 搜索维度（定义、引用、实现、测试）\n3. 搜索顺序（从精确到模糊）", Next = "search_definitions" },
                new() { Id = "search_definitions", Type = SkillStepType.Tool, Tool = "search", Description = "搜索定义位置", Prompt = "搜索 {{target}} 的定义位置，范围：{{scope}}", Next = "search_references" },
                new() { Id = "search_references", Type = SkillStepType.Tool, Tool = "search", Description = "搜索引用位置", Prompt = "搜索 {{target}} 的所有引用位置", Next = "check_depth" },
                new() { Id = "check_depth", Type = SkillStepType.Condition, Condition = "{{depth}} >= 3", Description = "检查是否需要深度关联分析", Next = "deep_analysis", OnError = "generate_report" },
                new() { Id = "deep_analysis", Type = SkillStepType.Prompt, Description = "深度关联分析", Prompt = "对 {{target}} 进行深度关联分析：\n1. 调用链追踪（谁调用了它，它调用了谁）\n2. 依赖关系图\n3. 影响范围评估\n4. 相关测试覆盖情况", Next = "generate_report" },
                new() { Id = "generate_report", Type = SkillStepType.Prompt, Description = "生成搜索报告", Prompt = "生成搜索报告：\n\n目标：{{target}}\n范围：{{scope}}\n深度：{{depth}}\n\n1. 定义位置（文件:行号）\n2. 引用位置列表\n3. 关联分析（如适用）\n4. 代码模式总结\n5. 建议的后续操作" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 300,
            Tags = new List<string> { "search", "trace", "analyze" }.AsReadOnly(),
            Permissions = new List<string> { "search.code", "file.read" }.AsReadOnly()
        };
    }
}
