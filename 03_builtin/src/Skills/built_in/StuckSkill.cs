
namespace Core.Skills.BuiltIn;

public sealed class StuckSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "stuck",
            Description = "当AI卡住时提供恢复策略",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["context"] = new() { Type = "string", Description = "当前卡住的上下文描述", Required = false },
                ["maxRetries"] = new() { Type = "integer", Description = "最大重试次数", Required = false, DefaultValue = 3, Validation = new ParameterValidation { Min = 1, Max = 10 } }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "analyze_state", Type = SkillStepType.Prompt, Description = "分析当前状态", Prompt = "分析当前卡住的状态：\n\n上下文：{{context}}\n\n请识别：\n1. 当前正在尝试做什么\n2. 卡在哪个步骤\n3. 已尝试过的方法\n4. 可能的阻碍因素", Next = "identify_cause" },
                new() { Id = "identify_cause", Type = SkillStepType.Prompt, Description = "识别卡住原因", Prompt = "根据分析结果，识别卡住的根本原因：\n\n1. 信息不足 - 缺少关键上下文\n2. 方法不当 - 当前方法不适合此问题\n3. 依赖阻塞 - 等待外部条件\n4. 认知偏差 - 对问题理解有误\n5. 工具限制 - 当前工具无法完成\n\n给出最可能的原因和置信度。", Next = "provide_alternatives" },
                new() { Id = "provide_alternatives", Type = SkillStepType.Prompt, Description = "提供替代方案", Prompt = "针对卡住的原因，提供替代方案：\n\n1. 换一个角度思考问题\n2. 分解为更小的子问题\n3. 使用不同的工具或方法\n4. 回退到上一个已知正确状态\n5. 请求用户提供更多信息\n\n每个方案给出具体步骤和预期效果。", Next = "retry" },
                new() { Id = "retry", Type = SkillStepType.Loop, Description = "尝试恢复", Loop = new LoopConfig { Variable = "attempt", Count = 3, MaxIterations = 10 }, Prompt = "第 {{attempt}} 次尝试恢复，使用替代方案重新执行任务", Next = "check_result" },
                new() { Id = "check_result", Type = SkillStepType.Prompt, Description = "检查恢复结果", Prompt = "检查恢复尝试的结果：\n1. 是否成功突破卡点\n2. 是否需要更多尝试\n3. 是否需要用户介入\n\n给出最终状态和建议。" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 180,
            Tags = new List<string> { "recovery", "stuck", "fallback" }.AsReadOnly(),
            Permissions = new List<string>().AsReadOnly()
        };
    }
}
