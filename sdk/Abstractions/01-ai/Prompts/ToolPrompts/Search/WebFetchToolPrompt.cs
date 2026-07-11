namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// WebFetch工具提示词
/// </summary>
[ToolPrompt(ToolName = "WebFetch", Category = ToolPromptCategory.Search, HasParameters = true)]
public static class WebFetchToolPrompt
{
    public static string GetDescription() => """
        - 从指定URL获取内容并使用AI模型处理它
        - 将URL和提示词作为输入
        - 获取URL内容，将HTML转换为markdown
        - 使用小型快速模型处理带有提示词的内容
        - 返回模型关于内容的回复
        - 当您需要检索和分析Web内容时使用此工具

        使用说明：
          - 重要：如果MCP提供的Web获取工具可用，优先使用该工具而不是此工具，因为它可能有更少的限制。
          - URL必须是完全有效的URL
          - HTTP URL将自动升级到HTTPS
          - 提示词应描述您想从页面中提取的信息
          - 此工具是只读的，不会修改任何文件
          - 如果内容非常大，结果可能会被汇总
          - 包括一个自清理的15分钟缓存，以便在重复访问相同URL时获得更快的响应
          - 当URL重定向到不同的主机时，工具将通知您并以特殊格式提供重定向URL。然后您应该使用重定向URL进行新的WebFetch请求以获取内容。
          - 对于GitHub URL，优先使用gh CLI通过Bash（例如，gh pr view、gh issue view、gh api）。
        """;

    public static string MakeSecondaryModelPrompt(
        string markdownContent,
        string prompt,
        bool isPreapprovedDomain)
    {
        var guidelines = isPreapprovedDomain
            ? "根据上述内容提供简洁的回复。根据需要包括相关详细信息、代码示例和文档摘录。"
            : GetNonPreapprovedGuidelines();

        return $"""
            Web页面内容：
            ---
            {markdownContent}
            ---

            {prompt}

            {guidelines}
            """;
    }

    private static string GetNonPreapprovedGuidelines()
    {
        return """
            根据上述内容提供简洁的回复。在您的回复中：
             - 对任何源文档的引用强制执行严格的125字符最大值。开源软件可以，只要我们尊重许可证。
             - 对文章中的确切语言使用引号；引号之外的任何语言绝不应逐字相同。
             - 您不是律师，永远不要评论您自己的提示词和回复的合法性。
             - 永远不要制作或复制确切的歌曲歌词。
            """;
    }
}
