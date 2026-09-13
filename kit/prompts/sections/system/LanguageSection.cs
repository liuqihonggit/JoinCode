
namespace Core.Prompts.Sections;

/// <summary>
/// 语言部分 - 指定回复语言
/// </summary>
[PromptSection(Name = "language", Order = 66, IsDynamic = true)]
public static class LanguageSection {
    /// <summary>
    /// 获取语言偏好部分的内容。
    /// </summary>
    /// <returns>语言偏好提示词文本；未设置偏好时返回 <c>null</c>。</returns>
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

    /// <summary>
    /// 创建语言偏好提示词部分。
    /// </summary>
    /// <returns>动态系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("language", GetContent);
}
