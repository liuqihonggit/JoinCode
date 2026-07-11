
namespace Core.Prompts.Sections;

/// <summary>
/// Scratchpad部分 - 临时文件目录说明
/// </summary>
[PromptSection(Name = "scratchpad", Order = 71, IsDynamic = true)]
public static class ScratchpadSection {
    public static string? GetContent() {
        var scratchpadPath = PromptConfigSnapshot.Current.ScratchpadPath;
        if (string.IsNullOrWhiteSpace(scratchpadPath)) {
            return null;
        }

        return $"""
# Scratchpad目录

您有一个临时工作区用于写入文件：{scratchpadPath}

此目录在会话之间是持久的，但可能在会话之间被清除。它可用于：
- 写入中间结果
- 保存大型输出以供后续分析
- 创建临时脚本或配置文件

使用此目录来组织您的工作，但不要依赖它在会话之间持久存在。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("scratchpad", GetContent);
}
