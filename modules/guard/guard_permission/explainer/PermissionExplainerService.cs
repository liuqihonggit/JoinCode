namespace Core.Permission;

/// <summary>
/// 权限解释器（旁路模型调用）— 对齐基准 <c>generatePermissionExplanation</c>：
/// 一次性的、不进会话历史的模型请求，拿回「做什么 / 为什么 / 什么风险 / 风险等级」四段。
/// <para><b>fail-soft 是硬要求</b>：关闭开关、没有模型、超时、取消、响应不可解析，一律返回 <c>null</c>
/// 且不抛异常 —— 解释只是帮用户下判断，任何情况下都不许把权限确认本身拖下水。</para>
/// <para><b>超时</b>：基准注释说请求「够快所以不设 abort」；本仓库的对话框是 50ms 一帧的常驻渲染，
/// 无限等待会让「Loading」永远转下去，因此给自己一个上限（默认 20s）后落回不可用。</para>
/// </summary>
[Register(typeof(IPermissionExplainer), ServiceLifetime.Singleton)]
public sealed class PermissionExplainerService : IPermissionExplainer {
    /// <summary>解释请求的等待上限 — 超时即视为不可用（不阻塞对话框）。</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private static readonly ChatOptions ExplainChatOptions = new() {
        Temperature = 0.2f,
        MaxTokens = 500,
    };

    private readonly IQueryService? _queryService;
    private readonly IChatService? _chatService;
    private readonly WorkflowConfig? _workflowConfig;
    private readonly ILogger<PermissionExplainerService>? _logger;

    /// <summary>构造权限解释器。</summary>
    /// <param name="queryService">LLM 查询服务（引擎 DI 提供；无 LLM 的宿主为 null → 恒不可用）。</param>
    /// <param name="chatService">会话服务（用于取最近 assistant 消息做上下文；可为 null）。</param>
    /// <param name="workflowConfig">工作流配置（读 <c>PermissionExplainerEnabled</c>；可为 null → 默认开启）。</param>
    /// <param name="logger">可选的日志记录器。</param>
    public PermissionExplainerService(
        IQueryService? queryService = null,
        IChatService? chatService = null,
        WorkflowConfig? workflowConfig = null,
        ILogger<PermissionExplainerService>? logger = null) {
        _queryService = queryService;
        _chatService = chatService;
        _workflowConfig = workflowConfig;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsEnabled => _workflowConfig?.PermissionExplainerEnabled ?? true;

    /// <inheritdoc/>
    public async Task<PermissionExplanation?> ExplainAsync(PermissionExplainRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsEnabled) {
            return null;
        }

        if (_queryService is null) {
            _logger?.LogDebug("权限解释不可用: 当前宿主未注册 IQueryService");
            return null;
        }

        try {
            var context = await GetConversationContextAsync(cancellationToken).ConfigureAwait(false);
            var messages = new MessageList();
            messages.AddSystemMessage(PermissionExplainerPrompt.SystemPrompt);
            messages.AddUserMessage(PermissionExplainerPrompt.BuildUserPrompt(request, context));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DefaultTimeout);

            var response = await _queryService
                .GetApiMessageContentsAsync(messages, ExplainChatOptions, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            return PermissionExplainerPrompt.TryParse(response.FirstOrDefault()?.Content);
        } catch (OperationCanceledException) {
            _logger?.LogDebug("权限解释取消或超时: {ToolName}", request.ToolName);
            return null;
        } catch (Exception ex) {
            // 解释失败绝不上抛 — 调用方只看到「不可用」
            _logger?.LogDebug(ex, "权限解释失败: {ToolName}", request.ToolName);
            return null;
        }
    }

    /// <summary>取最近 assistant 消息做上下文；会话不可用或读取失败时返回空串（无上下文也能解释）。</summary>
    private async Task<string> GetConversationContextAsync(CancellationToken cancellationToken) {
        if (_chatService is null) {
            return string.Empty;
        }

        try {
            var history = await _chatService.GetMessageListAsync(cancellationToken).ConfigureAwait(false);
            return PermissionExplainerPrompt.ExtractConversationContext(history);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "权限解释上下文读取失败 — 退化为无上下文");
            return string.Empty;
        }
    }
}
