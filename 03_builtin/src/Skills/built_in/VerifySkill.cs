
namespace Core.Skills.BuiltIn;

public sealed class VerifySkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "verify",
            Description = "验证代码或结果的正确性",
            Version = "2.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["target"] = new() { Type = "string", Description = "要验证的目标（文件路径、函数名或模块）", Required = true },
                ["criteria"] = new() { Type = "string", Description = "验证标准（如：编译通过、测试通过、性能达标）", Required = true },
                ["autoFix"] = new() { Type = "boolean", Description = "是否自动修复发现的问题", Required = false, DefaultValue = false }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "read_target", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileRead, Description = "读取目标内容", Prompt = "读取 {{target}} 的内容", Next = "check_criteria" },
                new() { Id = "check_criteria", Type = SkillStepType.Prompt, Description = "根据标准检查目标", Prompt = "根据以下标准验证目标内容：\n\n目标：{{target}}\n标准：{{criteria}}\n\n请逐条检查每个标准，给出通过/不通过的判定和详细说明。", Next = "run_tests" },
                new() { Id = "run_tests", Type = SkillStepType.Tool, Tool = "shell", Description = "运行相关测试", Prompt = "运行与 {{target}} 相关的测试，验证功能正确性", Next = "check_auto_fix" },
                new() { Id = "check_auto_fix", Type = SkillStepType.Condition, Condition = "{{autoFix}}", Description = "检查是否需要自动修复", Next = "auto_fix", OnError = "generate_report" },
                new() { Id = "auto_fix", Type = SkillStepType.Prompt, Description = "自动修复发现的问题", Prompt = "根据验证结果，自动修复 {{target}} 中发现的问题。修复时遵循以下原则：\n1. 最小化修改范围\n2. 保持代码风格一致\n3. 不改变公共API", Next = "re_verify" },
                new() { Id = "re_verify", Type = SkillStepType.Prompt, Description = "修复后重新验证", Prompt = "重新验证修复后的 {{target}}，确认问题已解决且未引入新问题", Next = "generate_report" },
                new() { Id = "generate_report", Type = SkillStepType.Prompt, Description = "生成验证报告", Prompt = "生成验证报告，包含：\n1. 验证目标：{{target}}\n2. 验证标准：{{criteria}}\n3. 每条标准的通过状态\n4. 发现的问题列表\n5. 修复操作（如有）\n6. 最终结论：通过/不通过" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 300,
            Tags = new List<string> { "verify", "validation", "quality" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "shell.execute" }.AsReadOnly()
        };
    }
}
