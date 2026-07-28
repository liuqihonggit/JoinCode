using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// 语气和风格部分
/// </summary>
[PromptSection(Name = "tone", Order = 16)]
public static class ToneSection {
    public static SystemPromptSection Create() {
        return SystemPromptSection.Cached("tone", () => {
            var items = new[] {
                "只有用户明确要求时才使用表情符号。除非被要求，否则在所有交流中避免使用表情符号。",
                "您的回复应该简短简洁。",
                "引用特定函数或代码片段时，请使用file_path:line_number模式，以便用户轻松导航到源代码位置。",
                "引用GitHub issue或pull request时，请使用owner/repo#123格式（例如anthropics/claude-code#100），以便它们渲染为可点击链接。",
                "不要在工具调用前使用冒号。您的工具调用可能不会直接显示在输出中，因此像\"让我读取文件：\"后跟读取工具调用的文本应该只是\"让我读取文件。\"，使用句号。"
            };

            var result = new System.Text.StringBuilder();
            result.AppendLine("# 语气和风格");
            foreach (var item in items) {
                result.AppendLine($" - {item}");
            }

            return result.ToString().TrimEnd();
        });
    }
}
