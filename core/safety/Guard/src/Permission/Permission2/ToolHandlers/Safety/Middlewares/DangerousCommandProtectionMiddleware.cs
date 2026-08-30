
namespace Core.Permission;

/// <summary>
/// 危险命令保护中间件 — 替代 AutoSafetyMiddleware 中的危险命令检查
/// 使用 ICommandRiskHandler 映射表对各种 CommandRisk 提供细粒度的拦截策略
/// Auto/Default 模式: 根据风险类型返回拒绝+引导消息
/// Ask 模式: 返回待确认，附带风险说明和建议
/// Plan 模式: 拒绝所有危险命令
/// 同时支持 IDeleteOperationDetector 检测非 Shell 工具的删除操作（如 file_delete）
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DangerousCommandProtectionMiddleware : ServiceEntity, IPermissionMiddleware
{
    private readonly IDestructiveCommandDetector? _destructiveCommandDetector;
    private readonly ICommandDangerClassifier? _dangerClassifier;
    private readonly FrozenDictionary<CommandRisk, ICommandRiskHandler> _riskHandlers;
    private readonly IReadOnlyList<IDeleteOperationDetector> _deleteDetectors;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 DangerousCommandProtectionMiddleware
    /// </summary>
    public DangerousCommandProtectionMiddleware(
        IEnumerable<ICommandRiskHandler>? riskHandlers = null,
        IDestructiveCommandDetector? destructiveCommandDetector = null,
        IEnumerable<IDeleteOperationDetector>? deleteDetectors = null,
        ICommandDangerClassifier? dangerClassifier = null)
    {
        _destructiveCommandDetector = destructiveCommandDetector;
        _dangerClassifier = dangerClassifier;
        _riskHandlers = (riskHandlers ?? []).ToFrozenDictionary(h => h.RiskType);
        _deleteDetectors = (deleteDetectors ?? []).ToList();
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        // Bypass 模式跳过所有检查
        if (context.CurrentMode == PermissionMode.Bypass)
            return next(context, ct);

        // 1. 检查非 Shell 工具的删除操作（如 file_delete）
        var deleteInfo = DetectDeleteOperation(context);
        if (deleteInfo is not null)
        {
            HandleDeleteOperation(context, deleteInfo);
            return Task.CompletedTask;
        }

        // 2. 检查敏感路径写入（Auto 模式下，对齐原 AutoSafetyMiddleware 逻辑）
        if (context.CurrentMode == PermissionMode.Auto &&
            context.IsWriteOperation(context.ToolName) &&
            context.Arguments != null &&
            context.Arguments.TryGetValue("path", out var pathEl) &&
            pathEl.ValueKind == JsonValueKind.String)
        {
            var path = pathEl.GetString()!;
            if (PermissionCheckContext.IsSensitivePath(path, context.Config.SensitivePathPatterns))
            {
                context.Result = ToolPermissionCheckResult.PendingConfirmation(
                    $"工具 '{context.ToolName}' 尝试写入敏感路径 '{path}'，是否批准？");
                return Task.CompletedTask;
            }
        }

        // 3. 检查 Shell 工具的危险命令
        if (!context.IsShellOperation(context.ToolName))
            return next(context, ct);

        if (context.Arguments is null ||
            !context.Arguments.TryGetValue("command", out var cmdEl) ||
            cmdEl.ValueKind != JsonValueKind.String)
            return next(context, ct);

        var command = cmdEl.GetString()!;
        var (riskContext, dangerResult) = DetectRisks(context.ToolName, command);

        if (riskContext is null || riskContext.Risks.Count == 0)
            return next(context, ct);

        HandleRisks(context, riskContext, dangerResult);
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
    /// 检测 Shell 命令的风险 — 优先使用 ICommandDangerClassifier（统一分级），回退到 IDestructiveCommandDetector
    /// </summary>
    private (CommandRiskContext? Context, DangerClassificationResult? DangerResult) DetectRisks(string toolName, string command)
    {
        // 优先使用统一危险分类器
        if (_dangerClassifier is not null)
        {
            var dangerResult = _dangerClassifier.Classify(command);
            if (dangerResult.RequiresIntervention)
            {
                var shellCommand = ShellCommand.Parse(command);
                var riskContext = new CommandRiskContext
                {
                    ToolName = toolName,
                    ShellCommand = shellCommand,
                    Risks = [dangerResult.RiskType],
                    Details = dangerResult.Details
                };
                return (riskContext, dangerResult);
            }
            return (null, null);
        }

        // 回退：使用 IDestructiveCommandDetector
        if (_destructiveCommandDetector is not null)
        {
            var shellCommand = ShellCommand.Parse(command);
            var result = _destructiveCommandDetector.Detect(shellCommand);

            if (!result.IsDestructive)
                return (null, null);

            var riskContext = new CommandRiskContext
            {
                ToolName = toolName,
                ShellCommand = shellCommand,
                Risks = result.Risks,
                Details = result.Details
            };
            // 从 CommandRisk 推断 DangerLevel
            var primaryRiskForInfer = SelectPrimaryRisk(result.Risks) ?? CommandRisk.None;
            var inferredLevel = DangerousCommandCatalog.InferLevel(primaryRiskForInfer);
            var dangerResult = new DangerClassificationResult(inferredLevel, primaryRiskForInfer, result.Details);
            return (riskContext, dangerResult);
        }

        // 降级检测 — 无检测器时使用配置中的危险命令模式
        if (PermissionCheckContext.IsDangerousCommand(command, _dangerousCommandPatterns))
        {
            var riskContext = new CommandRiskContext
            {
                ToolName = toolName,
                Risks = [CommandRisk.DataModification],
                Details = "配置模式检测到危险命令"
            };
            var dangerResult = new DangerClassificationResult(CommandDangerLevel.Dangerous, CommandRisk.DataModification, "配置模式检测到危险命令");
            return (riskContext, dangerResult);
        }

        return (null, null);
    }

    /// <summary>
    /// 降级检测用的危险命令模式 — 从 PermissionConfig 获取
    /// </summary>
    private static readonly List<DangerousCommandPattern> _dangerousCommandPatterns = PermissionConfig.CreateDefault().DangerousCommandPatterns;

    /// <summary>
    /// 处理检测到的风险 — 根据 CommandDangerLevel 做决策
    /// Dangerous: 任何模式下都直接拒绝不提示
    /// Execution（红色ask/不可撤回）: Auto拒绝/Ask确认/Plan拒绝
    /// LightValidation（绿色ask/可撤回）: Auto拒绝/Ask确认/Plan放行(只读性质)
    /// </summary>
    private void HandleRisks(PermissionCheckContext context, CommandRiskContext riskContext, DangerClassificationResult? dangerResult)
    {
        // 按优先级选择最关键的风险
        var primaryRisk = SelectPrimaryRisk(riskContext.Risks);
        var handler = primaryRisk is not null ? _riskHandlers.GetValueOrDefault(primaryRisk.Value) : null;

        // 获取危险等级：优先使用 dangerResult，否则从 CommandRisk 推断
        var level = dangerResult?.Level ?? DangerousCommandCatalog.InferLevel(primaryRisk ?? CommandRisk.None);

        // Dangerous 级 — 任何模式下都直接拒绝不提示
        if (level == CommandDangerLevel.Dangerous)
        {
            context.Result = ToolPermissionCheckResult.Rejected("此操作被禁止");
            return;
        }

        switch (context.CurrentMode)
        {
            case PermissionMode.Auto:
                var rejection = handler is not null
                    ? handler.BuildRejectionMessage(riskContext)
                    : $"危险操作已被阻止（{riskContext.Details}）。如确需执行请切换到 Ask 模式确认";
                context.Result = ToolPermissionCheckResult.Rejected(rejection);
                break;

            case PermissionMode.Ask:
                // LightValidation（绿色ask/可撤回）和 Execution（红色ask/不可撤回）都需确认
                // 颜色区分由 IPermissionConfirmationHandler 根据 DangerLevel 实现
                var levelTag = level == CommandDangerLevel.LightValidation ? "[轻校验]" : "[执行]";
                var confirmation = handler is not null
                    ? $"{levelTag} {handler.BuildConfirmationMessage(riskContext)}"
                    : $"{levelTag} 工具 '{context.ToolName}' 请求执行操作（{riskContext.Details}）。是否批准？";
                context.Result = ToolPermissionCheckResult.PendingConfirmation(confirmation);
                break;

            case PermissionMode.Plan:
                if (level == CommandDangerLevel.LightValidation)
                {
                    // LightValidation 在 Plan 模式下放行（可撤回操作，类似只读）
                    return;
                }
                context.Result = ToolPermissionCheckResult.Rejected(
                    $"Plan 模式下禁止不可撤回操作（{riskContext.Details}）");
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
