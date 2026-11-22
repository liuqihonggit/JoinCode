namespace McpToolRegistry;

/// <summary>
/// 权限感知的工具执行器 — 通过标准中间件管道执行工具调用
/// 管道: 参数修复 → 必填参数校验 → Schema校验 → Agent限制 → 权限检查 → 远程策略 → FeatureFlag → 执行
/// 权限模式统一从 IToolPermissionManager 获取，不再自行维护
/// </summary>
[Register(typeof(IToolExecutionGateway), ServiceLifetime.Singleton)]
public sealed partial class PermissionAwareToolExecutor : ServiceEntity, IToolExecutionGateway {
    private readonly IToolRegistry _toolRegistry;
    private readonly ITelemetryService? _telemetryService;
    private readonly IToolPermissionManager _permissionManager;
    private readonly IPermissionConfirmationHandler? _confirmationHandler;
    private readonly IPendingConfirmationQueue? _pendingConfirmations;
    private readonly ILogger<PermissionAwareToolExecutor> _logger;
    private readonly MiddlewarePipeline<ToolExecutionContext> _pipeline;

    /// <summary>
    /// 工具执行完成事件 — 无论成功或失败都会触发，用于遥测和诊断
    /// </summary>
    public event EventHandler<ToolExecutionCompletedEventArgs>? ToolExecutionCompleted;

    /// <summary>
    /// 构造权限感知工具执行器 — 绑定工具注册表、中间件管道与权限管理器
    /// </summary>
    /// <param name="toolRegistry">工具注册表，用于按名查找工具处理器</param>
    /// <param name="pipeline">中间件管道，按序执行参数修复、校验、权限检查等中间件</param>
    /// <param name="permissionManager">工具权限管理器，提供当前权限模式</param>
    /// <param name="telemetryService">可选遥测服务，用于记录工具执行跨度</param>
    /// <param name="confirmationHandler">可选权限确认处理器，用于处理待确认权限</param>
    /// <param name="pendingConfirmations">可选待确认队列，登记在途确认事项供前台批量展示</param>
    /// <param name="logger">日志记录器</param>
    public PermissionAwareToolExecutor(
        IToolRegistry toolRegistry,
        MiddlewarePipeline<ToolExecutionContext> pipeline,
        IToolPermissionManager permissionManager,
        ITelemetryService? telemetryService = null,
        IPermissionConfirmationHandler? confirmationHandler = null,
        IPendingConfirmationQueue? pendingConfirmations = null,
        ILogger<PermissionAwareToolExecutor>? logger = null) {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _telemetryService = telemetryService;
        _confirmationHandler = confirmationHandler;
        _pendingConfirmations = pendingConfirmations;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行工具调用 — 对齐 TS streamedCheckPermissionsAndCallTool
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        var handler = await _toolRegistry.GetToolAsync(toolName, cancellationToken).ConfigureAwait(false);

        if (handler is null) {
            _logger.LogWarning(L.T(StringKey.ToolNotFoundLog, toolName));
            return ToolErrorJsonBuilder.ToToolResult(ToolErrorType.ToolNotFound, $"Tool '{toolName}' not found.", toolName, ToolErrorCode.ToolNotFoundInRegistry);
        }

        await using var span = _telemetryService?.StartSpan($"tool.{toolName}", TelemetrySpanKind.Client);
        if (span is not null) {
            span.SetTag("tool.name", toolName);
        }

        var executionEntity = ToolExecutionEntityFactory.Create(
            toolName, toolUseId: null, spanId: span?.SpanId, arguments: arguments);
        ToolExecutionEntity.Registry.TransitionLifecycle(executionEntity.ObjectId, EntityLifecycle.Active);
        executionEntity.StartedAt = DateTime.UtcNow;
        span?.SetTag("entity.object_id", executionEntity.UniqueId);

        var currentMode = ResolvePermissionMode(await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false));
        var agentContext = SubAgentContext.Current;

        var context = new ToolExecutionContext {
            ToolName = toolName,
            Arguments = arguments,
            Handler = handler,
            OnProgress = onProgress,
            AgentMode = currentMode,
            AllowedTools = ToToolSet(agentContext?.AllowedTools),
            DeniedTools = ToToolSet(agentContext?.DeniedTools),
            Span = span,
            ExecutionEntity = executionEntity,
        };

        try {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            if (context.PermissionDecision == PermissionDecision.PendingConfirmation) {
                return await HandlePendingConfirmationAsync(toolName, arguments, handler, onProgress, currentMode, span, executionEntity, context, cancellationToken).ConfigureAwait(false);
            }

            if (context.Result is not null) {
                ApplyQuotaWarning(toolName, context.Result);
                CompleteExecutionEntity(context);
                RaiseToolExecutionCompleted(toolName, context.Result, arguments);
                return context.Result;
            }

            _logger.LogError("Tool {ToolName} pipeline completed without result", toolName);
            var noResultError = ToolErrorJsonBuilder.ToToolResult(ToolErrorType.NoResult, $"Tool '{toolName}' execution produced no result.", toolName, ToolErrorCode.ExecutionFailedNoResult);
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, noResultError, arguments);
            return noResultError;
        } catch (OperationCanceledException) {
            _logger.LogInformation(L.T(StringKey.ToolExecCancelledLog, toolName));
            span?.SetStatus(TelemetryStatusCode.Error, "Cancelled");
            ToolExecutionEntity.Registry.TransitionLifecycle(executionEntity.ObjectId, EntityLifecycle.Completed);
            executionEntity.CompletedAt = DateTime.UtcNow;
            executionEntity.IsError = true;
            var cancelledResult = ToolErrorJsonBuilder.FromCancellation(toolName);
            RaiseToolExecutionCompleted(toolName, cancelledResult, arguments, "Cancelled");
            return cancelledResult;
        } catch (Exception ex) {
            _logger.LogError(ex, L.T(StringKey.ToolExecFailedLog, toolName));
            span?.RecordException(ex);
            Diag.WriteError($"[ToolExec] Tool={toolName}", ex);
            var exceptionError = ToolErrorJsonBuilder.FromException(ex, toolName);
            ToolExecutionEntity.Registry.TransitionLifecycle(executionEntity.ObjectId, EntityLifecycle.Completed);
            executionEntity.CompletedAt = DateTime.UtcNow;
            executionEntity.IsError = true;
            RaiseToolExecutionCompleted(toolName, exceptionError, arguments, ex.Message);
            return exceptionError;
        }
    }

    /// <summary>
    /// 解析当前 Agent 的有效权限模式 — 子代理作用域优先于全局权限管理器，
    /// 否则后台子代理会继承主代理的 Auto 并重新弹出 ask（TASK105 D2）。
    /// </summary>
    private static PermissionMode ResolvePermissionMode(PermissionMode globalMode) {
        var scoped = SubAgentContext.Current?.PermissionMode;
        if (scoped is null)
            return globalMode;

        return PermissionModeExtensions.FromValue(scoped) ?? globalMode;
    }

    /// <summary>把可空字符串集合投影为 O(1) 查找集合；null/空集合统一返回空集合（表示无该层限制）。</summary>
    private static IReadOnlySet<string> ToToolSet(IEnumerable<string>? tools) {
        if (tools is null)
            return FrozenSet<string>.Empty;

        var set = tools.Where(static t => !string.IsNullOrWhiteSpace(t))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        return set.Count > 0 ? set : FrozenSet<string>.Empty;
    }

    /// <summary>
    /// 处理权限待确认 — 调用 IPermissionConfirmationHandler.Confirm,用户允许则重新执行管道
    /// 确认逻辑统一在此处,QueryEngine 子代理与主代理路径都经过这里
    /// </summary>
    internal async Task<ToolResult> HandlePendingConfirmationAsync(
        string toolName,
        Dictionary<string, JsonElement> arguments,
        IToolHandler handler,
        ToolProgressCallback? onProgress,
        PermissionMode currentMode,
        ITelemetrySpan? span,
        ToolExecutionEntity executionEntity,
        ToolExecutionContext context,
        CancellationToken cancellationToken) {
        var prompt = context.PermissionConfirmationPrompt ?? "需要确认";

        // 无人值守（含 autonomous 别名）没有宿主：只登记、立即 Defer，绝不等待前台。
        // 不变量：后台任务不允许因权限确认而阻塞，也不允许弹模态框。
        if (currentMode == PermissionMode.Unattended) {
            var deferredTicket = _pendingConfirmations?.Enqueue(SubAgentContext.Current?.AgentId, toolName, prompt, arguments);
            ResolvePendingTicket(deferredTicket, PendingConfirmationDecision.Defer);

            _logger.LogWarning("无人值守模式登记待确认并拒绝本轮执行: {ToolName}", toolName);
            var deferredError = ToolErrorJsonBuilder.ToToolResult(
                ToolErrorType.PermissionRequired,
                $"工具 '{toolName}' 需要确认，但当前是无人值守/后台执行：已登记待确认事项，本轮不执行。请在前台回合确认后在交互模式下重试。",
                toolName,
                ToolErrorCode.PermissionRequiredNoHandler);
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, deferredError, arguments);
            return deferredError;
        }

        if (_confirmationHandler is null) {
            _logger.LogWarning("无确认处理器,工具权限待确认作为拒绝返回: {ToolName}", toolName);
            var noHandlerError = context.Result ?? ToolErrorJsonBuilder.ToToolResult(ToolErrorType.PermissionRequired, $"工具 '{toolName}' 需要确认但无确认处理器", toolName, ToolErrorCode.PermissionRequiredNoHandler);
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, noHandlerError, arguments);
            return noHandlerError;
        }

        var pendingTicket = _pendingConfirmations?.Enqueue(SubAgentContext.Current?.AgentId, toolName, prompt, arguments);
        // TASK069-P5 — 带上触发原因规则内容（当前仅 WebFetch 的 domain:hostname 有结构化来源），
        // 供对话框渲染「触发原因 + /permissions 引导」；无来源时为 null，渲染层整区折叠
        PermissionConfirmResult confirmResult;
        try {
            confirmResult = await _confirmationHandler.ConfirmAsync(toolName, prompt, context.PermissionRuleContent, cancellationToken).ConfigureAwait(false);
        } catch {
            ResolvePendingTicket(pendingTicket, PendingConfirmationDecision.Defer);
            throw;
        }

        var action = confirmResult.Action;
        ResolvePendingTicket(pendingTicket, action switch {
            PermissionConfirmAction.Allow or PermissionConfirmAction.AlwaysAllow => PendingConfirmationDecision.Approve,
            _ => PendingConfirmationDecision.Deny
        });

        if (action != PermissionConfirmAction.Allow && action != PermissionConfirmAction.AlwaysAllow) {
            _logger.LogWarning("用户拒绝工具执行: {ToolName}", toolName);
            // TASK069-P3 — 拒绝时带上用户按 Tab 填写的说明（对齐基准 REJECT_MESSAGE_WITH_REASON_PREFIX）：
            // 只说「被拒绝」模型只能干等，带上原话它才知道该换哪种做法。无说明时保持原文案（现状零变化）。
            var deniedReason = confirmResult.Feedback is null
                ? $"用户拒绝工具执行: {toolName}"
                : $"用户拒绝工具执行: {toolName}" + Environment.NewLine + $"用户对后续做法的说明: {confirmResult.Feedback}";
            var deniedError = ToolErrorJsonBuilder.ToToolResult(ToolErrorType.PermissionDenied, deniedReason, toolName, ToolErrorCode.PermissionDeniedByUser);
            CompleteExecutionEntity(context);
            RaiseToolExecutionCompleted(toolName, deniedError, arguments);
            return deniedError;
        }

        // TASK070 P1/DSG046 — AlwaysAllow 持久化（对齐 TS persistPermissionUpdate）：
        // Shell 类工具（带 command 参数）→ 前缀规则 "Bash(command:*)"；其他 → 工具级 "Tool"。
        // 失败仅记日志（ApproveToolPermanentlyAsync 内吞）— 临时批准已兜底本回合。
        if (action == PermissionConfirmAction.AlwaysAllow) {
            var command = PermissionRuleParser.ExtractCommand(arguments);
            var ruleContent = command is not null ? $"{command}:*" : null;
            await _permissionManager.ApproveToolPermanentlyAsync(toolName, ruleContent, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("用户确认允许工具执行: {ToolName}", toolName);

        var retryContext = new ToolExecutionContext {
            ToolName = toolName,
            Arguments = arguments,
            Handler = handler,
            OnProgress = onProgress,
            AgentMode = currentMode,
            AllowedTools = context.AllowedTools,
            DeniedTools = context.DeniedTools,
            Span = span,
            ExecutionEntity = executionEntity,
        };

        await _pipeline.ExecuteAsync(retryContext, cancellationToken).ConfigureAwait(false);

        if (retryContext.Result is not null) {
            AppendAcceptFeedback(retryContext.Result, confirmResult.Feedback);
            ApplyQuotaWarning(toolName, retryContext.Result);
            CompleteExecutionEntity(retryContext);
            RaiseToolExecutionCompleted(toolName, retryContext.Result, arguments);
            return retryContext.Result;
        }

        _logger.LogError("Tool {ToolName} retry pipeline completed without result", toolName);
        var retryNoResultError = ToolErrorJsonBuilder.ToToolResult(ToolErrorType.NoResult, $"Tool '{toolName}' execution produced no result.", toolName, ToolErrorCode.ExecutionFailedNoResult);
        CompleteExecutionEntity(retryContext);
        RaiseToolExecutionCompleted(toolName, retryNoResultError, arguments);
        return retryNoResultError;
    }

    /// <summary>结算待确认票据 — 无队列/无票据时静默跳过；票据只结算一次。</summary>
    private void ResolvePendingTicket(PendingConfirmationItem? ticket, PendingConfirmationDecision decision) {
        if (ticket is null || _pendingConfirmations is null)
            return;

        _pendingConfirmations.TryResolve(ticket.Id, decision);
    }

    /// <summary>
    /// 允许时把用户补充说明追加为工具结果末尾的文本内容（TASK069-P3 — 对齐基准
    /// <c>toolExecution.ts:1417-1429</c>：允许附带的 acceptFeedback 作为独立 text content block
    /// 追加在工具结果之后，模型按说明继续做）。无说明时不改结果（现状零变化）。
    /// </summary>
    /// <param name="result">工具执行结果。</param>
    /// <param name="feedback">用户补充说明（null 表示未填写）。</param>
    private static void AppendAcceptFeedback(ToolResult result, string? feedback) {
        if (feedback is null) {
            return;
        }

        result.Content.Add(new ToolContent { Type = ToolContentType.Text, Text = feedback });
    }

    private void RecordPermissionDenied(string toolName) {
        if (_telemetryService is null) return;

        var counter = _telemetryService.GetCounter("tool.permission.denied", "count", "Tool permission denied count");
        counter.Add(1, new Dictionary<string, string> { ["tool"] = toolName });
    }

    private static void CompleteExecutionEntity(ToolExecutionContext context) {
        if (context.ExecutionEntity is not { } entity) return;

        entity.CompletedAt = DateTime.UtcNow;
        ToolExecutionEntity.Registry.TransitionLifecycle(entity.ObjectId, EntityLifecycle.Completed);
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
    private static void BackfillEntityMetadata(ToolExecutionEntity entity, List<EntityMetadataEntry>? metadata) {
        if (metadata is null || metadata.Count == 0) return;

        var dict = new Dictionary<string, EntityMetadataEntry>(metadata.Count, StringComparer.Ordinal);
        foreach (var m in metadata)
            dict.TryAdd(m.Key, m);

        switch (entity) {
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

    /// <summary>
    /// 记录工具调用配额并在高频时注入强烈重复性警告提示 — 不拒绝调用，仅让 AI 嫌烦主动换工具
    /// </summary>
    private static void ApplyQuotaWarning(string toolName, ToolResult result) {
        ToolQuotaService.RecordCall(toolName);
        if (!ToolQuotaService.ShouldWarn(toolName)) return;
        var warning = ToolQuotaService.GetWarningPrompt(toolName);
        result.Content.Add(new ToolContent {
            Type = ToolContentType.Text,
            Text = warning
        });
    }

    private void RaiseToolExecutionCompleted(
        string toolName,
        ToolResult? result,
        Dictionary<string, JsonElement> arguments,
        string? errorMessage = null) {
        Supervise(toolName, result, arguments);
        ToolExecutionCompleted?.Invoke(this, new ToolExecutionCompletedEventArgs {
            ToolName = toolName,
            IsError = result?.IsError ?? true,
            ErrorMessage = errorMessage ?? result?.Content?.FirstOrDefault(c => c.Type == ToolContentType.Text)?.Text,
            Duration = TimeSpan.Zero,
            Arguments = arguments
        });
    }

    /// <summary>
    /// 执行后监督：结果里出现凭据特征时登记待复核票据（立即 Defer，不阻塞本轮）。
    /// 结果已经返回给模型，监督只做可见性与后续处置提醒，不改变工具结果。
    /// </summary>
    private void Supervise(string toolName, ToolResult? result, Dictionary<string, JsonElement> arguments) {
        if (result is null) {
            return;
        }
        var reason = ToolExecutionSupervisor.Inspect(result);
        if (reason is null) {
            return;
        }

        var ticket = _pendingConfirmations?.Enqueue(SubAgentContext.Current?.AgentId, toolName, reason, arguments);
        if (ticket is not null && _pendingConfirmations is not null) {
            _pendingConfirmations.TryResolve(ticket.Id, PendingConfirmationDecision.Defer);
        }
        _logger.LogWarning("执行后监督发现疑似凭据输出: {ToolName}", toolName);
        Diag.WriteLine($"[TOOL-SUPERVISOR] secret-in-output tool={toolName} ticket={ticket?.Id ?? "(none)"}");
    }
}

/// <summary>
/// 工具执行完成事件参数
/// </summary>
public sealed class ToolExecutionCompletedEventArgs : EventArgs {
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