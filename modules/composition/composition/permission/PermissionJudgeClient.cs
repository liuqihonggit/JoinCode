namespace JoinCode.Composition.Permission;

/// <summary>
/// 权限判定轻量客户端实现 — 单次 LLM 调用，不进入 QueryEngine，不执行工具。
/// <para>超时 3 秒；失败/空响应返回 null，由 <c>LlmAutoModeClassifier</c> 回退到规则结果。</para>
/// </summary>
[Register(typeof(IPermissionJudgeClient), ServiceLifetime.Singleton)]
public sealed partial class PermissionJudgeClient : ServiceEntity, IPermissionJudgeClient {
    private readonly IChatClient _kernel;
    private readonly ILogger<PermissionJudgeClient>? _logger;

    /// <summary>判定请求最长等待时间 — 权限检查不能因 AI 判定无限阻塞。</summary>
    private static readonly TimeSpan JudgeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 初始化 <see cref="PermissionJudgeClient"/> 实例。
    /// </summary>
    /// <param name="kernel">聊天客户端（用于单次判定调用）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public PermissionJudgeClient(IChatClient kernel, ILogger<PermissionJudgeClient>? logger = null) {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> JudgeAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(systemPrompt) || string.IsNullOrWhiteSpace(userPrompt))
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(JudgeTimeout);

        var chatHistory = new MessageList {
            new(MessageRole.System, systemPrompt),
            new(MessageRole.User, userPrompt),
        };

        try {
            var completion = _kernel.GetChatCompletionService();
            var results = await completion.GetApiMessageContentsAsync(chatHistory, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
            var content = results.FirstOrDefault()?.Content;
            if (string.IsNullOrWhiteSpace(content)) {
                _logger?.LogWarning("[PermissionJudge] LLM 返回空判定");
                return null;
            }

            return content;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[PermissionJudge] 判定调用失败，回退静态规则");
            return null;
        }
    }
}
