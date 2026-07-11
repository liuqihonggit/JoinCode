
namespace Core.Hooks.ToolPermission;

/// <summary>
/// 权限日志记录器实现
/// 集中处理所有权限决策的分析/遥测日志记录
/// </summary>
[Register]
public sealed partial class PermissionLogger : IPermissionLogger
{
    [Inject] private readonly ILogger<PermissionLogger>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;

    private static readonly FrozenSet<string> CodeEditingTools = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        FileToolNameConstants.FileEdit,
        FileToolNameConstants.FileWrite,
        NotebookToolNameConstants.NotebookEdit);

    /// <inheritdoc />
    public void LogPermissionDecision(PermissionLogContext context, PermissionDecisionArgs args)
    {
        var sourceString = SourceToString(args.Source);

        // 记录分析事件
        if (args.Decision == "accept")
        {
            LogApprovalEvent(context, args.Source, context.WaitingForUserPermissionMs);
        }
        else
        {
            LogRejectionEvent(context, args.Source, context.WaitingForUserPermissionMs);
        }

        // 记录代码编辑工具指标
        if (IsCodeEditingTool(context.ToolName))
        {
            LogCodeEditToolDecision(
                context.ToolName,
                args.Decision,
                sourceString);
        }

        // 记录 OTel 事件
        _logger?.LogInformation(
            "[ToolDecision] Decision={Decision}, Source={Source}, Tool={ToolName}",
            args.Decision,
            sourceString,
            context.ToolName);

        _telemetryService?.RecordCount("permission.decision.count", new() { ["tool"] = context.ToolName, ["decision"] = args.Decision, ["source"] = sourceString }, description: "Permission decision count");
    }

    /// <inheritdoc />
    public void LogPermissionCancelled(PermissionLogContext context)
    {
        _logger?.LogInformation(
            "[ToolUseCancelled] MessageID={MessageId}, ToolName={ToolName}",
            context.MessageId,
            context.ToolName);
    }

    /// <inheritdoc />
    public void LogCodeEditToolDecision(string toolName, string decision, string source, string? language = null)
    {
        var attributes = new Dictionary<string, JsonNode?>
        {
            ["decision"] = JsonValue.Create(decision),
            ["source"] = JsonValue.Create(source),
            ["tool_name"] = JsonValue.Create(toolName)
        };

        if (!string.IsNullOrEmpty(language))
        {
            attributes["language"] = JsonValue.Create(language);
        }

        _logger?.LogInformation(
            "[CodeEditToolDecision] Tool={ToolName}, Decision={Decision}, Source={Source}, Language={Language}",
            toolName,
            decision,
            source,
            language ?? "unknown");
    }

    /// <summary>
    /// 记录批准事件
    /// </summary>
    private void LogApprovalEvent(
        PermissionLogContext context,
        PermissionDecisionSourceType source,
        int? waitMs)
    {
        var eventName = source switch
        {
            PermissionDecisionSourceType.Config => "ToolUseGrantedInConfig",
            PermissionDecisionSourceType.Classifier => "ToolUseGrantedByClassifier",
            PermissionDecisionSourceType.User => waitMs.HasValue
                ? "ToolUseGrantedInPromptTemporary"
                : "ToolUseGrantedInPromptPermanent",
            PermissionDecisionSourceType.Hook => "ToolUseGrantedByPermissionHook",
            _ => "ToolUseGranted"
        };

        _logger?.LogInformation(
            "[{EventName}] MessageID={MessageId}, ToolName={ToolName}, SandboxEnabled={SandboxEnabled}, WaitMs={WaitMs}",
            eventName,
            context.MessageId,
            context.ToolName,
            context.SandboxEnabled,
            waitMs);
    }

    /// <summary>
    /// 记录拒绝事件
    /// </summary>
    private void LogRejectionEvent(
        PermissionLogContext context,
        PermissionDecisionSourceType source,
        int? waitMs)
    {
        var eventName = source switch
        {
            PermissionDecisionSourceType.Config => "ToolUseDeniedInConfig",
            _ => "ToolUseRejectedInPrompt"
        };

        var isHook = source == PermissionDecisionSourceType.Hook;
        var hasFeedback = source == PermissionDecisionSourceType.UserReject;

        _logger?.LogWarning(
            "[{EventName}] MessageID={MessageId}, ToolName={ToolName}, IsHook={IsHook}, HasFeedback={HasFeedback}, WaitMs={WaitMs}",
            eventName,
            context.MessageId,
            context.ToolName,
            isHook,
            hasFeedback,
            waitMs ?? 0);
    }

    /// <summary>
    /// 将来源转换为字符串标签
    /// </summary>
    private static string SourceToString(PermissionDecisionSourceType source)
    {
        return source switch
        {
            PermissionDecisionSourceType.Hook => "hook",
            PermissionDecisionSourceType.User => "user_temporary",
            PermissionDecisionSourceType.Classifier => "classifier",
            PermissionDecisionSourceType.Config => "config",
            PermissionDecisionSourceType.UserAbort => "user_abort",
            PermissionDecisionSourceType.UserReject => "user_reject",
            _ => "unknown"
        };
    }

    /// <summary>
    /// 检查是否为代码编辑工具
    /// </summary>
    private static bool IsCodeEditingTool(string toolName)
    {
        return CodeEditingTools.Contains(toolName);
    }
}
