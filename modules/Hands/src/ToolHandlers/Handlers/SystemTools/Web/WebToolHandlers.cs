


namespace Tools.Handlers;

[McpToolHandler(ToolCategory.Web)]
public class WebToolHandlers
{
    private readonly IWebService _webService;
    private readonly JoinCode.Abstractions.LLM.IQueryService? _queryService;
    private readonly ITelemetryService? _telemetryService;
    private readonly ProviderConfig? _providerConfig;

    public WebToolHandlers(
        IWebService webService,
        JoinCode.Abstractions.LLM.IQueryService? queryService = null,
        ITelemetryService? telemetryService = null,
        ProviderConfig? providerConfig = null)
    {
        _webService = webService ?? throw new ArgumentNullException(nameof(webService));
        _queryService = queryService;
        _telemetryService = telemetryService;
        _providerConfig = providerConfig;
    }

    [McpTool(WebToolNameConstants.WebFetch, "Fetch web content from a URL and process with AI model", "web")]
    public async Task<ToolResult> WebFetchAsync(
        [McpToolParameter("URL to fetch")] string url,
        [McpToolParameter("Prompt describing what information to extract from the page")] string prompt,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(url, "url"),
            ValidationHelper.ValidateRequired(prompt, "prompt"),
            ValidationHelper.ValidateStringLength(url, 2048, "URL"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        // 域名级权限检查已移入全局权限链路 PermissionChecker.CheckWebFetchPermission
        // 对齐 TS 版: checkPermissions 返回 behavior:'ask' 时由全局权限系统弹出确认框

        var result = await _webService.FetchAsync(url, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordWebMetrics("fetch", "failed");
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to fetch web content").Build();
        }

        if (result.RedirectUrl != null)
        {
            var statusText = result.RedirectStatusCode switch
            {
                301 => "Moved Permanently",
                308 => "Permanent Redirect",
                307 => "Temporary Redirect",
                _ => "Found"
            };

            var redirectMessage = $"REDIRECT DETECTED: The URL redirects to a different host.\n\nOriginal URL: {result.Url}\nRedirect URL: {result.RedirectUrl}\nStatus: {result.RedirectStatusCode} {statusText}\n\nTo complete your request, I need to fetch content from the redirected URL. Please use WebFetch again with these parameters:\n- url: \"{result.RedirectUrl}\"\n- prompt: \"{prompt}\"";

            RecordWebMetrics("fetch", "redirect");
            return ResultBuilder.Success().WithText(redirectMessage).Build();
        }

        var markdownContent = result.Content ?? string.Empty;
        var isPreapproved = IsPreapprovedUrl(url);

        string processedResult;
        if (isPreapproved
            && (result.ContentType?.Contains("text/markdown", StringComparison.OrdinalIgnoreCase) ?? false)
            && markdownContent.Length < 100_000)
        {
            processedResult = markdownContent;
        }
        else
        {
            processedResult = await ApplyPromptToMarkdownAsync(
                prompt, markdownContent, isPreapproved, cancellationToken).ConfigureAwait(false);
        }

        RecordWebMetrics("fetch", "ok", result.Bytes);

        // 对齐TS版: 二进制内容持久化路径提示
        // TS: [Binary content (contentType, size) also saved to path]
        var finalResult = processedResult;
        if (result.PersistedPath is not null)
        {
            var sizeStr = ContentReplacementConstants.FormatFileSize(result.PersistedSize);
            finalResult += $"\n\n[Binary content ({result.ContentType}, {sizeStr}) also saved to {result.PersistedPath}]";
        }

        return ResultBuilder.Success().WithText(finalResult).Build();
    }

    [McpTool(WebToolNameConstants.WebToMarkdown, "Fetch a URL and convert its HTML content to Markdown format", "web")]
    public async Task<ToolResult> WebToMarkdownAsync(
        [McpToolParameter("URL to fetch and convert to Markdown")] string url,
        [McpToolParameter("Maximum length of the output in characters, default 100000", Required = false, DefaultValue = "100000")] int? max_length = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(url, "url"),
            ValidationHelper.ValidateStringLength(url, 2048, "URL"),
            ValidationHelper.ValidateRange(max_length, 1, int.MaxValue, "max_length"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _webService.FetchAsync(url, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordWebMetrics("to_markdown", "failed");
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to fetch web content").Build();
        }

        if (result.RedirectUrl != null)
        {
            RecordWebMetrics("to_markdown", "redirect");
            return ResultBuilder.Error().WithText($"URL redirects to {result.RedirectUrl} (status {result.RedirectStatusCode}). Please use the redirect URL directly.").Build();
        }

        var markdownContent = result.Content ?? string.Empty;
        var maxLength = max_length ?? 100_000;

        if (markdownContent.Length > maxLength)
        {
            markdownContent = markdownContent[..maxLength] + "\n\n[Content truncated due to length...]";
        }

        RecordWebMetrics("to_markdown", "ok", result.Bytes);
        return ResultBuilder.Success().WithText(markdownContent).Build();
    }

    [McpTool(WebToolNameConstants.WebSearch, "Search the web for up-to-date information (requires Anthropic provider)", "web")]
    public async Task<ToolResult> WebSearchAsync(
        [McpToolParameter("Search query")] string query,
        [McpToolParameter("Only include results from these domains", Required = false)] string[]? allowed_domains = null,
        [McpToolParameter("Exclude results from these domains", Required = false)] string[]? blocked_domains = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidationHelper.CombineErrors(
            ValidationHelper.ValidateRequired(query, "query"),
            ValidationHelper.ValidateStringLength(query, 500, "search query"));
        if (validationError != null)
        {
            return ResultBuilder.Error().WithText(validationError).Build();
        }

        var result = await _webService.SearchAsync(
            query,
            allowedDomains: allowed_domains,
            blockedDomains: blocked_domains,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            RecordWebMetrics("search", "failed");
            return ResultBuilder.Error().WithText(result.ErrorMessage ?? "Search failed").Build();
        }

        var response = new StringBuilder();

        // 对齐 TS 版 mapToolResultToToolResultBlockParam 格式
        if (result.Results.Count > 0)
        {
            response.AppendLine("Links:");
            for (int i = 0; i < result.Results.Count; i++)
            {
                var item = result.Results[i];
                response.AppendLine($"{i + 1}. [{item.Title}]({item.Url})");

                if (!string.IsNullOrEmpty(item.Snippet))
                {
                    response.AppendLine($"   Snippet: {item.Snippet}");
                }
            }
        }
        else
        {
            // 对齐 TS 版: 无搜索结果时输出 "No links found."
            response.AppendLine("No links found.");
        }

        if (result.DurationSeconds > 0)
        {
            response.AppendLine();
            response.AppendLine($"Search completed in {result.DurationSeconds:F1}s");
        }

        // 对齐 TS 版: 强制提醒包含来源
        response.AppendLine();
        response.AppendLine("REMINDER: You MUST include the sources above in your response using markdown hyperlinks: [Title](URL)");

        RecordWebMetrics("search", "ok", result.Results.Count);
        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    private async Task<string> ApplyPromptToMarkdownAsync(
        string prompt,
        string markdownContent,
        bool isPreapprovedDomain,
        CancellationToken cancellationToken)
    {
        if (_queryService == null)
        {
            return markdownContent.Length > 5000
                ? markdownContent[..5000] + "\n\n[Content truncated — LLM processing unavailable]"
                : markdownContent;
        }

        var truncatedContent = markdownContent.Length > 100_000
            ? markdownContent[..100_000] + "\n\n[Content truncated due to length...]"
            : markdownContent;

        var modelPrompt = WebFetchToolPrompt.MakeSecondaryModelPrompt(
            truncatedContent, prompt, isPreapprovedDomain);

        var chatHistory = new MessageList();
        chatHistory.AddUserMessage(modelPrompt);

        // 对齐 TS 版 queryHaiku — 使用快速模型处理 WebFetch 的二级调用
        var fastModelId = _providerConfig?.Definition?.DefaultFastModelId;
        var options = new ChatOptions
        {
            Temperature = 0,
            MaxTokens = 4096,
            FastMode = !string.IsNullOrEmpty(fastModelId),
            FastModelId = fastModelId
        };

        try
        {
            var results = await _queryService.GetApiMessageContentsAsync(
                chatHistory,
                options,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var responseText = results.FirstOrDefault()?.Content;
            return !string.IsNullOrEmpty(responseText) ? responseText : "No response from model";
        }
        catch (Exception ex)
        {
            return markdownContent.Length > 5000
                ? markdownContent[..5000] + $"\n\n[LLM processing failed: {ex.Message}]"
                : markdownContent;
        }
    }

    private static bool IsPreapprovedUrl(string url)
    {
        return PreapprovedDomains.IsPreapprovedUrl(url);
    }

    private void RecordWebMetrics(string operation, string result, int size = 0)
    {
        _telemetryService?.RecordCount("web.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, description: "Web operation count");
        if (size > 0) _telemetryService?.RecordHistogram("web.operation.size", size, new Dictionary<string, string> { ["operation"] = operation }, "bytes", "Web operation response size");
    }
}
