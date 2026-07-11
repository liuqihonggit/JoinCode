
namespace Core.Planning;

/// <summary>
/// 计划模式管理器实现
/// </summary>
[Register]
public sealed partial class PlanModeManager : IPlanModeManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, PlanState> _plans = new();
    private readonly List<PlanState> _planHistory = new();
    private readonly SemaphoreSlim _historyLock;
    private readonly ITelemetryService? _telemetryService;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly ITeammateMailboxService? _mailboxService;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private int _planCounter;

    /// <summary>
    /// 对齐 TS planSlugCache: 当前 session 的 slug 缓存
    /// 同一 session 内进出 plan mode 始终使用同一文件路径
    /// </summary>
    private string? _currentSessionSlug;

    /// <summary>
    /// 进入 Plan 模式前保存的权限模式，退出时恢复
    /// 对齐 TS ToolPermissionContext.prePlanMode
    /// </summary>
    private PermissionMode? _prePlanMode;

    /// <summary>
    /// 对齐 TS strippedDangerousRules: 进入Plan时剥离的危险权限规则数量，退出时恢复
    /// </summary>
    private int _strippedRuleCount;

    /// <summary>
    /// 对齐 TS hasExitedPlanMode: 本次会话是否退出过plan模式
    /// 用于检测重入plan模式时提供引导（plan_mode_reentry attachment）
    /// </summary>
    private bool _hasExitedPlanMode;

    /// <summary>
    /// 对齐 TS needsPlanModeExitAttachment: 退出plan后是否需要发送一次性通知
    /// 消费方读取后应清除标志
    /// </summary>
    private bool _needsPlanModeExitAttachment;

    /// <summary>
    /// 待审批请求的等待字典 — 对齐 TS awaitingLeaderApproval
    /// key: requestId, value: TaskCompletionSource（审批响应到达时 SetResult）
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PlanApprovalResponseMessage>> _pendingApprovals = new();

    public PlanModeManager(IFileSystem fs, IClockService clock, ITelemetryService? telemetryService = null, IToolPermissionManager? permissionManager = null, ITeammateMailboxService? mailboxService = null, ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _historyLock = new SemaphoreSlim(1, 1);
        _telemetryService = telemetryService;
        _permissionManager = permissionManager;
        _mailboxService = mailboxService;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
    }

    /// <summary>
    /// 当前是否处于计划模式
    /// </summary>
    public bool IsInPlanMode => CurrentPlanId != null && _plans.TryGetValue(CurrentPlanId, out var plan) && plan.IsInPlanMode;

    /// <summary>
    /// 当前计划ID
    /// </summary>
    public string? CurrentPlanId { get; private set; }

    /// <summary>
    /// 对齐 TS hasExitedPlanModeInSession: 本次会话是否退出过plan模式
    /// 用于检测重入plan模式时提供引导
    /// </summary>
    public bool HasExitedPlanMode => _hasExitedPlanMode;

    /// <summary>
    /// 对齐 TS needsPlanModeExitAttachment: 退出plan后是否需要发送一次性通知
    /// 消费方读取后应调用 ClearPlanModeExitAttachment() 清除标志
    /// </summary>
    public bool NeedsPlanModeExitAttachment => _needsPlanModeExitAttachment;

    /// <summary>
    /// 对齐 TS setNeedsPlanModeExitAttachment(false): 清除退出通知标志
    /// 消费方发送完plan_mode_exit通知后调用
    /// </summary>
    public void ClearPlanModeExitAttachment() => _needsPlanModeExitAttachment = false;

    /// <summary>
    /// 对齐 TS setHasExitedPlanMode(false): 清除已退出plan标志
    /// 消费方发送完plan_mode_reentry引导后调用
    /// </summary>
    public void ClearHasExitedPlanMode() => _hasExitedPlanMode = false;

    /// <summary>
    /// 进入计划模式
    /// </summary>
    public async Task<PlanOperationResult> EnterPlanModeAsync(
        string? description = null,
        List<PlanStepInput>? initialSteps = null,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: 禁止在 Agent 上下文中进入计划模式
        if (_subAgentContextAccessor.Current != null)
        {
            return new PlanOperationResult(false, null, "EnterPlanMode tool cannot be used in agent contexts");
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
        _currentSessionSlug ??= PlanSlugGenerator.GetOrCreateSlug(
            $"session_{Environment.CurrentManagedThreadId}_{_clock.GetUtcNow():yyyyMMddHHmmss}", _fs);

        var plan = new PlanState
        {
            PlanId = planId,
            Description = description,
            Status = PlanStatus.Draft,
            Steps = steps,
            CurrentStepIndex = 0,
            IsInPlanMode = true,
            PlanFilePath = GetPlanFilePath(_currentSessionSlug),
            CreatedAt = _clock.GetUtcNow(),
            LastUpdatedAt = _clock.GetUtcNow()
        };

        _plans[planId] = plan;
        CurrentPlanId = planId;

        // 对齐 TS handlePlanModeTransition: 进入plan时清除退出通知标志
        _needsPlanModeExitAttachment = false;

        // 对齐 TS: 保存当前权限模式并切换到 Plan 模式
        if (_permissionManager != null)
        {
            _prePlanMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            await _permissionManager.SetPermissionModeAsync(PermissionMode.Plan, cancellationToken).ConfigureAwait(false);

            // 对齐 TS: 从 Auto 模式进入 Plan 时剥离危险权限规则
            if (_prePlanMode == PermissionMode.Auto)
            {
                _strippedRuleCount = await _permissionManager.StripDangerousRulesAsync(cancellationToken).ConfigureAwait(false);
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
        // 对齐 TS validateInput: 非plan模式拒绝调用 ExitPlanMode
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            // 对齐 TS 遥测: 记录在非plan模式下调用ExitPlanMode
            _telemetryService?.RecordCount("plan.exit_called_outside_plan", description: "ExitPlanMode called outside plan mode");
            return new PlanOperationResult(false, null, "Not currently in plan mode. Enter plan mode first before exiting.");
        }

        // 对齐 TS validateInput: 检查当前权限模式必须是 Plan
        if (_permissionManager != null)
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
        var teammateContext = JoinCode.Abstractions.Interfaces.TeammateContext.Current;
        var isPlanModeRequired = teammateContext?.PlanModeRequired == true;
        if (agentContext != null && _mailboxService != null && isPlanModeRequired)
        {
            var planContent = FormatPlanAsMarkdown(plan);
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
                    MessageType = TeammateMessageTypeConstants.PlanApprovalRequest,
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

        // 添加到历史记录
        await _historyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _planHistory.Add(plan);
        }
        finally
        {
            _historyLock.Release();
        }

        // 对齐 TS: 退出时不自动写文件 — plan 文件由模型通过 FileWriteTool 写入
        // TS ExitPlanModeV2Tool.call() 仅在用户通过 CCR 编辑了 plan 时才同步写入磁盘

        // 对齐 TS getPlan(): 从磁盘读取 plan 文件内容（LLM 可能通过 FileWriteTool 修改了 plan 文件）
        var diskPlanContent = await ReadPlanFileContentAsync(plan.PlanFilePath, cancellationToken).ConfigureAwait(false);

        CurrentPlanId = null;

        // 对齐 TS: 恢复进入 Plan 模式前的权限模式
        if (_permissionManager != null && _prePlanMode.HasValue)
        {
            var restoreMode = _prePlanMode.Value;

            // 对齐 TS Auto模式断路器: 如果之前是 Auto 模式，检查是否仍可恢复
            // TS 版 isAutoModeGateEnabled: 如果断路器触发，回退到 Default 而非 Auto
            if (restoreMode == PermissionMode.Auto)
            {
                // 检查 auto mode gate 是否仍然开启
                // 如果用户在 plan 模式期间手动关闭了 auto mode，则回退到 Default
                var autoModeEnabled = await IsAutoModeGateEnabledAsync(cancellationToken).ConfigureAwait(false);
                if (!autoModeEnabled)
                {
                    restoreMode = PermissionMode.Default;
                    System.Diagnostics.Trace.WriteLine("Auto mode gate disabled during plan mode, falling back to Default mode");
                }
            }

            await _permissionManager.SetPermissionModeAsync(restoreMode, cancellationToken).ConfigureAwait(false);
            _prePlanMode = null;

            // 对齐 TS: 恢复之前剥离的危险权限规则
            if (_strippedRuleCount > 0)
            {
                await _permissionManager.RestoreDangerousRulesAsync(_strippedRuleCount, cancellationToken).ConfigureAwait(false);
                _strippedRuleCount = 0;
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
        _hasExitedPlanMode = true;
        _needsPlanModeExitAttachment = true;

        RecordPlanMetrics("exit", true);
        return new PlanOperationResult(true, plan, planFileContent: diskPlanContent);
    }

    /// <summary>
    /// 获取当前计划状态
    /// </summary>
    public Task<PlanState?> GetPlanStatusAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null)
        {
            return Task.FromResult<PlanState?>(null);
        }

        _plans.TryGetValue(CurrentPlanId, out var plan);
        return Task.FromResult(plan);
    }

    /// <summary>
    /// 添加计划步骤
    /// </summary>
    public Task<PlanOperationResult> AddStepAsync(
        string description,
        string? toolName = null,
        Dictionary<string, JsonElement>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
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

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 批准执行步骤
    /// </summary>
    public Task<PlanOperationResult> ApproveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效"));
        }

        var step = plan.Steps[stepIndex];
        if (step.Status != PlanStepStatus.Pending && step.Status != PlanStepStatus.Rejected)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤 {stepIndex} 状态为 {step.Status}，无法批准"));
        }

        step.Status = PlanStepStatus.Approved;
        step.RejectionReason = null;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 拒绝执行步骤
    /// </summary>
    public Task<PlanOperationResult> RejectStepAsync(
        int stepIndex,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效"));
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法拒绝"));
        }

        step.Status = PlanStepStatus.Rejected;
        step.RejectionReason = reason;
        plan.LastUpdatedAt = _clock.GetUtcNow();

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 执行已批准的步骤
    /// </summary>
    public Task<PlanOperationResult> ExecuteApprovedStepsAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
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

                    return Task.FromResult(new PlanOperationResult(false, plan, $"步骤 {i} 执行失败", string.Join("\n", results)));
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

        return Task.FromResult(new PlanOperationResult(true, plan, executionResult: string.Join("\n", results)));
    }

    /// <summary>
    /// 修改步骤
    /// </summary>
    public Task<PlanOperationResult> ModifyStepAsync(
        int stepIndex,
        string? newDescription = null,
        string? newToolName = null,
        Dictionary<string, JsonElement>? newParameters = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效"));
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法修改"));
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

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 删除步骤
    /// </summary>
    public Task<PlanOperationResult> RemoveStepAsync(
        int stepIndex,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
        }

        if (stepIndex < 0 || stepIndex >= plan.Steps.Count)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤索引 {stepIndex} 无效"));
        }

        var step = plan.Steps[stepIndex];
        if (step.Status == PlanStepStatus.Completed || step.Status == PlanStepStatus.Executing)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, $"步骤 {stepIndex} 已在执行或完成，无法删除"));
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

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 重新排序步骤
    /// </summary>
    public Task<PlanOperationResult> ReorderStepsAsync(
        List<int> newOrder,
        CancellationToken cancellationToken = default)
    {
        if (CurrentPlanId == null || !_plans.TryGetValue(CurrentPlanId, out var plan))
        {
            return Task.FromResult(new PlanOperationResult(false, null, "当前不在计划模式中"));
        }

        if (newOrder.Count != plan.Steps.Count)
        {
            return Task.FromResult(new PlanOperationResult(false, plan, "新顺序列表长度与步骤数不匹配"));
        }

        // 检查是否有步骤正在执行或已完成
        if (plan.Steps.Any(s => s.Status == PlanStepStatus.Executing || s.Status == PlanStepStatus.Completed))
        {
            return Task.FromResult(new PlanOperationResult(false, plan, "有步骤正在执行或已完成，无法重新排序"));
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

        return Task.FromResult(new PlanOperationResult(true, plan));
    }

    /// <summary>
    /// 获取所有计划历史
    /// </summary>
    public async Task<List<PlanState>> GetPlanHistoryAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        await _historyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = _planHistory.AsEnumerable().Reverse().Take(limit).ToList();
            return history;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _historyLock.Dispose();
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
        => _telemetryService?.RecordCount("plan.mode.count", new() { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Plan mode operation count");

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
    {
        var plansDir = PlanSlugGenerator.GetPlansDirectory();
        if (!_fs.DirectoryExists(plansDir))
        {
            return 0;
        }

        var cutoff = _clock.GetUtcNow().AddDays(-maxAgeDays);
        var cleanedCount = 0;

        try
        {
            foreach (var filePath in _fs.EnumerateFiles(plansDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var lastWriteTime = _fs.GetLastWriteTimeUtc(filePath);
                    if (lastWriteTime < cutoff)
                    {
                        var deletedPath = GetDeletedPath(filePath, _clock.GetUtcNow());
                        var deletedDir = Path.GetDirectoryName(deletedPath)!;
                        if (!_fs.DirectoryExists(deletedDir))
                        {
                            _fs.CreateDirectory(deletedDir);
                        }
                        _fs.MoveFile(filePath, deletedPath);
                        cleanedCount++;
                    }
                }
                catch (Exception ex)
                {
                    // 单个文件清理失败不影响其他文件
                    System.Diagnostics.Trace.WriteLine($"Plan file cleanup failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // 目录遍历失败不抛出
            System.Diagnostics.Trace.WriteLine($"Plan directory traversal failed: {ex.Message}");
        }

        return cleanedCount;
    }

    /// <summary>
    /// 对齐 TS clearPlanSlug(): 清除当前 session 的 slug 缓存
    /// </summary>
    public void ClearPlanSlug()
    {
        _currentSessionSlug = null;
    }

    private static string GetDeletedPath(string filePath, DateTime timestamp)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var ts = timestamp.ToString("yyyyMMddHHmmss");
        return Path.Combine(dir, ".x", $"{fileName}{ext}.{ts}.del");
    }

    /// <summary>
    /// 获取 Plan 文件路径 — 对齐 TS getPlanFilePath()
    /// 路径格式: ~/.jcc/plans/{adjective-verb-noun}.md
    /// </summary>
    private static string GetPlanFilePath(string slug)
    {
        var appDataPath = PlanSlugGenerator.GetPlansDirectory();
        return Path.Combine(appDataPath, $"{slug}.md");
    }

    /// <summary>
    /// 将 Plan 格式化为 Markdown
    /// </summary>
    private static string FormatPlanAsMarkdown(PlanState plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan: {plan.Description ?? "Untitled"}");
        sb.AppendLine();
        sb.AppendLine($"- **Plan ID**: {plan.PlanId}");
        sb.AppendLine($"- **Status**: {plan.Status}");
        sb.AppendLine($"- **Created**: {plan.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Updated**: {plan.LastUpdatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Progress**: {plan.CompletedStepsCount}/{plan.TotalSteps} ({plan.GetProgressPercentage():F1}%)");
        sb.AppendLine();

        if (plan.Steps.Count > 0)
        {
            sb.AppendLine("## Steps");
            sb.AppendLine();
            foreach (var step in plan.Steps)
            {
                var statusIcon = step.Status switch
                {
                    PlanStepStatus.Pending => "[ ]",
                    PlanStepStatus.Approved => "[~]",
                    PlanStepStatus.Rejected => "[x]",
                    PlanStepStatus.Executing => "[>]",
                    PlanStepStatus.Completed => "[✓]",
                    PlanStepStatus.Failed => "[✗]",
                    PlanStepStatus.Skipped => "[-]",
                    _ => "[?]"
                };

                var toolInfo = !string.IsNullOrEmpty(step.ToolName) ? $" (`{step.ToolName}`)" : "";
                sb.AppendLine($"- {statusIcon} {step.Description}{toolInfo}");

                if (!string.IsNullOrEmpty(step.ExecutionResult))
                {
                    sb.AppendLine($"  - Result: {step.ExecutionResult}");
                }
                if (!string.IsNullOrEmpty(step.RejectionReason))
                {
                    sb.AppendLine($"  - Reason: {step.RejectionReason}");
                }
            }
        }

        return sb.ToString();
    }

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
            if (_permissionManager is not null && _strippedRuleCount > 0)
            {
                await _permissionManager.RestoreDangerousRulesAsync(_strippedRuleCount, cancellationToken).ConfigureAwait(false);
                _strippedRuleCount = 0;
            }

            // 退出 PlanMode
            var currentPlan = _plans.Values.FirstOrDefault(p => p.IsInPlanMode);
            if (currentPlan is not null)
            {
                currentPlan.IsInPlanMode = false;
                currentPlan.LastUpdatedAt = _clock.GetUtcNow();
                _hasExitedPlanMode = true;
                _needsPlanModeExitAttachment = true;

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
}
