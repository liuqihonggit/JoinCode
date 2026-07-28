
namespace Core.Skills.BuiltIn;

public sealed class SimplifySkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "simplify",
            Description = "简化复杂代码",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["target"] = new() { Type = "string", Description = "要简化的目标（文件路径、函数名或代码片段）", Required = true },
                ["strategy"] = new() { Type = "string", Description = "简化策略", Required = false, DefaultValue = "balanced", Validation = new ParameterValidation { EnumValues = new List<string> { "conservative", "balanced", "aggressive" }.AsReadOnly() } }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "read_target", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileRead, Description = "读取目标代码", Prompt = "读取 {{target}} 的内容", Next = "analyze_complexity" },
                new() { Id = "analyze_complexity", Type = SkillStepType.Prompt, Description = "分析代码复杂度", Prompt = "分析以下代码的复杂度：\n\n{{read_target_result}}\n\n请评估：\n1. 圈复杂度（分支/循环数量）\n2. 认知复杂度（嵌套深度、变量数量）\n3. 代码行数\n4. 重复代码比例\n5. 依赖数量\n\n给出复杂度评分（1-10）和具体问题列表。", Next = "identify_simplifications" },
                new() { Id = "identify_simplifications", Type = SkillStepType.Prompt, Description = "识别简化点", Prompt = "根据复杂度分析，识别可简化的点：\n\n策略：{{strategy}}\n- conservative：仅简化明显冗余，保持结构不变\n- balanced：适度重构，改善可读性\n- aggressive：大幅简化，可能改变内部结构\n\n请列出：\n1. 可简化的代码段\n2. 每段的简化方法\n3. 预期的复杂度降低\n4. 风险评估", Next = "refactor" },
                new() { Id = "refactor", Type = SkillStepType.Prompt, Description = "执行重构", Prompt = "执行代码简化重构：\n\n目标：{{target}}\n策略：{{strategy}}\n\n重构原则：\n1. 保持公共API不变\n2. 不改变外部行为\n3. 每次只做一个简化\n4. 优先处理高收益低风险的简化点", Next = "verify_equivalence" },
                new() { Id = "verify_equivalence", Type = SkillStepType.Prompt, Description = "验证等价性", Prompt = "验证简化后的代码与原始代码行为等价：\n\n1. 检查公共API是否不变\n2. 检查边界条件处理是否一致\n3. 检查异常处理是否完整\n4. 建议运行测试验证\n\n给出等价性评估：完全等价/部分等价/不等价" }
            },
            RequiresConfirmation = true,
            TimeoutSeconds = 300,
            Tags = new List<string> { "simplify", "refactor", "complexity" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "file.write" }.AsReadOnly()
        };
    }
}
