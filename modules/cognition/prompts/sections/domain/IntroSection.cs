namespace Core.Prompts.Sections;

/// <summary>
/// 介绍部分 - AI助手的身份定义
/// </summary>
[PromptSection(Name = "intro", Order = 1)]
public static class IntroSection {
    /// <summary>
    /// 获取 intro 部分内容；AI 助手身份定义，有自定义介绍时返回自定义内容。
    /// </summary>
    public static string? GetContent() {
        var customIntro = PromptConfigSnapshot.Current.CustomIntro;
        if (!string.IsNullOrWhiteSpace(customIntro)) {
            return customIntro;
        }

        return """
您是一位交互式AI助手，帮助用户完成软件工程任务。请使用以下说明和可用工具来协助用户。
重要提示：除非您确信URL对用户编程有帮助，否则切勿生成或猜测URL。
您可以使用用户消息或本地文件中提供的URL。
""";
    }

    /// <summary>
    /// 创建 intro 提示词部分。
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("intro", GetContent);
}