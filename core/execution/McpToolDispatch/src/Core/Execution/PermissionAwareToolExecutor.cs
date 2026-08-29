namespace McpToolRegistry;

/// <summary>
/// 权限感知的工具执行器 — 通过标准中间件管道执行工具调用
/// 管道: 参数修复 → 必填参数校验 → Schema校验 → Agent限制 → 权限检查 → 远程策略 → FeatureFlag → 执行
/// 权限模式统一从 IToolPermissionManager 获取，不再自行维护
/// </summary>
[Register(typeof(IToolExecutionGateway), ServiceLifetime.Singleton)]
public sealed partial class PermissionAwareToolExecutor : ServiceEntity, IToolExecutionGateway
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ITelemetryService? _telemetryService;
    private readonly IToolPermissionManager _permissionManager;
    private readonly IPermissionConfirmationHandler? _confirmationHandler;
    private readonly ILogger<PermissionAwareToolExecutor> _logger;
    private readonly MiddlewarePipeline<ToolExecutionContext> _pipeline;

    /// <summary>
    /// 工具执行完成事件 — 无论成功或失败都会触发，用于遥测和诊断
    /// </summary>
    public event EventHandler<ToolExecutionCompletedEventArgs>? ToolExecutionCompleted;

    public PermissionAwareToolExecutor(
        IToolRegistry toolRegistry,
        MiddlewarePipeline<ToolExecutionContext> pipeline,
        IToolPermissionManager permissionManager,
        ITelemetryService? telemetryService = null,
        IPermissionConfirmationHandler? confirmationHandler = null,
        ILogger<PermissionAwareToolExecutor>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _telemetryService = telemetryService;
        _confirmationHandler = confirmationHandler;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行工具调用 — 对齐 TS streamedCheckPermissionsAndCallTool
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        var handler = await _toolRegistry.GetToolAsync(toolName, cancellationToken).ConfigureAwait(false);

        if (handler is null)
        {
            _logger.LogWarning(L.T(StringKey.ToolNotFoundLog, toolName));
            return CreateErrorResult($"Tool '{toolName}' not found.");
        }

        await using var span = _telemetryService?.StartSpan($"tool.{toolName}", TelemetrySpanKind.Client);
        if (span is not null)
        {
            span.SetTag("tool.name", toolName);
        }

        var executionEntity = ToolExecutionEntityFactory.Create(
            toolName, toolUseId: null, spanId: span?.SpanId, arguments: arguments);
        executionEntity.LifecycleState = EntityLifecycle.Active;
        executionEntity.StartedAt = DateTime.UtcNow;
        span?.SetTag("entity.object_id", executionEntity.UniqueId);

        var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);

        var context = new ToolExecutionContext
        {
            ToolName = toolName,
            Arguments = arguments,
            Handler = handler,
            OnProgress = onProgress,
            AgentMode = currentMode,
            Span = span,
            ExecutionEntity = executionEntity,
        };

        try
        {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (context.PermissionDecision == PermissionDecision.PendingConfirmation)
            {
                return await HandlePendingConfirmationAsync(toolName, arguments, handler, onProgress, currentMode, span, executionEntity, context, cancellationToken).ConfigureAwait(false);
            }

            if (context.Result is not null)
            {
                CompleteExecutionEntity(context);
                RaiseToolExecutionCompleted(toolName, context.Result, arguments);
                return context.Result;
            }

            _logger.LogError("Tool {ToolName} pipeline completed without result", toolName);
            var noResultError = CreateErrorResult($"Tool '{toolName}' execution produced no result.");
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, noResultError, arguments);
            return noResultError;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(L.T(StringKey.ToolExecCancelledLog, toolName));
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            executionEntity.LifecycleState = EntityLifecycle.Completed;
            executionEntity.CompletedAt = DateTime.UtcNow;
            executionEntity.IsError = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, L.T(StringKey.ToolExecFailedLog, toolName));
            span?.RecordException(ex);
            Diag.WriteError($"[ToolExec] Tool={toolName}", ex);
            var exceptionError = CreateErrorResult($"Error executing tool '{toolName}': {ex.Message}");
            executionEntity.LifecycleState = EntityLifecycle.Completed;
            executionEntity.CompletedAt = DateTime.UtcNow;
            executionEntity.IsError = true;
            RaiseToolExecutionCompleted(toolName, exceptionError, arguments, ex.Message);
            return exceptionError;
        }
    }

    /// <summary>
    /// 处理权限待确认 — 调用 IPermissionConfirmationHandler.Confirm,用户允许则重新执行管道
    /// 确认逻辑统一在此处,QueryEngine 和 ChatToolOrchestrator 两条路径都经过这里
    /// </summary>
    private async Task<ToolResult> HandlePendingConfirmationAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        IToolHandler handler,
        ToolProgressCallback? onProgress,
        PermissionMode currentMode,
        ITelemetrySpan? span,
        ToolExecutionEntity executionEntity,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (_confirmationHandler is null)
        {
            _logger.LogWarning("无确认处理器,工具权限待确认作为拒绝返回: {ToolName}", toolName);
            var noHandlerError = context.Result ?? CreateErrorResult($"工具 '{toolName}' 需要确认但无确认处理器");
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, noHandlerError, arguments);
            return noHandlerError;
        }

        var prompt = context.PermissionConfirmationPrompt ?? "需要确认";
        var action = _confirmationHandler.Confirm(toolName, prompt);

        if (action != PermissionConfirmAction.Allow && action != PermissionConfirmAction.AlwaysAllow)
        {
            _logger.LogWarning("用户拒绝工具执行: {ToolName}", toolName);
            var deniedError = CreateErrorResult($"用户拒绝工具执行: {toolName}");
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, deniedError, arguments);
            return deniedError;
        }

        _logger.LogInformation("用户确认允许工具执行: {ToolName}", toolName);

        var retryContext = new ToolExecutionContext
        {
            ToolName = toolName,
            Arguments = arguments,
            Handler = handler,
            OnProgress = onProgress,
            AgentMode = currentMode,
            Span = span,
            ExecutionEntity = executionEntity,
        };

        await _pipeline.ExecuteAsync(retryContext, cancellationToken).ConfigureAwait(false);

        if (retryContext.Result is not null)
        {
            CompleteExecutionEntity(retryContext);
            RaiseToolExecutionCompleted(toolName, retryContext.Result, arguments);
            return retryContext.Result;
        }

        _logger.LogError("Tool {ToolName} retry pipeline completed without result", toolName);
        var retryNoResultError = CreateErrorResult($"Tool '{toolName}' execution produced no result.");
        CompleteExecutionEntity(retryContext);
        RaiseToolExecutionCompleted(toolName, retryNoResultError, arguments);
        return retryNoResultError;
    }

    private void RecordPermissionDenied(string toolName)
    {
        if (_telemetryService is null) return;

        var counter = _telemetryService.GetCounter("tool.permission.denied", "count", "Tool permission denied count");
        counter.Add(1, new Dictionary<string, string> { ["tool"] = toolName });
    }

    private static void CompleteExecutionEntity(ToolExecutionContext context)
    {
        var entity = context.ExecutionEntity!;
        entity.CompletedAt = DateTime.UtcNow;
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.IsError = context.Result?.IsError ?? true;
        entity.ResultSummary = context.Result?
            .Content?.FirstOrDefault(c => c.Type == ToolContentType.Text)?
            .Text;

        BackfillEntityMetadata(entity, context.Result?.EntityMetadata);
    }

    /// <summary>
    /// 从 ToolResult.EntityMetadata 回填子类 Entity 特有字段
    /// Key 约定: exit_code, process_id, http_status_code, content_length, interrupted, background_task_id
    /// </summary>
    private static void BackfillEntityMetadata(ToolExecutionEntity entity, List<EntityMetadataEntry>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return;

        var dict = new Dictionary<string, EntityMetadataEntry>(metadata.Count, StringComparer.Ordinal);
        foreach (var m in metadata)
            dict.TryAdd(m.Key, m);

        switch (entity)
        {
            case BashProcessEntity bash:
                if (dict.TryGetValue("exit_code", out var exitCodeEntry) && exitCodeEntry.IntValue is int exitCode)
                    bash.ExitCode = exitCode;
                if (dict.TryGetValue("process_id", out var processIdEntry) && processIdEntry.IntValue is int processId)
                    bash.ProcessId = processId;
                if (dict.TryGetValue("interrupted", out var interruptedEntry) && interruptedEntry.BoolValue == true)
                    bash.Status = BashProcessStatus.TimedOut;
                else if (bash.ExitCode.HasValue)
                    bash.Status = BashProcessStatus.Exited;
                break;

            case WebFetchEntity web:
                if (dict.TryGetValue("http_status_code", out var httpStatusEntry) && httpStatusEntry.IntValue is int statusCode)
                    web.HttpStatusCode = statusCode;
                if (dict.TryGetValue("content_length", out var contentLengthEntry) && contentLengthEntry.LongValue is long contentLength)
                    web.ContentLength = contentLength;
                break;
        }
    }

    private static ToolResult CreateErrorResult(string errorMessage)
    {
        return new ToolResult
        {
            Content =
            [
                new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = errorMessage
                }
             ],
            IsError = true
        };
    }

    private void RaiseToolExecutionCompleted(
        string toolName,
        ToolResult? result,
        Dictionary<string, JsonElement> arguments,
        string? errorMessage = null)
    {
        ToolExecutionCompleted?.Invoke(this, new ToolExecutionCompletedEventArgs
        {
            ToolName = toolName,
            IsError = result?.IsError ?? true,
            ErrorMessage = errorMessage ?? result?.Content?.FirstOrDefault(c => c.Type == ToolContentType.Text)?.Text,
            Duration = TimeSpan.Zero,
            Arguments = arguments
        });
    }
}

/// <summary>
/// 工具执行完成事件参数
/// </summary>
public sealed class ToolExecutionCompletedEventArgs : EventArgs
{
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }

    /// <summary>是否执行错误</summary>
    public required bool IsError { get; init; }

    /// <summary>错误消息（成功时为 null）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>工具参数</summary>
    public Dictionary<string, JsonElement> Arguments { get; init; } = [];
}
