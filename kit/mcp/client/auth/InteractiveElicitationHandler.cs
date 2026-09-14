namespace McpClient;

/// <summary>
/// 交互式 Elicitation 处理器 — 通过用户交互服务（IUserInteractionService）将服务器的 elicitation 请求转发给终端用户,
/// 支持 Form 与 Url 两种模式,串行处理请求队列。
/// </summary>
[Register(typeof(IElicitationHandler), ServiceLifetime.Singleton)]
public sealed partial class InteractiveElicitationHandler : ServiceEntity, IElicitationHandler
{
    private readonly IUserInteractionService _userInteraction;
    private readonly ILogger<InteractiveElicitationHandler>? _logger;
    private readonly AsyncLock _queueLock = new();

    /// <summary>
    /// 构造 InteractiveElicitationHandler 实例。
    /// </summary>
    /// <param name="userInteraction">用户交互服务,用于向终端用户提问与确认。</param>
    /// <param name="logger">日志记录器。</param>
    public InteractiveElicitationHandler(
        IUserInteractionService userInteraction,
        ILogger<InteractiveElicitationHandler>? logger = null)
    {
        _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        _logger = logger;
    }

    /// <summary>
    /// 处理 Elicitation 请求 — 依据请求模式分发到 Form 或 Url 处理流程,串行执行以避免并发冲突。
    /// </summary>
    /// <param name="serverName">发起请求的服务器名称。</param>
    /// <param name="requestId">JSON-RPC 请求标识。</param>
    /// <param name="params">Elicitation 请求参数,包含模式、消息与 schema。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Elicitation 处理结果,包含 Accept/Decline/Cancel 动作与内容。</returns>
    public async Task<ElicitResult> HandleElicitationAsync(
        string serverName,
        JsonRpcId requestId,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        using var guard = await _queueLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_queueLock.Name}' 等待超时");
        try
        {
            var mode = @params.Mode == ElicitModeEnumConstants.Url ? ElicitModeEnumConstants.Url : ElicitModeEnumConstants.Form;

            _logger?.LogInformation("处理 Elicitation 请求: 服务器={ServerName}, 模式={Mode}", serverName, mode);

            return mode == ElicitModeEnumConstants.Url
                ? await HandleUrlModeAsync(serverName, @params, cancellationToken).ConfigureAwait(false)
                : await HandleFormModeAsync(serverName, @params, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Elicitation 请求被取消: 服务器={ServerName}", serverName);
            return new ElicitResult { Action = ElicitActionEnumConstants.Cancel };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 Elicitation 请求失败: 服务器={ServerName}", serverName);
            return new ElicitResult { Action = ElicitActionEnumConstants.Cancel };
        }

    }

    private async Task<ElicitResult> HandleFormModeAsync(
        string serverName,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        var question = $"[MCP:{serverName}] {@params.Message}";

        if (@params.RequestedSchema?.Properties == null || @params.RequestedSchema.Properties.Count == 0)
        {
            var result = await _userInteraction.AskQuestionAsync(question, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrEmpty(result.Response))
            {
                return new ElicitResult { Action = ElicitActionEnumConstants.Decline };
            }

            return new ElicitResult
            {
                Action = ElicitActionEnumConstants.Accept,
                Content = new Dictionary<string, JsonElement?>
                {
                    ["response"] = JsonSerializer.SerializeToElement(result.Response, McpClientJsonContext.Default.String)
                }
            };
        }

        var content = new Dictionary<string, JsonElement?>();
        var requiredSet = @params.RequestedSchema.Required?.ToHashSet() ?? new HashSet<string>();

        foreach (var kvp in @params.RequestedSchema.Properties)
        {
            var fieldName = kvp.Key;
            var field = kvp.Value;
            var fieldLabel = !string.IsNullOrEmpty(field.Title) ? field.Title : fieldName;
            var fieldDesc = !string.IsNullOrEmpty(field.Description) ? $"\n  {field.Description}" : "";
            var isRequired = requiredSet.Contains(fieldName);
            var fieldQuestion = $"{question}\n  {fieldLabel}{fieldDesc}{(isRequired ? " (必填)" : " (可选)")}";

            List<string>? options = null;
            if (field.Enum is { Count: > 0 })
            {
                options = field.Enum;
            }

            var fieldResult = await _userInteraction.AskQuestionAsync(fieldQuestion, options, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!fieldResult.Success)
            {
                if (isRequired)
                {
                    return new ElicitResult { Action = ElicitActionEnumConstants.Decline };
                }
                continue;
            }

            if (string.IsNullOrEmpty(fieldResult.Response) && isRequired)
            {
                return new ElicitResult { Action = ElicitActionEnumConstants.Decline };
            }

            if (!string.IsNullOrEmpty(fieldResult.Response))
            {
                content[fieldName] = ConvertFieldValue(field.Type, fieldResult.Response);
            }
        }

        return new ElicitResult
        {
            Action = ElicitActionEnumConstants.Accept,
            Content = content
        };
    }

    private async Task<ElicitResult> HandleUrlModeAsync(
        string serverName,
        ElicitRequestParams @params,
        CancellationToken cancellationToken)
    {
        var url = @params.Url ?? string.Empty;
        var message = $"[MCP:{serverName}] {@params.Message}\n  URL: {url}\n\n请在浏览器中完成操作后确认。";

        var confirmed = await _userInteraction.ConfirmAsync(message, cancellationToken).ConfigureAwait(false);

        if (!confirmed)
        {
            return new ElicitResult { Action = ElicitActionEnumConstants.Decline };
        }

        return new ElicitResult { Action = ElicitActionEnumConstants.Accept };
    }

    private static JsonElement? ConvertFieldValue(string fieldType, string value)
    {
        return fieldType switch
        {
            "number" or "integer" when double.TryParse(value, out var num) =>
                JsonSerializer.SerializeToElement(num, McpClientJsonContext.Default.Double),
            "boolean" when bool.TryParse(value, out var b) =>
                JsonSerializer.SerializeToElement(b, McpClientJsonContext.Default.String),
            _ => JsonSerializer.SerializeToElement(value, McpClientJsonContext.Default.String)
        };
    }

    /// <summary>释放资源 — 释放请求队列锁。</summary>
    protected override void OnDispose() => _queueLock.Dispose();
}