using JoinCode.Abstractions.Attributes;

namespace McpToolRegistry;

/// <summary>
/// 权限感知的工具执行器 — 通过标准中间件管道执行工具调用
/// 管道: 参数修复 → 必填参数校验 → Schema校验 → Agent限制 → 权限检查 → 远程策略 → FeatureFlag → 执行
/// </summary>
[Register]
public sealed partial class PermissionAwareToolExecutor
{
    private readonly IToolRegistry _toolRegistry;
    private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<PermissionAwareToolExecutor> _logger;
    private readonly MiddlewarePipeline<ToolExecutionContext> _pipeline;
    private PermissionMode _currentAgentMode = PermissionMode.Auto;

    /// <summary>
    /// 工具执行完成事件 — 无论成功或失败都会触发，用于遥测和诊断
    /// </summary>
    public event EventHandler<ToolExecutionCompletedEventArgs>? ToolExecutionCompleted;

    public PermissionMode CurrentAgentMode
    {
        get => _currentAgentMode;
        set => _currentAgentMode = value;
    }

    public PermissionAwareToolExecutor(
        IToolRegistry toolRegistry,
        MiddlewarePipeline<ToolExecutionContext> pipeline,
        ITelemetryService? telemetryService = null,
        ILogger<PermissionAwareToolExecutor>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _telemetryService = telemetryService;
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

        var context = new ToolExecutionContext
        {
            ToolName = toolName,
            Arguments = arguments,
            Handler = handler,
            OnProgress = onProgress,
            AgentMode = _currentAgentMode,

            Span = span,
        };

        try
        {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (context.Result is not null)
            {
                RaiseToolExecutionCompleted(toolName, context.Result, arguments);
                return context.Result;
            }

            _logger.LogError("Tool {ToolName} pipeline completed without result", toolName);
            var noResultError = CreateErrorResult($"Tool '{toolName}' execution produced no result.");
            RaiseToolExecutionCompleted(toolName, noResultError, arguments);
            return noResultError;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(L.T(StringKey.ToolExecCancelledLog, toolName));
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            throw;
        }
        catch (PermissionDeniedException)
        {
            _logger.LogWarning(L.T(StringKey.ToolExecPermissionDeniedLog, toolName));
            span?.SetStatus(TelemetryStatusCode.Error, "Permission denied");
            RecordPermissionDenied(toolName);
            RaiseToolExecutionCompleted(toolName, null, arguments, "permission_denied");
            throw;
        }
        catch (PermissionPendingConfirmationException)
        {
            _logger.LogInformation(L.T(StringKey.ToolExecNeedsConfirmLog, toolName));
            span?.SetStatus(TelemetryStatusCode.Error, "Pending confirmation");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, L.T(StringKey.ToolExecFailedLog, toolName));
            span?.RecordException(ex);
            var exceptionError = CreateErrorResult($"Error executing tool '{toolName}': {ex.Message}");
            RaiseToolExecutionCompleted(toolName, exceptionError, arguments, ex.Message);
            return exceptionError;
        }
    }

    private void RecordPermissionDenied(string toolName)
    {
        if (_telemetryService is null) return;

        var counter = _telemetryService.GetCounter("tool.permission.denied", "count", "Tool permission denied count");
        counter.Add(1, new Dictionary<string, string> { ["tool"] = toolName });
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
    public Dictionary<string, JsonElement>? Arguments { get; init; }
}
