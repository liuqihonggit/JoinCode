
namespace Core.Prompts.Sections;

/// <summary>
/// 语言部分 - 指定回复语言
/// </summary>
[PromptSection(Name = "language", Order = 66, IsDynamic = true)]
public static class LanguageSection {
    public static string? GetContent() {
        var languagePreference = PromptConfigSnapshot.Current.LanguagePreference;
        if (string.IsNullOrWhiteSpace(languagePreference)) {
            return null;
        }

        return $"""
# 语言

始终使用{languagePreference}回复。
对所有解释、注释和与用户的交流使用{languagePreference}。
技术术语和代码标识符应保持其原始形式。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("language", GetContent);
}
