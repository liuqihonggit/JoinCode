namespace Services.Web;

/// <summary>
/// Anthropic web_search 共享核心逻辑 — 由 <see cref="AnthropicSearchProvider"/> 和 <see cref="WebService"/> 共用。
/// 提取自原 WebService.SearchAsync 的 private 方法，消除复制粘贴（ADR 0138）。
/// </summary>
internal static class AnthropicWebSearchCore {
    /// <summary>
    /// 构建搜索专用系统提示词 — 对齐 TS prompt.ts getWebSearchPrompt()
    /// </summary>
    public static string BuildSystemPrompt(IClockService clock) {
        var currentMonthYear = clock.GetLocalNow().ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        return $"""
            You are an assistant for performing a web search tool use.

            CRITICAL REQUIREMENT - You MUST follow this:
            - After answering the user's question, you MUST include a "Sources:" section at the end of your response
            - In the Sources section, list all relevant URLs from the search results as markdown hyperlinks: [Title](URL)
            - This is MANDATORY - never skip including sources in your response

            IMPORTANT - Use the correct year in search queries:
            - The current month is {currentMonthYear}. You MUST use this year when searching for recent information.
            """;
    }

    /// <summary>
    /// 构建 web_search 工具 schema（对齐 TS 版 BetaWebSearchTool20250305）
    /// 通过 DTO + JsonSourceGeneration 序列化，兼容 NativeAOT
    /// </summary>
    public static JsonElement BuildToolSchema(string[]? allowedDomains, string[]? blockedDomains) {
        var dto = new WebSearchToolSchemaDto {
            AllowedDomains = allowedDomains is { Length: > 0 } ? allowedDomains : null,
            BlockedDomains = blockedDomains is { Length: > 0 } ? blockedDomains : null
        };
        return JsonSerializer.SerializeToElement(dto, WebSearchSchemaJsonContext.Default.WebSearchToolSchemaDto);
    }

    /// <summary>
    /// 从 ApiMessage metadata 提取结构化搜索结果 — 对齐 TS makeOutputFromSearchResponse
    /// </summary>
    public static List<SearchResultItem> ExtractResultsFromMetadata(IReadOnlyList<ApiMessage> messages) {
        var results = new List<SearchResultItem>();

        foreach (var msg in messages) {
            if (msg.Metadata == null || !msg.Metadata.TryGetValue("web_search_results", out var resultsJson))
                continue;

            if (resultsJson.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var block in resultsJson.EnumerateArray()) {
                if (block.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in block.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var dto = JsonSerializer.Deserialize(item, WebSearchSchemaJsonContext.Default.SearchResultItemDto);
                    var title = dto?.Title;
                    var url = dto?.Url;

                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
                        continue;

                    if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    results.Add(new SearchResultItem(title, url));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 从文本内容中提取搜索结果链接（回退方案，兼容非 Anthropic provider）
    /// </summary>
    public static void ExtractResultsFromText(string content, List<SearchResultItem> results) {
        var linkRegex = new Regex(@"\[([^\]]+)\]\((https?://[^)]+)\)", RegexOptions.Compiled);
        var matches = linkRegex.Matches(content);

        foreach (Match match in matches) {
            var title = match.Groups[1].Value.Trim();
            var url = match.Groups[2].Value.Trim();

            if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new SearchResultItem(title, url));
        }
    }
}

/// <summary>
/// web_search 工具 schema DTO — 对齐 TS 版 BetaWebSearchTool20250305
/// 字段使用 snake_case 显式映射，null 域名列表不输出
/// </summary>
public sealed record WebSearchToolSchemaDto {
    /// <summary>工具类型标识，固定为 web_search_20250305。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "web_search_20250305";

    /// <summary>工具名称，固定为 web_search。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "web_search";

    /// <summary>工具最大调用次数，默认 8。</summary>
    [JsonPropertyName("max_uses")]
    public int MaxUses { get; init; } = 8;

    /// <summary>允许的域名白名单，为 null 时不输出该字段。</summary>
    [JsonPropertyName("allowed_domains")]
    public string[]? AllowedDomains { get; init; }

    /// <summary>屏蔽的域名黑名单，为 null 时不输出该字段。</summary>
    [JsonPropertyName("blocked_domains")]
    public string[]? BlockedDomains { get; init; }
}

/// <summary>
/// 搜索结果项 DTO — 解析 web_search_results 中的单个结果 {title, url}
/// </summary>
public sealed record SearchResultItemDto {
    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>
/// Web 搜索 schema 序列化上下文 — NativeAOT 兼容
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebSearchToolSchemaDto))]
[JsonSerializable(typeof(SearchResultItemDto))]
internal sealed partial class WebSearchSchemaJsonContext : JsonSerializerContext;
