namespace Services.Web;

/// <summary>
/// Anthropic web_search 供应商 — 复用现有 Anthropic 服务端 web_search_20250305 工具链路。
/// 仅当 ProviderConfig.Definition.SupportsWebSearch 为 true 时可用。
/// 优先级 500，由 <see cref="AnthropicWebSearchCore"/> 提供共享逻辑。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class AnthropicSearchProvider : SearchProviderBase {
    private const int DefaultPriority = 500;

    private readonly IQueryService? _queryService;
    private readonly ProviderConfig? _providerConfig;
    private readonly IClockService _clock;

    /// <summary>
    /// 初始化 <see cref="AnthropicSearchProvider"/> 实例。
    /// </summary>
    /// <param name="clock">时钟服务，用于生成搜索提示词中的当前日期。</param>
    /// <param name="queryService">可选的查询服务，用于执行 Anthropic web_search。</param>
    /// <param name="providerConfig">可选的供应商配置，用于判断是否支持 WebSearch。</param>
    /// <param name="logger">可选日志记录器。</param>
    public AnthropicSearchProvider(
        IClockService? clock = null,
        IQueryService? queryService = null,
        ProviderConfig? providerConfig = null,
        ILogger<AnthropicSearchProvider>? logger = null)
        : base(logger) {
        _clock = clock ?? SystemClockService.Instance;
        _queryService = queryService;
        _providerConfig = providerConfig;
    }

    /// <inheritdoc />
    public override string Name => "Anthropic";

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override bool IsAvailable =>
        _providerConfig is { Definition.SupportsWebSearch: true } && _queryService is not null;

    /// <inheritdoc />
    protected override async Task<SearchProviderResult> SearchCoreAsync(SearchRequest request, CancellationToken cancellationToken) {
        var providerConfig = _providerConfig;
        var queryService = _queryService;
        if (providerConfig is null || queryService is null) {
            return new SearchProviderResult(false, Name, [], 0, "Anthropic provider not available");
        }
        if (providerConfig.Definition is not { SupportsWebSearch: true }) {
            return new SearchProviderResult(false, Name, [], 0, "Anthropic provider does not support web search");
        }

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage(AnthropicWebSearchCore.BuildSystemPrompt(_clock));
        chatHistory.AddUserMessage($"Perform a web search for the query: {request.Query}");

        var webSearchToolSchema = AnthropicWebSearchCore.BuildToolSchema(request.AllowedDomains, request.BlockedDomains);
        var options = new ChatOptions {
            Temperature = 0,
            MaxTokens = 4096,
            FastMode = true,
            FastModelId = providerConfig.Definition.DefaultFastModelId,
            ExtensionData = new Dictionary<string, JsonElement> {
                ["web_search_tool"] = webSearchToolSchema
            }
        };

        var results = await queryService.GetApiMessageContentsAsync(
            chatHistory, options, cancellationToken: cancellationToken).ConfigureAwait(false);

        var searchResults = AnthropicWebSearchCore.ExtractResultsFromMetadata(results);
        if (searchResults.Count == 0) {
            foreach (var msg in results) {
                if (string.IsNullOrEmpty(msg.Content)) continue;
                AnthropicWebSearchCore.ExtractResultsFromText(msg.Content, searchResults);
            }
        }

        return new SearchProviderResult(true, Name, searchResults);
    }
}
