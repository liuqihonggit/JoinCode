
namespace Core.Skills.BuiltIn;

public sealed class SkillifySkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "skillify",
            Description = "将重复操作转化为可复用技能",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["name"] = new() { Type = "string", Description = "技能名称", Required = true, Validation = new ParameterValidation { Pattern = @"^[a-z][a-z0-9_]*$", MinLength = 3, MaxLength = 50 } },
                ["description"] = new() { Type = "string", Description = "技能描述", Required = false },
                ["fromHistory"] = new() { Type = "boolean", Description = "是否从操作历史中提取", Required = false, DefaultValue = false }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "observe_pattern", Type = SkillStepType.Prompt, Description = "观察操作模式", Prompt = "观察操作模式并提取技能定义：\n\n技能名称：{{name}}\n描述：{{description}}\n从历史提取：{{fromHistory}}\n\n请分析：\n1. 重复出现的操作步骤\n2. 每步的输入和输出\n3. 步骤间的依赖关系\n4. 可变参数和固定参数", Next = "extract_steps" },
                new() { Id = "extract_steps", Type = SkillStepType.Prompt, Description = "提取步骤定义", Prompt = "提取技能步骤定义：\n\n技能名：{{name}}\n\n请生成：\n1. 步骤列表（id、type、description）\n2. 每步的工具调用或提示词\n3. 步骤间的流转关系\n4. 错误处理策略\n5. 参数定义（名称、类型、是否必需、默认值）", Next = "generate_definition" },
                new() { Id = "generate_definition", Type = SkillStepType.Prompt, Description = "生成技能定义", Prompt = "生成完整的技能定义（JSON格式）：\n\n技能名：{{name}}\n描述：{{description}}\n\n生成的定义必须包含：\n1. name\n2. description\n3. version\n4. parameters（含类型和验证规则）\n5. steps（含id、type、tool/prompt、next、onError）\n6. requiresConfirmation\n7. timeoutSeconds\n8. tags\n9. permissions", Next = "register" },
                new() { Id = "register", Type = SkillStepType.Tool, Tool = "skill_register", Description = "注册技能", Prompt = "注册技能 {{name}}", Next = "confirm" },
                new() { Id = "confirm", Type = SkillStepType.Prompt, Description = "确认注册结果", Prompt = "确认技能注册结果：\n\n1. 技能名称：{{name}}\n2. 技能描述\n3. 参数列表\n4. 步骤数量\n5. 注册状态\n6. 是否可立即使用\n\n如注册失败，请说明原因。" }
            },
            RequiresConfirmation = true,
            TimeoutSeconds = 120,
            Tags = new List<string> { "skillify", "create", "meta" }.AsReadOnly(),
            Permissions = new List<string> { "skill.register" }.AsReadOnly()
        };
    }
}
