
namespace Core.Prompts.Sections;

/// <summary>
/// 数字长度限制部分 - 具体的字数限制
/// </summary>
[PromptSection(Name = "numeric_length", Order = 29)]
public static class NumericLengthSection {
    /// <summary>
    /// 获取数字长度限制部分的内容。
    /// </summary>
    /// <returns>长度限制提示词文本；未启用时返回 <c>null</c>。</returns>
    public static string? GetContent() {
        var enableNumericLength = PromptConfigSnapshot.Current.EnableNumericLength;
        if (!enableNumericLength) {
            return null;
        }

        return """
# 长度限制

长度限制：工具调用之间的文本保持在25个词以内。最终回复保持在100个词以内，除非任务需要更多细节。
""";
    }

    /// <summary>
    /// 创建数字长度限制提示词部分。
    /// </summary>
    /// <returns>缓存系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("numeric_length", GetContent);
}