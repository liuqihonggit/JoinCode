
namespace Core.Skills.BuiltIn;

public sealed class DebugSkill
{
    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "debug",
            Description = "帮助调试代码问题",
            Version = "2.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["error"] = new() { Type = "string", Description = "错误信息或异常描述", Required = true },
                ["file"] = new() { Type = "string", Description = "出问题的文件路径", Required = false },
                ["context"] = new() { Type = "string", Description = "额外的上下文信息（如操作步骤、环境信息）", Required = false }
            },
            Steps = new List<SkillStep>
            {
                new() { Id = "read_error", Type = SkillStepType.Prompt, Description = "读取并解析错误信息", Prompt = "分析以下错误信息，提取关键信息：\n\n错误：{{error}}\n{{context}}\n\n请识别：\n1. 错误类型（编译/运行时/逻辑）\n2. 涉及的文件和行号\n3. 错误的根本原因假设", Next = "locate_file" },
                new() { Id = "locate_file", Type = SkillStepType.Condition, Condition = "{{file}}", Description = "检查是否指定了文件", Next = "read_file", OnError = "search_file" },
                new() { Id = "read_file", Type = SkillStepType.Tool, Tool = FileToolNameConstants.FileRead, Description = "读取问题文件", Prompt = "读取 {{file}} 的内容", Next = "analyze" },
                new() { Id = "search_file", Type = SkillStepType.Tool, Tool = "search", Description = "搜索相关文件", Prompt = "根据错误信息搜索相关代码文件", Next = "analyze" },
                new() { Id = "analyze", Type = SkillStepType.Prompt, Description = "分析根本原因", Prompt = "基于错误信息和代码内容，分析根本原因：\n\n错误：{{error}}\n\n请从以下角度分析：\n1. 代码逻辑错误\n2. 类型不匹配\n3. 空引用/资源未找到\n4. 并发问题\n5. 配置问题\n6. 依赖版本冲突\n\n给出最可能的原因和证据。", Next = "suggest_fix" },
                new() { Id = "suggest_fix", Type = SkillStepType.Prompt, Description = "建议修复方案", Prompt = "根据分析结果，提供修复方案：\n\n1. 修复步骤（按优先级排列）\n2. 每步的代码变更\n3. 验证修复的方法\n4. 可能的副作用\n\n注意：修复应最小化变更范围，避免引入新问题。" }
            },
            RequiresConfirmation = false,
            TimeoutSeconds = 300,
            Tags = new List<string> { "debug", "troubleshoot", "fix" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "search.code" }.AsReadOnly()
        };
    }
}
