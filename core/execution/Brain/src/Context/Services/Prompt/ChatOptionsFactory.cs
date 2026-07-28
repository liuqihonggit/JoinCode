namespace Core.Context;

/// <summary>
/// 聊天选项工厂 — 负责创建 ChatOptions（执行设置）
/// 提取自 ChatService.CreateExecutionSettings
/// </summary>
[Register]
public sealed partial class ChatOptionsFactory : IChatOptionsFactory
{
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
            Temperature = chatParams.Temperature,
            MaxTokens = chatParams.MaxTokens,
            TopP = chatParams.TopP,
            FrequencyPenalty = chatParams.FrequencyPenalty,
            PresencePenalty = chatParams.PresencePenalty,
            ToolChoice = ToolChoice.AutoInvoke,
            DiscoveredTools = discoveredTools,
            DeferredTools = deferredTools.Count > 0 ? deferredTools : null,
            EffortLevel = effortLevel,
            FastMode = fastMode,
            FastModelId = fastModelId,
            ExtensionData = extensionData,
            ContextManagement = _apiContextManagementService?.GetConfig(
                hasThinking: effortLevel is not null,
                isRedactThinkingActive: false,
                clearAllThinking: false)
        };
    }
}
