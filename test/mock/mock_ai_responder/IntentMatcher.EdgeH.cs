namespace MockAiResponder;

/// <summary>H1-H20: 纯内容流式边缘场景 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>H1-H20: 纯内容流式边缘场景匹配</summary>
    private static (InjectDto Response, string Description)? MatchEdgeHIntent(string userMessage, string protocol, bool isStream) {
        var lower = userMessage.ToLowerInvariant();

        string? content = null;
        string? desc = null;

        if (lower.Contains("h1\n") || lower.Contains("single line text")) { content = "h1 single line response"; desc = "H1: 纯文本 1 行"; }
        else if (lower.Contains("h2\n") || lower.Contains("100 lines text")) { content = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"h2 line {i}")); desc = "H2: 纯文本 100 行"; }
        else if (lower.Contains("h3\n") || lower.Contains("code block text")) { content = "h3 before\n```python\nprint('hello')\n```\nh3 after"; desc = "H3: 纯文本含代码块"; }
        else if (lower.Contains("h4\n") || lower.Contains("table text")) { content = "| col1 | col2 |\n|------|------|\n| a | b |\n| c | d |"; desc = "H4: 纯文本含 markdown 表格"; }
        else if (lower.Contains("h5\n") || lower.Contains("link text")) { content = "h5 [link](https://example.com) text"; desc = "H5: 纯文本含 markdown 链接"; }
        else if (lower.Contains("h6\n") || lower.Contains("emoji text")) { content = "h6 🔧🚀⚡ emoji test"; desc = "H6: 纯文本含 emoji"; }
        else if (lower.Contains("h7\n") || lower.Contains("4byte emoji text")) { content = "h7 𝕏𝕐𝕏 4byte emoji"; desc = "H7: 纯文本含 4 字节 emoji"; }
        else if (lower.Contains("h8\n") || lower.Contains("chinese text")) { content = "h8 你好世界 中文测试"; desc = "H8: 纯文本含中文"; }
        else if (lower.Contains("h9\n") || lower.Contains("japanese text")) { content = "h9 こんにちは世界 日本語テスト"; desc = "H9: 纯文本含日文"; }
        else if (lower.Contains("h10\n") || lower.Contains("korean text")) { content = "h10 안녕하세요 한국어 테스트"; desc = "H10: 纯文本含韩文"; }
        else if (lower.Contains("h11\n") || lower.Contains("cyrillic text")) { content = "h11 Привет мир Кириллица"; desc = "H11: 纯文本含西里尔文"; }
        else if (lower.Contains("h12\n") || lower.Contains("rtl text content")) { content = "h12 مرحبا بالعالم RTL"; desc = "H12: 纯文本含 RTL 文本"; }
        else if (lower.Contains("h13\n") || lower.Contains("mixed script text")) { content = "h13 Hello 世界 こんにちは 안녕 Привет مرحبا"; desc = "H13: 纯文本含混合脚本"; }
        else if (lower.Contains("h14\n") || lower.Contains("100kb text")) { content = new string('x', 102400); desc = "H14: 纯文本 100KB"; }
        else if (lower.Contains("h15\n") || lower.Contains("json in text")) { content = """h15 {"key":"value","num":42,"arr":[1,2,3]} json"""; desc = "H15: 纯文本含 JSON"; }
        else if (lower.Contains("h16\n") || lower.Contains("xml in text")) { content = "h16 <root><child>value</child></root> xml"; desc = "H16: 纯文本含 XML"; }
        else if (lower.Contains("h17\n") || lower.Contains("html in text")) { content = "h17 <div class=\"x\">content</div> html"; desc = "H17: 纯文本含 HTML"; }
        else if (lower.Contains("h18\n") || lower.Contains("sql in text")) { content = "h18 SELECT * FROM users WHERE id = 1; sql"; desc = "H18: 纯文本含 SQL"; }
        else if (lower.Contains("h19\n") || lower.Contains("url in text")) { content = "h19 https://example.com/path?q=1&v=2 url"; desc = "H19: 纯文本含 URL"; }
        else if (lower.Contains("h20\n") || lower.Contains("control chars text")) { content = "h20_before\x01\x02\x03h20_after control"; desc = "H20: 纯文本含控制字符"; }

        if (content is null) return null;

        if (protocol == "anthropic")
            return (new InjectDto { Body = BuildAnthropicResponse(content) }, $"{desc} [anthropic]");
        if (isStream)
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = BuildOpenAiStreamChunks(content) }, $"{desc} [stream]");
        return (new InjectDto { Body = BuildOpenAiResponse(content) }, desc ?? "");
    }
}
