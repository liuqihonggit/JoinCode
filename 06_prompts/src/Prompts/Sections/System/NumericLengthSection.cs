
namespace Core.Prompts.Sections;

/// <summary>
/// 数字长度限制部分 - 具体的字数限制
/// </summary>
[PromptSection(Name = "numeric_length", Order = 29)]
public static class NumericLengthSection
{
    public static string? GetContent()
    {
        var enableNumericLength = PromptConfigSnapshot.Current.EnableNumericLength;
        if (!enableNumericLength)
        {
            return null;
        }

        return """
# 长度限制

长度限制：工具调用之间的文本保持在25个词以内。最终回复保持在100个词以内，除非任务需要更多细节。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("numeric_length", GetContent);
}
