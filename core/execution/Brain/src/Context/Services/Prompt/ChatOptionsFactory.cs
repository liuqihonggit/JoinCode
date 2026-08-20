namespace Core.Context;

/// <summary>
/// 聊天选项工厂 — 负责创建 ChatOptions（执行设置）
/// 提取自 ChatService.CreateExecutionSettings
/// </summary>
[Register]
public sealed partial class ChatOptionsFactory : ServiceEntity, IChatOptionsFactory
{

    public ChatOptionsFactory(IChatContextManager contextManager, IExecutionSettingsProvider? executionSettingsProvider = null, IApiContextManagementService? apiContextManagementService = null)
    {
        _contextManager = contextManager;
        _executionSettingsProvider = executionSettingsProvider;
        _apiContextManagementService = apiContextManagementService;
    }
    [Inject] private readonly IChatContextManager _contextManager;
    [Inject] private readonly IExecutionSettingsProvider? _executionSettingsProvider;
    [Inject] private readonly IApiContextManagementService? _apiContextManagementService;

    /// <summary>
    /// 创建当前会话的 ChatOptions
    /// </summary>
    public ChatOptions Create()
    {
        var chatParams = LlmParameters.Chat;
        var discoveredTools = _contextManager.GetDiscoveredTools();
        var deferredTools = _contextManager.GetDeferredTools();

        var effortLevel = _executionSettingsProvider?.EffortLevel;
        var fastMode = _executionSettingsProvider?.FastMode ?? false;
        var fastModelId = _executionSettingsProvider?.FastModelId;

        Dictionary<string, JsonElement>? extensionData = null;
        if (fastMode && !string.IsNullOrEmpty(fastModelId))
        {
            extensionData = new Dictionary<string, JsonElement> { ["model"] = JsonElementHelper.FromString(fastModelId) };
        }

        return new ChatOptions
        {
            Temperature = _executionSettingsProvider?.Temperature ?? chatParams.Temperature,
            MaxTokens = _executionSettingsProvider?.MaxTokens ?? chatParams.MaxTokens,
            TopP = chatParams.TopP,
            FrequencyPenalty = chatParams.FrequencyPenalty,
            PresencePenalty = chatParams.PresencePenalty,
            ToolChoice = ToolChoice.AutoInvoke,
            DiscoveredTools = discoveredTools,
            DeferredTools = deferredTools.Any() ? deferredTools.ToList() : null,
            EffortLevel = effortLevel,
            ThinkingEnabled = _executionSettingsProvider?.ThinkingEnabled ?? false,
            FastMode = fastMode,
            FastModelId = fastModelId,
            ExtensionData = extensionData,
            ContextManagement = _apiContextManagementService?.GetConfig(
                effortLevel is not null ? new ThinkingContext { HasThinking = true } : null)
        };
    }
}
