
namespace Core.Permission;

/// <summary>
/// 危险命令保护中间件 — 替代 AutoSafetyMiddleware 中的危险命令检查
/// 使用 ICommandRiskHandler 映射表对各种 CommandRisk 提供细粒度的拦截策略
/// Auto/Default 模式: 根据风险类型返回拒绝+引导消息
/// Ask 模式: 返回待确认，附带风险说明和建议
/// Plan 模式: 拒绝所有危险命令
/// 同时支持 IDeleteOperationDetector 检测非 Shell 工具的删除操作（如 file_delete）
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class DangerousCommandProtectionMiddleware : IPermissionMiddleware
{
    private readonly IDestructiveCommandDetector? _destructiveCommandDetector;
    private readonly FrozenDictionary<CommandRisk, ICommandRiskHandler> _riskHandlers;
    private readonly IReadOnlyList<IDeleteOperationDetector> _deleteDetectors;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 DangerousCommandProtectionMiddleware
    /// </summary>
    public DangerousCommandProtectionMiddleware(
        IEnumerable<ICommandRiskHandler>? riskHandlers = null,
        IDestructiveCommandDetector? destructiveCommandDetector = null,
        IEnumerable<IDeleteOperationDetector>? deleteDetectors = null)
    {
        _destructiveCommandDetector = destructiveCommandDetector;
        _riskHandlers = (riskHandlers ?? []).ToFrozenDictionary(h => h.RiskType);
        _deleteDetectors = (deleteDetectors ?? []).ToList();
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        // Bypass 模式跳过所有检查
        if (context.CurrentMode == PermissionMode.BypassPermissions ||
            context.CurrentMode == PermissionMode.DontAsk)
            return next(context, ct);

        // 1. 检查非 Shell 工具的删除操作（如 file_delete）
        var deleteInfo = DetectDeleteOperation(context);
        if (deleteInfo is not null)
        {
            HandleDeleteOperation(context, deleteInfo);
            return Task.CompletedTask;
        }

        // 2. 检查 Shell 工具的危险命令
        if (!context.IsShellOperation(context.ToolName))
            return next(context, ct);

        if (context.Arguments is null ||
            !context.Arguments.TryGetValue("command", out var cmdEl) ||
            cmdEl.ValueKind != JsonValueKind.String)
            return next(context, ct);

        var command = cmdEl.GetString()!;
        var riskContext = DetectRisks(context.ToolName, command);

        if (riskContext is null || riskContext.Risks.Count == 0)
            return next(context, ct);

        HandleRisks(context, riskContext);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 检测非 Shell 工具的删除操作
    /// </summary>
    private DeleteOperationInfo? DetectDeleteOperation(PermissionCheckContext context)
    {
        for (var i = 0; i < _deleteDetectors.Count; i++)
        {
            var info = _deleteDetectors[i].Detect(context.ToolName, context.Arguments);
            if (info is not null)
                return info;
        }

        return null;
    }

    /// <summary>
    /// 处理删除操作 — 复用 FileDeletionRiskHandler 的消息构建逻辑
    /// </summary>
    private void HandleDeleteOperation(PermissionCheckContext context, DeleteOperationInfo deleteInfo)
    {
        var handler = _riskHandlers.GetValueOrDefault(CommandRisk.FileDeletion);
        var riskContext = new CommandRiskContext
        {
            ToolName = context.ToolName,
            Risks = [CommandRisk.FileDeletion],
            Details = deleteInfo.SourceDescription
        };

        // 补充路径信息
        if (deleteInfo.TargetPath is not null)
        {
            riskContext = riskContext with
            {
                ShellCommand = ShellCommand.Parse($"rm {deleteInfo.TargetPath}")
            };
        }

        switch (context.CurrentMode)
        {
            case PermissionMode.Auto:
            case PermissionMode.Default:
                var rejection = handler is not null
                    ? handler.BuildRejectionMessage(riskContext)
                    : $"文件删除操作已被阻止（{deleteInfo.SourceDescription}）。请使用 Shell 工具将文件移动到 .xxx/ 目录";
                context.Result = ToolPermissionCheckResult.Rejected(rejection);
                break;

            case PermissionMode.Ask:
                var confirmation = handler is not null
                    ? handler.BuildConfirmationMessage(riskContext)
                    : $"工具 '{context.ToolName}' 请求删除文件（{deleteInfo.SourceDescription}）。建议移动到 .xxx/ 目录。是否允许删除？";
                context.Result = ToolPermissionCheckResult.PendingConfirmation(confirmation);
                break;

            case PermissionMode.Plan:
                context.Result = ToolPermissionCheckResult.Rejected(
                    $"Plan 模式下禁止文件删除操作（{deleteInfo.SourceDescription}）。请使用 Shell 工具将文件移动到 .xxx/ 目录");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 检测 Shell 命令的风险
    /// </summary>
    private CommandRiskContext? DetectRisks(string toolName, string command)
    {
        if (_destructiveCommandDetector is null)
            return null;

        var shellCommand = ShellCommand.Parse(command);
        var result = _destructiveCommandDetector.Detect(shellCommand);

        if (!result.IsDestructive)
            return null;

        return new CommandRiskContext
        {
            ToolName = toolName,
            ShellCommand = shellCommand,
            Risks = result.Risks,
            Details = result.Details
        };
    }

    /// <summary>
    /// 处理检测到的风险 — 使用最高优先级的风险处理器
    /// 优先级: FileDeletion > DirectoryDeletion > PrivilegeEscalation > RemoteExecution > ForceOperation > RecursiveOperation > DataModification > SystemModification > PathEscape
    /// </summary>
    private void HandleRisks(PermissionCheckContext context, CommandRiskContext riskContext)
    {
        // 按优先级选择最关键的风险
        var primaryRisk = SelectPrimaryRisk(riskContext.Risks);
        var handler = primaryRisk is not null ? _riskHandlers.GetValueOrDefault(primaryRisk.Value) : null;

        switch (context.CurrentMode)
        {
            case PermissionMode.Auto:
            case PermissionMode.Default:
                var rejection = handler is not null
                    ? handler.BuildRejectionMessage(riskContext)
                    : $"危险操作已被阻止（{riskContext.Details}）。如确需执行请切换到 Ask 模式确认";
                context.Result = ToolPermissionCheckResult.Rejected(rejection);
                break;

            case PermissionMode.Ask:
                var confirmation = handler is not null
                    ? handler.BuildConfirmationMessage(riskContext)
                    : $"工具 '{context.ToolName}' 请求执行危险操作（{riskContext.Details}）。是否批准？";
                context.Result = ToolPermissionCheckResult.PendingConfirmation(confirmation);
                break;

            case PermissionMode.Plan:
                context.Result = ToolPermissionCheckResult.Rejected(
                    $"Plan 模式下禁止危险操作（{riskContext.Details}）");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 选择最高优先级的风险
    /// </summary>
    private static CommandRisk? SelectPrimaryRisk(IReadOnlyList<CommandRisk> risks)
    {
        // 优先级从高到低
        var priority = new[]
        {
            CommandRisk.FileDeletion,
            CommandRisk.DirectoryDeletion,
            CommandRisk.PrivilegeEscalation,
            CommandRisk.RemoteExecution,
            CommandRisk.ForceOperation,
            CommandRisk.RecursiveOperation,
            CommandRisk.DataModification,
            CommandRisk.SystemModification,
            CommandRisk.PathEscape
        };

        foreach (var risk in priority)
        {
            if (risks.Contains(risk))
                return risk;
        }

        return risks.FirstOrDefault();
    }
}
