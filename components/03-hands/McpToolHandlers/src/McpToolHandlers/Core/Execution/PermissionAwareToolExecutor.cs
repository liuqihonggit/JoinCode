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
                return context.Result;
            }

            _logger.LogError("Tool {ToolName} pipeline completed without result", toolName);
            return CreateErrorResult($"Tool '{toolName}' execution produced no result.");
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
            return CreateErrorResult($"Error executing tool '{toolName}': {ex.Message}");
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
}
