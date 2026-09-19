
namespace Core.Planning;

/// <summary>
/// 计划历史 Actor 命令类型 — 串行化 _planHistory 访问，消除显式锁 — TASK001
/// </summary>
public abstract record PlanModeCommand;

/// <summary>退出计划模式 — 锁内部分（_planHistory.Add）走 Actor 串行化</summary>
public sealed record ExitPlanModeCmd(
    PlanState Plan,
    TaskCompletionSource Reply) : PlanModeCommand;

/// <summary>获取计划历史 — 对应 GetPlanHistoryAsync</summary>
public sealed record GetPlanHistoryCmd(
    int Limit,
    TaskCompletionSource<List<PlanState>> Reply) : PlanModeCommand;

/// <summary>
/// 计划模式管理器实现
/// </summary>
[Register(typeof(IPlanModeManager), ServiceLifetime.Singleton)]
public sealed partial class PlanModeManager : IPlanModeManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PlanState> _plans = new();
    private readonly List<PlanState> _planHistory = new();
    private readonly PlanHistoryActor _actor;
    private readonly ITelemetryService? _telemetryService;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly ILogger<PlanModeManager>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly PlanFileStore _fileStore;
    private int _planCounter;
    private int _disposed;

    private readonly SessionPlanState _fallbackState = new();
    private const string PlanStateKey = "plan_state";

    private SessionPlanState CurrentSessionState()
    {
        var sessionId = SessionContext.Current;
        if (sessionId is null) return _fallbackState;
        var scope = SessionRouter.GetOrCreateScope(sessionId.Value);
        var state = scope.Cache.Get<SessionPlanState>(PlanStateKey);
        if (state is null)
        {
            state = new SessionPlanState();
            scope.Cache.Set(PlanStateKey, state);
        }
        return state;
    }

    private sealed class SessionPlanState
    {
        public string? CurrentSessionSlug { get; set; }
        public PermissionMode? PrePlanMode { get; set; }
        public int StrippedRuleCount { get; set; }
        public bool HasExitedPlanMode { get; set; }
        public bool NeedsPlanModeExitAttachment { get; set; }
        public string? CurrentPlanId { get; set; }
    }

    /// <summary>
    /// 待审批请求的等待字典 — 对齐 TS awaitingLeaderApproval
    /// key: requestId, value: TaskCompletionSource（审批响应到达时 SetResult）
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PlanApprovalResponseMessage>> _pendingApprovals = new();

    /// <summary>
    /// 初始化计划模式管理器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="clock">时钟服务</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="permissionManager">权限管理器（可选，用于进入/退出 Plan 模式时切换权限）</param>
    /// <param name="mailboxService">队友邮箱服务（可选，用于 teammate 审批流程）</param>
    /// <param name="subAgentContextAccessor">子 Agent 上下文访问器（可选）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public PlanModeManager(IFileSystem fs, IClockService clock, ITelemetryService? telemetryService = null, IToolPermissionManager? permissionManager = null, ITeammateMailboxService? mailboxService = null, ISubAgentContextAccessor? subAgentContextAccessor = null, ILogger<PlanModeManager>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _telemetryService = telemetryService;
        _permissionManager = permissionManager;
        _mailboxService = mailboxService;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _logger = logger;
        _fileStore = new PlanFileStore(fs, clock, logger);
        _actor = new PlanHistoryActor(this, logger);
    }

    /// <summary>
    /// 当前是否处于计划模式
    /// </summary>
    public bool IsInPlanMode => CurrentPlanId != null && _plans.TryGetValue(CurrentPlanId, out var plan) && plan.IsInPlanMode;

    /// <summary>
    /// 当前计划ID
    /// </summary>
    public string? CurrentPlanId
    {
        get => CurrentSessionState().CurrentPlanId;
        private set => CurrentSessionState().CurrentPlanId = value;
    }

    /// <summary>
    /// 对齐 TS hasExitedPlanModeInSession: 本次会话是否退出过plan模式
    /// 用于检测重入plan模式时提供引导
    /// </summary>
    public bool HasExitedPlanMode => CurrentSessionState().HasExitedPlanMode;

    /// <summary>
    /// 对齐 TS needsPlanModeExitAttachment: 退出plan后是否需要发送一次性通知
    /// 消费方读取后应调用 ClearPlanModeExitAttachment() 清除标志
    /// </summary>
    public bool NeedsPlanModeExitAttachment => CurrentSessionState().NeedsPlanModeExitAttachment;

    /// <summary>
    /// 对齐 TS setNeedsPlanModeExitAttachment(false): 清除退出通知标志
    /// 消费方发送完plan_mode_exit通知后调用
    /// </summary>
    public void ClearPlanModeExitAttachment() => CurrentSessionState().NeedsPlanModeExitAttachment = false;

    /// <summary>
    /// 对齐 TS setHasExitedPlanMode(false): 清除已退出plan标志
    /// 消费方发送完plan_mode_reentry引导后调用
    /// </summary>
    public void ClearHasExitedPlanMode() => CurrentSessionState().HasExitedPlanMode = false;

    /// <summary>
    /// 进入计划模式
    /// </summary>
    public async Task<PlanOperationResult> EnterPlanModeAsync(
        string? description = null,
        List<PlanStepInput>? initialSteps = null,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 禁止在 Agent 上下文中进入计划模式
        if (_subAgentContextAccessor.Current != null)
        {
            return new PlanOperationResult(false, null, $"{PlanToolNameEnumConstants.EnterPlanMode} tool cannot be used in agent contexts");
        }

        // 如果已经在计划模式，先退出当前计划
        if (IsInPlanMode && CurrentPlanId != null)
        {
            await ExitPlanModeAsync(false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var planId = GeneratePlanId();
        var steps = initialSteps?.Select((input, index) => new PlanStep
        {
            Index = index,
            Description = input.Description,
            ToolName = input.ToolName,
            Parameters = input.Parameters,
            Status = PlanStepStatus.Pending
        }).ToList() ?? new List<PlanStep>();

        // 对齐 TS getPlanSlug(): 同一 session 内缓存 slug，保证覆盖同一文件
        // 用 AsyncFlowIdentity.FlowId 替代 Environment.CurrentManagedThreadId — async 流跨 await 自动流转,不随线程池调度变化
        var sessionState = CurrentSessionState();
        var flowId = LockRegistry.EnsureFlowRegistered();
        sessionState.CurrentSessionSlug ??= PlanSlugGenerator.GetOrCreateSlug(
            $"session_{flowId}_{_clock.GetUtcNow():yyyyMMddHHmmss}", _fs, _logger);

        var plan = new PlanState
        {
            PlanId = planId,
            Description = description,
            Status = PlanStatus.Draft,
            Steps = steps,
            CurrentStepIndex = 0,
            IsInPlanMode = true,
            PlanFilePath = PlanFileStore.GetPlanFilePath(sessionState.CurrentSessionSlug!),
            CreatedAt = _clock.GetUtcNow(),
            LastUpdatedAt = _clock.GetUtcNow()
        };

        _plans[planId] = plan;
        CurrentPlanId = planId;

        // 对齐 TS handlePlanModeTransition: 进入plan时清除退出通知标志
        CurrentSessionState().NeedsPlanModeExitAttachment = false;

        // 跨进程持久化: 保存活跃 plan 状态到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        // 对齐 TS: 保存当前权限模式并切换到 Plan 模式
        if (_permissionManager != null)
        {
            CurrentSessionState().PrePlanMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            await _permissionManager.SetPermissionModeAsync(PermissionMode.Plan, cancellationToken).ConfigureAwait(false);

            // 对齐 TS: 从 Auto 模式进入 Plan 时剥离危险权限规则
            if (CurrentSessionState().PrePlanMode == PermissionMode.Auto)
            {
                CurrentSessionState().StrippedRuleCount = await _permissionManager.StripDangerousRulesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        RecordPlanMetrics("enter", true);
        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 退出计划模式
    /// </summary>
    public async Task<PlanOperationResult> ExitPlanModeAsync(
        bool executeRemainingSteps = false,
        AllowedPrompt[]? allowedPrompts = null,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        // 对齐 TS validateInput: 非plan模式拒绝调用 ExitPlanMode
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            // 对齐 TS 遥测: 记录在非plan模式下调用ExitPlanMode
            _telemetryService?.RecordCount("plan.exit_called_outside_plan", description: $"{PlanToolNameEnumConstants.ExitPlanMode} called outside plan mode");
            return new PlanOperationResult(false, null, "Not currently in plan mode. Enter plan mode first before exiting.");
        }

        // 对齐 TS validateInput: 检查当前权限模式必须是 Plan
        // 跨进程持久化场景: MCP 工具调用是独立进程，权限模式不保持，跳过检查
        if (_permissionManager != null && !TestEnvironmentDetector.IsNonInteractive)
        {
            var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (currentMode != PermissionMode.Plan)
            {
                return new PlanOperationResult(false, null, "Current permission mode is not plan. Cannot exit plan mode.");
            }
        }

        // 对齐 TS ExitPlanModeV2Tool: teammate 审批流程
        // TS 条件: isTeammate() && isPlanModeRequired()
        // 只有 planModeRequired 的 teammate 才走审批，自愿进入 PlanMode 的 teammate 直接本地退出
        var agentContext = _subAgentContextAccessor.Current;
        var isPlanModeRequired = agentContext?.TeammateMeta?.PlanModeRequired == true;
        if (agentContext != null && _mailboxService != null && isPlanModeRequired)
        {
            var planContent = PlanFileStore.FormatPlanAsMarkdown(plan);
            var requestId = $"plan_approval_{agentContext.AgentId}_{_clock.GetUtcNow():yyyyMMddHHmmss}";

            // 构建审批请求消息 — 对齐 TS PlanApprovalRequestMessageSchema
            var requestMessage = new PlanApprovalRequestMessage
            {
                From = agentContext.AgentId,
                Timestamp = _clock.GetUtcNow().ToString("o"),
                PlanFilePath = plan.PlanFilePath ?? "",
                PlanContent = planContent,
                RequestId = requestId
            };

            // 注册等待 — 对齐 TS setAwaitingPlanApproval
            var tcs = new TaskCompletionSource<PlanApprovalResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingApprovals[requestId] = tcs;

            try
            {
                // 发送审批请求
                await _mailboxService.SendAsync(new MailboxSendRequest
                {
                    FromAgentId = agentContext.AgentId,
                    ToAgentId = "team-lead",
                    MessageType = TeammateMessageTypeEnumConstants.PlanApprovalRequest,
                    Content = JsonSerializer.Serialize(requestMessage, PlanJsonContext.Default.PlanApprovalRequestMessage),
                    SessionId = agentContext.SessionId ?? ""
                }, cancellationToken).ConfigureAwait(false);

                RecordPlanMetrics("exit_approval_requested", true);

                // 对齐 TS: 返回 awaitingLeaderApproval 状态，告知 teammate 等待审批
                return new PlanOperationResult(true, plan, "Plan approval request sent to team lead. Awaiting approval before proceeding.")
                {
                    AwaitingLeaderApproval = true,
                    ApprovalRequestId = requestId
                };
            }
            catch
            {
                // 发送失败时清理等待
                _pendingApprovals.TryRemove(requestId, out _);
                throw;
            }
        }

        // 如果需要，执行剩余步骤
        if (executeRemainingSteps)
        {
            await ExecuteApprovedStepsAsync(cancellationToken).ConfigureAwait(false);
        }

        plan.IsInPlanMode = false;
        plan.Status = plan.Status == PlanStatus.Executing ? PlanStatus.Cancelled : plan.Status;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 添加到历史记录 — 通过 Actor 串行化，消除显式锁 — TASK001
        var exitReply = new TaskCompletionSource();
        await _actor.SendAsync(new ExitPlanModeCmd(plan, exitReply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(exitReply, cancellationToken).ConfigureAwait(false);


        // 对齐 TS: 退出时不自动写文件 — plan 文件由模型通过 FileWriteTool 写入
        // TS ExitPlanModeV2Tool.call() 仅在用户通过 CCR 编辑了 plan 时才同步写入磁盘

        // 对齐 TS getPlan(): 从磁盘读取 plan 文件内容（LLM 可能通过 FileWriteTool 修改了 plan 文件）
        var diskPlanContent = await ReadPlanFileContentAsync(plan.PlanFilePath, cancellationToken).ConfigureAwait(false);

        CurrentPlanId = null;

        // 跨进程持久化: 退出 plan 模式后清除状态文件
        ClearActivePlanStateFile();

        // 对齐 TS: 恢复进入 Plan 模式前的权限模式
        var sessionState = CurrentSessionState();
        if (_permissionManager != null && sessionState.PrePlanMode.HasValue)
        {
            var restoreMode = sessionState.PrePlanMode.Value;

            // 对齐 TS Auto模式断路器: 如果之前是 Auto 模式，检查是否仍可恢复
            // TS 版 isAutoModeGateEnabled: 如果断路器触发，回退到 Default 而非 Auto
            if (restoreMode == PermissionMode.Auto)
            {
                // 检查 auto mode gate 是否仍然开启
                // 如果用户在 plan 模式期间手动关闭了 auto mode，则回退到 Default
                var autoModeEnabled = await IsAutoModeGateEnabledAsync(cancellationToken).ConfigureAwait(false);
                if (!autoModeEnabled)
                {
                    restoreMode = PermissionMode.Auto;
                    _logger?.LogWarning("计划模式期间 auto mode gate 被禁用，回退到 Auto 模式");
                }
            }

            await _permissionManager.SetPermissionModeAsync(restoreMode, cancellationToken).ConfigureAwait(false);
            sessionState.PrePlanMode = null;

            // 对齐 TS: 恢复之前剥离的危险权限规则
            if (sessionState.StrippedRuleCount > 0)
            {
                await _permissionManager.RestoreDangerousRulesAsync(sessionState.StrippedRuleCount, cancellationToken).ConfigureAwait(false);
                sessionState.StrippedRuleCount = 0;
            }
        }

        // 对齐 TS allowedPrompts: 退出plan后注册语义级Bash权限
        // 允许LLM在退出plan时请求特定Bash命令的自动批准（如"run tests"、"install dependencies"）
        if (allowedPrompts != null && allowedPrompts.Length > 0 && _permissionManager != null)
        {
            await Task.WhenAll(allowedPrompts.Select(ap =>
                _permissionManager.AddAllowedPromptAsync(ap.Prompt, cancellationToken))).ConfigureAwait(false);
        }

        // 对齐 TS: 设置全局状态标志
        CurrentSessionState().HasExitedPlanMode = true;
        CurrentSessionState().NeedsPlanModeExitAttachment = true;

        RecordPlanMetrics("exit", true);
        return new PlanOperationResult(true, plan, planFileContent: diskPlanContent);
    }

    /// <summary>
    /// 获取当前计划状态
    /// </summary>
    public async Task<PlanState?> GetPlanStatusAsync(CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null)
        {
            return null;
        }

        _plans.TryGetValue(CurrentPlanId, out var plan);
        return plan;
    }

    /// <summary>
    /// 添加计划步骤
    /// </summary>
    public async Task<PlanOperationResult> AddStepAsync(
        string description,
        string? toolName = null,
        Dictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        var step = new PlanStep
        {
            Index = plan.Steps.Count,
            Description = description,
            ToolName = toolName,
            Parameters = parameters,
            Status = PlanStepStatus.Pending
        };

        plan.Steps.Add(step);
        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 批准执行步骤
    /// </summary>
    public async Task<PlanOperationResult> ApproveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效");
        }

        var step = plan.Steps[stepIndex];
        if (step.Status != PlanStepStatus.Pending && step.Status != PlanStepStatus.Rejected)
        {
            return new PlanOperationResult(false, plan, $"步骤 {stepIndex} 状态为 {step.Status}，无法批准");
        }

        step.Status = PlanStepStatus.Approved;
        step.RejectionReason = null;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 拒绝执行步骤
    /// </summary>
    public async Task<PlanOperationResult> RejectStepAsync(
        int stepIndex,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效");
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法拒绝");
        }

        step.Status = PlanStepStatus.Rejected;
        step.RejectionReason = reason;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 执行已批准的步骤
    /// </summary>
    public async Task<PlanOperationResult> ExecuteApprovedStepsAsync(CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        plan.Status = PlanStatus.Executing;
        var results = new List<string>();

        for (int i = plan.CurrentStepIndex; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];

            if (step.Status == PlanStepStatus.Approved)
            {
                step.Status = PlanStepStatus.Executing;
                var startTime = _clock.GetUtcNow();

                try
                {
                    // 模拟执行步骤（实际实现中这里会调用相应的工具）
                    var result = ExecuteStep(step);

                    step.Status = PlanStepStatus.Completed;
                    step.ExecutionResult = result;
                    step.CompletedAt = _clock.GetUtcNow();
                    step.ExecutionTimeMs = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

                    results.Add($"步骤 {i}: 成功 - {result}");
                }
                catch (Exception ex)
                {
                    step.Status = PlanStepStatus.Failed;
                    step.ExecutionResult = $"错误: {ex.Message}";
                    results.Add($"步骤 {i}: 失败 - {ex.Message}");

                    plan.Status = PlanStatus.Failed;
                    plan.LastUpdatedAt = _clock.GetUtcNow();

                    // 跨进程持久化: 保存修改到文件
                    await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

                    return new PlanOperationResult(false, plan, $"步骤 {i} 执行失败", string.Join("\n", results));
                }
            }
            else if (step.Status == PlanStepStatus.Pending)
            {
                // 遇到未批准的步骤，停止执行
                break;
            }

            plan.CurrentStepIndex = i + 1;
        }

        // 检查是否所有步骤都已完成
        if (plan.Steps.All(s => s.IsCompleted || s.Status == PlanStepStatus.Rejected || s.Status == PlanStepStatus.Skipped))
        {
            plan.Status = PlanStatus.Completed;
            plan.CompletedAt = _clock.GetUtcNow();
        }

        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan, executionResult: string.Join("\n", results));
    }

    /// <summary>
    /// 修改步骤
    /// </summary>
    public async Task<PlanOperationResult> ModifyStepAsync(
        int stepIndex,
        string? newDescription = null,
        string? newToolName = null,
        Dictionary<string, JsonElement>? newParameters = null,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效");
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法修改");
        }

        if (newDescription != null)
        {
            step.Description = newDescription;
        }
        if (newToolName != null)
        {
            step.ToolName = newToolName;
        }
        if (newParameters != null)
        {
            step.Parameters = newParameters;
        }

        // 如果步骤已被拒绝，重置为待审批状态
        if (step.Status == PlanStepStatus.Rejected)
        {
            step.Status = PlanStepStatus.Pending;
            step.RejectionReason = null;
        }

        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 删除步骤
    /// </summary>
    public async Task<PlanOperationResult> RemoveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效");
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法删除");
        }

        plan.Steps.RemoveAt(stepIndex);

        // 重新索引
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            plan.Steps[i] = plan.Steps[i] with { Index = i };
        }

        // 调整当前步骤索引
        if (plan.CurrentStepIndex > stepIndex)
        {
            plan.CurrentStepIndex--;
        }

        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 重新排序步骤
    /// </summary>
    public async Task<PlanOperationResult> ReorderStepsAsync(
        List<int> newOrder,
        CancellationToken cancellationToken = default)
    {
        // 跨进程持久化: 从文件恢复活跃 plan 状态
        await LoadActivePlanStateFromFileAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return new PlanOperationResult(false, null, "当前不在计划模式中");
        }

        if (newOrder.Count != plan.Steps.Count)
        {
            return new PlanOperationResult(false, plan, "新顺序列表长度与步骤数不匹配");
        }

        // 检查是否有步骤正在执行或已完成
        if (plan.Steps.Any(s => s.Status == PlanStepStatus.Executing || s.Status == PlanStepStatus.Completed))
        {
            return new PlanOperationResult(false, plan, "有步骤正在执行或已完成，无法重新排序");
        }

        var reorderedSteps = newOrder.Select((oldIndex, newIndex) =>
        {
            var step = plan.Steps[oldIndex];
            return step with { Index = newIndex };
        }).ToList();

        plan.Steps.Clear();
        plan.Steps.AddRange(reorderedSteps);
        plan.CurrentStepIndex = 0;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        // 跨进程持久化: 保存修改到文件
        await SaveActivePlanStateToFileAsync(cancellationToken).ConfigureAwait(false);

        return new PlanOperationResult(true, plan);
    }

    /// <summary>
    /// 获取所有计划历史
    /// </summary>
    public async Task<List<PlanState>> GetPlanHistoryAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var reply = new TaskCompletionSource<List<PlanState>>();
        await _actor.SendAsync(new GetPlanHistoryCmd(limit, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }

    private string GeneratePlanId()
    {
        var counter = Interlocked.Increment(ref _planCounter);
        return $"plan_{counter:D4}_{_clock.GetUtcNow():yyyyMMddHHmmss}";
    }

    /// <summary>
    /// 对齐 TS isAutoModeGateEnabled: 检查 auto mode gate 是否仍然开启
    /// 默认返回 true，可通过环境变量 JCC_AUTO_MODE_GATE_ENABLED 控制
    /// </summary>
    private Task<bool> IsAutoModeGateEnabledAsync(CancellationToken cancellationToken = default)
    {
        // 检查环境变量或配置，默认 auto mode gate 是开启的
        var envValue = Environment.GetEnvironmentVariable(JccEnvVar.AutoModeGateEnabled.ToValue());
        var enabled = !string.Equals(envValue, "false", StringComparison.OrdinalIgnoreCase) &&
                      !string.Equals(envValue, "0", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(enabled);
    }

    private void RecordPlanMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "plan.mode.count", operation, isSuccess, "Plan mode operation count");

    private string ExecuteStep(PlanStep step)
    {
        // 实际实现中，这里会根据 ToolName 和 Parameters 调用相应的工具
        // 目前返回模拟结果
        if (!string.IsNullOrEmpty(step.ToolName))
        {
            return $"执行工具 {step.ToolName} 成功";
        }
        return "步骤执行成功";
    }

    /// <summary>
    /// 从磁盘读取 plan 文件内容 — 对齐 TS getPlan()
    /// LLM 可能通过 FileWriteTool 修改了 plan 文件，读取最新内容
    /// </summary>
    private async Task<string?> ReadPlanFileContentAsync(string? planFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(planFilePath) || !_fs.FileExists(planFilePath))
            return null;

        try
        {
            return await _fs.ReadAllTextAsync(planFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 对齐 TS cleanupOldPlanFiles(): 清理超过指定天数的旧 plan 文件
    /// 默认清理 30 天前的文件，对齐 TS cleanupPeriodDays
    /// </summary>
    public int CleanupOldPlanFiles(int maxAgeDays = 30)
        => _fileStore.CleanupOldFiles(maxAgeDays);

    /// <summary>
    /// 对齐 TS clearPlanSlug(): 清除当前 session 的 slug 缓存
    /// </summary>
    public void ClearPlanSlug()
    {
        CurrentSessionState().CurrentSessionSlug = null;
    }

    /// <summary>
    /// 从文件加载活跃 plan 状态 — 跨进程恢复 _plans 字典和 CurrentPlanId
    /// </summary>
    private async Task LoadActivePlanStateFromFileAsync(CancellationToken cancellationToken)
    {
        var state = await _fileStore.LoadActivePlanStateAsync(cancellationToken).ConfigureAwait(false);
        if (state?.Plan is not null && state.CurrentPlanId is not null)
        {
            _plans[state.CurrentPlanId] = state.Plan;
            var sessionState = CurrentSessionState();
            sessionState.CurrentPlanId = state.CurrentPlanId;
            if (state.CurrentSessionSlug is not null)
                sessionState.CurrentSessionSlug = state.CurrentSessionSlug;
        }
    }

    /// <summary>
    /// 保存活跃 plan 状态到文件 — 供下一个进程读取
    /// </summary>
    private async Task SaveActivePlanStateToFileAsync(CancellationToken cancellationToken)
    {
        var planId = CurrentPlanId;
        if (planId is null || !_plans.TryGetValue(planId, out var plan)) return;

        await _fileStore.SaveActivePlanStateAsync(planId, CurrentSessionState().CurrentSessionSlug, plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清除活跃 plan 状态文件 — 退出 plan 模式后调用
    /// </summary>
    private void ClearActivePlanStateFile()
        => _fileStore.ClearActivePlanStateFile();

    /// <summary>
    /// 处理审批响应 — 对齐 TS handlePlanApprovalResponse
    /// Leader 审批后，teammate 的 mailbox poller 调用此方法恢复权限模式
    /// </summary>
    public async Task HandlePlanApprovalResponseAsync(PlanApprovalResponseMessage response, CancellationToken cancellationToken = default)
    {
        // 查找匹配的等待请求
        if (!_pendingApprovals.TryRemove(response.RequestId, out var tcs))
        {
            _telemetryService?.RecordCount("plan.approval.orphan_response", [], "count", "Plan approval response without pending request");
            return;
        }

        // 安全校验 — 对齐 TS: 仅接受来自 team-lead 的审批响应
        if (!string.Equals(response.From, "team-lead", StringComparison.OrdinalIgnoreCase))
        {
            tcs.TrySetException(new InvalidOperationException($"Plan approval response from unauthorized source: {response.From}"));
            return;
        }

        if (response.Approved)
        {
            // 恢复权限模式 — 对齐 TS applyPermissionUpdate
            if (_permissionManager is not null && !string.IsNullOrEmpty(response.PermissionMode))
            {
                var mode = PermissionModeExtensions.FromValue(response.PermissionMode);
                if (mode is not null)
                {
                    await _permissionManager.SetPermissionModeAsync(mode.Value, cancellationToken).ConfigureAwait(false);
                }
            }

            // 恢复之前剥离的危险权限规则
            if (_permissionManager is not null && CurrentSessionState().StrippedRuleCount > 0)
            {
                await _permissionManager.RestoreDangerousRulesAsync(CurrentSessionState().StrippedRuleCount, cancellationToken).ConfigureAwait(false);
                CurrentSessionState().StrippedRuleCount = 0;
            }

            // 退出 PlanMode
            var currentPlan = _plans.Values.FirstOrDefault(p => p.IsInPlanMode);
            if (currentPlan is not null)
            {
                currentPlan.IsInPlanMode = false;
                currentPlan.LastUpdatedAt = _clock.GetUtcNow();
                CurrentSessionState().HasExitedPlanMode = true;
                CurrentSessionState().NeedsPlanModeExitAttachment = true;

                // 对齐 TS: 退出时不自动写文件 — plan 文件由模型通过 FileWriteTool 写入
            }

            RecordPlanMetrics("exit_approval_approved", true);
        }
        else
        {
            RecordPlanMetrics("exit_approval_rejected", true);
        }

        // 通知等待方
        tcs.TrySetResult(response);
    }

    /// <summary>
    /// 计划历史 Actor — 串行化 _planHistory 访问，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class PlanHistoryActor : ActorBase<PlanModeCommand, Unit>
    {
        private readonly PlanModeManager _owner;
        private readonly ILogger<PlanModeManager>? _logger;

        public PlanHistoryActor(PlanModeManager owner, ILogger<PlanModeManager>? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 PlanModeManager 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（无返回值） — 暴露 protected AskAwait 供 PlanModeManager 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override ValueTask HandleAsync(PlanModeCommand cmd, CancellationToken ct)
        {
            try
            {
                switch (cmd)
                {
                    case ExitPlanModeCmd(var plan, var reply):
                        _owner._planHistory.Add(plan);
                        reply.SetResult();
                        break;
                    case GetPlanHistoryCmd(var limit, var reply):
                        reply.SetResult(_owner._planHistory.AsEnumerable().Reverse().Take(limit).ToList());
                        break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PlanHistoryActor 命令处理异常");
                switch (cmd)
                {
                    case ExitPlanModeCmd(_, var reply): reply.SetException(ex); break;
                    case GetPlanHistoryCmd(_, var reply): reply.SetException(ex); break;
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
