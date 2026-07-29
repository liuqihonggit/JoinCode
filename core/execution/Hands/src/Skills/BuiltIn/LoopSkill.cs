
namespace Core.Skills.BuiltIn;

public sealed class LoopSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "loop",
            Description = "循环调度执行任务",
            Version = "1.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["task"] = new() { Type = "string", Description = "要循环执行的任务描述", Required = true },
                ["condition"] = new() { Type = "string", Description = "终止条件（如：测试通过、错误数为0）", Required = false },
                ["maxIterations"] = new() { Type = "integer", Description = "最大迭代次数", Required = false, DefaultValue = 10, Validation = new ParameterValidation { Min = 1, Max = 100 } }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "define_task", Type = SkillStepType.Prompt, Description = "定义循环任务", Prompt = "定义循环任务：\n\n任务：{{task}}\n终止条件：{{condition}}\n最大迭代次数：{{maxIterations}}\n\n请明确：\n1. 每次迭代的具体步骤\n2. 成功/失败的判定标准\n3. 迭代间是否需要调整策略", Next = "execute_loop" },
                new() { Id = "execute_loop", Type = SkillStepType.Loop, Description = "执行循环", Loop = new LoopConfig { Variable = "iteration", Count = 10, MaxIterations = 100, Condition = "{{condition}}" }, Prompt = "第 {{iteration}} 次迭代执行任务：{{task}}\n\n检查终止条件：{{condition}}\n如果条件满足则停止循环。", Next = "check_termination" },
                new() { Id = "check_termination", Type = SkillStepType.Prompt, Description = "检查终止条件", Prompt = "检查循环执行结果：\n1. 终止条件是否已满足\n2. 是否达到最大迭代次数\n3. 是否需要继续执行\n4. 当前进展总结", Next = "summarize" },
                new() { Id = "summarize", Type = SkillStepType.Prompt, Description = "生成循环执行总结", Prompt = "生成循环执行总结：\n1. 任务：{{task}}\n2. 总迭代次数\n3. 每次迭代的关键结果\n4. 最终状态\n5. 终止原因\n6. 建议" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 600,
            Tags = new List<string> { "loop", "iterate", "repeat" }.AsReadOnly(),
            Permissions = new List<string>().AsReadOnly()
        };
    }
}
