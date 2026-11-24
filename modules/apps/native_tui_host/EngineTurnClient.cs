namespace NativeTui.Host;

/// <summary>
/// 引擎查询客户端 — BridgeServeLoop 的进程内直连版本（ADR 0143 阶段二，ADR 0165 queryId 升级）：
/// 每条查询独立持 CTS/Task/consumer，查询身份由 Submit 返回并写进该查询的全部事件；
/// 生命周期事件（turn_started/completed/interrupted/error）由本类统一发出，回合内部事件由
/// 该查询专属 InProcessEventConsumer 发（共享队列 FIFO 保序：turn_started 必先于内部事件，终态必最后）。
/// 实现 IReplEngine 供 ReplShell 消费（主循环消费面与 bridge 形态的 BridgeClient 对齐 — DrainEvents 排空模式）。
/// </summary>
public sealed class EngineTurnClient : IReplEngine, IDisposable {
    /// <summary>合法权限模式值清单（Error 诱导提示用，对齐 bridge 协议与 abs_guard [EnumValue]）。</summary>
    private const string ValidPermissionModes = "plan, auto, ask, bypass, unattended";

    private readonly ConcurrentQueue<BridgeEvent> _events = new();
    private readonly InProcessEventConsumer _bootstrapConsumer;
    private readonly TurnExecutor _executeTurn;
    private readonly PermissionModeApplier? _applyPermissionMode;
    private readonly BackgroundTaskEventPump? _backgroundPump;
    private readonly NewConversationStarter? _startNewConversation;
    // 查询状态只由 Actor 单写者修改；ConcurrentDictionary 供读侧无锁观察
    private readonly Channel<TurnCommand> _commands = Channel.CreateBounded<TurnCommand>(
        new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<string, QueryRun> _runs = new(StringComparer.Ordinal);
    /// <summary>会话级消息序号 — 每查询独立 consumer 时 MessageId 仍需递增（ADR 0165）。</summary>
    private readonly MessageIdSequence _messageIds = new();
    private int _disposed;

    /// <summary>新会话待翻页标记（G3 D8 — StartNewConversation 置位，下个 Submit 命令由回合 Actor 消费：
    /// 启动会话翻页任务并串行等待完成 — 翻页不打断在途查询，忙时调用挂起到查询结束后的下个查询）。</summary>
    private bool _newConversationPending;

    /// <summary>构造函数 — 注入回合执行器（生产侧绑定 SessionController.StreamResponseAsync）、权限模式应用器（可选）、
    /// 后台任务事件泵（M8-8 可选 — 回合终态后快照下发，null = 无后台任务事件）与新会话启动器
    ///（G3 可选 — 生产侧绑定 SessionEpochSwitcher.BeginNewAsync，null = 无会话概念宿主纯标记零成本）。</summary>
    public EngineTurnClient(TurnExecutor executeTurn, PermissionModeApplier? applyPermissionMode = null,
        BackgroundTaskEventPump? backgroundPump = null, NewConversationStarter? startNewConversation = null) {
        _executeTurn = executeTurn;
        _applyPermissionMode = applyPermissionMode;
        _backgroundPump = backgroundPump;
        _startNewConversation = startNewConversation;
        // 启动握手等无查询身份的会话级事件走独立 consumer（不污染查询事件归因）
        _bootstrapConsumer = new InProcessEventConsumer(_events, messageIds: _messageIds);
        _ = Task.Run(ProcessCommandsAsync);
    }

    /// <summary>进程内事件出口 — 组装层后台任务泵 Bind 用（M8-8；全程单实例）。</summary>
    internal IEventConsumer Consumer => _bootstrapConsumer;

    /// <summary>回合执行器 — 生产侧绑定 SessionController，测试侧注入假执行器模拟 LLM 流（对齐 BridgeServeLoop.TurnExecutor）。</summary>
    /// <param name="input">用户提交文本</param>
    /// <param name="consumer">该查询专属事件消费器（执行器内触发回调实时推送事件）</param>
    /// <param name="ct">查询取消令牌（interrupt 触发）</param>
    public delegate Task TurnExecutor(string input, IEventConsumer consumer, CancellationToken ct);

    /// <summary>权限模式应用器 — 解析字符串并应用到引擎权限系统，返回实际生效值；无效或无权限系统返回 null。</summary>
    /// <param name="mode">模式字符串（plan/auto/ask/bypass/unattended）</param>
    public delegate string? PermissionModeApplier(string mode);

    /// <summary>新会话启动器（G3 D8 / ADR 0167 — Clear 语义引擎侧落点）— 会话翻页（生产侧绑定
    /// SessionEpochSwitcher.BeginNewAsync，与引擎 /clear 斜杠命令 ClearCommand 同一落点）；
    /// 返回 Task 由查询开始前串行等待（防清历史与查询读上下文竞态），失败降级 Error 诱导继续原会话。</summary>
    /// <returns>清上下文任务。</returns>
    public delegate Task NewConversationStarter();

    /// <summary>是否存在任一未完成查询。</summary>
    public bool IsTurnRunning {
        get {
            foreach (var run in _runs.Values) {
                if (!run.Task.IsCompleted)
                    return true;
            }
            return false;
        }
    }

    /// <summary>指定查询是否仍在运行（阶段 1 前台订阅视图与测试观察点）。</summary>
    public bool IsQueryRunning(string queryId) =>
        !string.IsNullOrEmpty(queryId) && _runs.TryGetValue(queryId, out var run) && !run.Task.IsCompleted;

    /// <summary>提交查询（单参形态）— 转调完整重载（无附件路径，G3 统一入口零分叉）。</summary>
    public string Submit(string input) => Submit(input, images: null, queryId: null);

    /// <summary>提交查询（ADR 0165）— 不再因忙拒绝；返回 queryId，全部事件按此归因。</summary>
    /// <param name="input">用户提交文本（附件引用段已由 <see cref="ProjectImageAttachments"/> 追加）。</param>
    /// <param name="images">图片附件（null/空 = 无附件等价单参提交）。</param>
    /// <param name="queryId">显式查询身份；null/空时生成。</param>
    /// <returns>生效 queryId。</returns>
    public string Submit(string input, IReadOnlyList<BridgeImageAttachment>? images, string? queryId = null) {
        var effective = queryId;
        if (string.IsNullOrWhiteSpace(effective))
            effective = QueryIdFactory.New();
        Post(new TurnSubmitCommand(input, images, effective, NewCompletion()));
        return effective;
    }

    /// <summary>开始新会话（G3 D8 — Clear 语义引擎侧落点）：置待清标记，下个 Submit 时分派
    /// <see cref="NewConversationStarter"/> 并在查询执行器前串行等待；忙时调用 = 标记挂起（清上下文不打断在途查询）。</summary>
    public void StartNewConversation() {
        Post(new TurnStartNewCommand(NewCompletion()));
    }

    /// <summary>打断指定查询（null = 全部在途查询；阶段 1 由 ReplShell 按聚焦 task 传显式 id）。</summary>
    public void Interrupt(string? queryId = null) {
        Post(new TurnInterruptCommand(queryId, NewCompletion()));
    }

    /// <summary>应用权限模式 — 成功发 permission_mode_changed（实际生效值，被安全闸拦截时与请求值不同）；无效/无权限系统发 Error 诱导。</summary>
    /// <returns>是否成功应用</returns>
    public bool SetPermissionMode(string mode) {
        var effective = _applyPermissionMode?.Invoke(mode);
        if (effective is null) {
            _events.Enqueue(new BridgeEvent.Error(
                $"未知权限模式: {mode}。合法值: {ValidPermissionModes}。"));
            return false;
        }

        _events.Enqueue(new BridgeEvent.PermissionModeChanged(effective));
        return true;
    }

    /// <summary>种子事件入队 — 组装层专用（EngineSession 启动握手 session_info）；回合生命周期事件仍由本类统一管理。</summary>
    internal void Enqueue(BridgeEvent bridgeEvent) => _events.Enqueue(bridgeEvent);

    /// <summary>停止后台任务（M8-8 后续 — x 停止委托的引擎侧落点，kind: fork|teammate 分派泵
    /// CancelFork/StopTeammate）。fire-and-forget：泵内部吞异常降级 + cancelled 终态 upsert 推回面板（事件队列）。</summary>
    public void StopBackgroundTask(string kind, string taskId) =>
        _ = _backgroundPump?.StopTaskAsync(kind, taskId);

    /// <summary>排空事件队列 — 主循环每 tick 调用（生命周期 + 查询内部事件同队 FIFO）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() {
        List<BridgeEvent>? drained = null;
        while (_events.TryDequeue(out var bridgeEvent))
            (drained ??= []).Add(bridgeEvent);
        return (IReadOnlyList<BridgeEvent>?)drained ?? [];
    }

    /// <summary>进程内引擎 — 恒存活（引擎异常经查询 Error 事件路径报告）。</summary>
    bool IReplEngine.IsAlive => true;

    /// <summary>显式接口实现 — ReplShell 消费面（bool 版本保留供 selftest 断言）。</summary>
    void IReplEngine.SetPermissionMode(string mode) => SetPermissionMode(mode);

    /// <summary>执行单个查询 — 终态翻译对齐 BridgeServeCommand.RunTurnAsync：成功 → turn_completed；
    /// 取消 → turn_interrupted；异常 → error(异常消息含诱导上下文)。终态事件入队先于查询 Task 完成（无竞态窗口）。
    /// G3 D8 — 查询执行器前串行等待新会话清理任务完成；M8-8 终态后发后台任务快照。</summary>
    private async Task RunTurnAsync(string input, IEventConsumer consumer, CancellationToken ct, Task? newConversationTask, string queryId) {
        try {
            await AwaitNewConversationAsync(newConversationTask, queryId).ConfigureAwait(false);
            await _executeTurn(input, consumer, ct).ConfigureAwait(false);
            _events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = queryId });
        } catch (OperationCanceledException) {
            _events.Enqueue(new BridgeEvent.TurnInterrupted { QueryId = queryId });
        } catch (Exception ex) {
            _events.Enqueue(new BridgeEvent.Error($"查询执行失败: {ex.Message}") { QueryId = queryId });
        } finally {
            if (_backgroundPump is not null)
                await _backgroundPump.PublishSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>新会话清理串行等待（G3 D8 — 清历史先于查询执行器）— 失败降级：Error 诱导提示
    /// 已按原会话继续（可稍后重试新会话），不阻断本查询。</summary>
    private async Task AwaitNewConversationAsync(Task? newConversationTask, string queryId) {
        if (newConversationTask is null)
            return;

        try {
            await newConversationTask.ConfigureAwait(false);
        } catch (Exception ex) {
            _events.Enqueue(new BridgeEvent.Error(
                $"新会话清理失败: {ex.Message} — 本查询按原会话继续，可稍后重试新会话。") { QueryId = queryId });
        }
    }

    /// <summary>附件投影为引擎查询输入（E4+G3 — 引擎提交链路 string-only 结论：
    /// SessionController.StreamResponseAsync(string) → AgentBase.CurrentInput
    /// / IChatService.StreamWithEventsAsync(string) → 落库 AddUserMessageAsync(string)
    /// — 用户消息无 image content block 通道，降级投影为文本引用段附加到查询输入）。</summary>
    private static string ProjectImageAttachments(string input, IReadOnlyList<BridgeImageAttachment>? images) {
        if (images is not { Count: > 0 })
            return input;

        var summary = string.Join("\n", images.Select(static (image, i) =>
            $"[Image #{i + 1}: {image.Filename} ({image.MediaType})]"));
        return input.Length == 0 ? summary : $"{input}\n\n{summary}";
    }

    /// <summary>释放 — 取消全部在途查询并等待收尾（查询 Task 内无阻塞等待，Dispose 快速返回）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Post(new TurnDisposeCommand(NewCompletion()));
        _commands.Writer.TryComplete();
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>投递查询命令并等待 Actor 处理完成 — 保证 Submit/Interrupt 返回时状态已发布。</summary>
    private void Post(TurnCommand command) {
        if (!_commands.Writer.TryWrite(command)) {
            return;
        }

        try {
            command.Completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — TUI 引擎同步查询契约: 终态信号在同步路径等待
        } catch (Exception ex) {
            Debug.WriteLine($"[EngineTurnClient] 查询命令处理失败: {ex.Message}");
        }
    }

    /// <summary>查询状态 Actor — 唯一修改 _runs/_newConversationPending 的线程。</summary>
    private async Task ProcessCommandsAsync() {
        await foreach (var command in _commands.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
            try {
                Handle(command);
                command.Completion.TrySetResult();
            } catch (Exception ex) {
                command.Completion.TrySetException(ex);
            }
        }
    }

    private void Handle(TurnCommand command) {
        switch (command) {
            case TurnSubmitCommand submit:
                HandleSubmit(submit);
                break;
            case TurnInterruptCommand interrupt:
                HandleInterrupt(interrupt.QueryId);
                break;
            case TurnStartNewCommand:
                _newConversationPending = true;
                break;
            case TurnDisposeCommand:
                foreach (var run in _runs.Values)
                    run.Dispose();
                _runs.Clear();
                break;
        }
    }

    private void HandleInterrupt(string? queryId) {
        if (queryId is null) {
            foreach (var run in _runs.Values)
                run.Cancel();
            return;
        }

        if (_runs.TryGetValue(queryId, out var target))
            target.Cancel();
    }

    private void HandleSubmit(TurnSubmitCommand submit) {
        if (_runs.TryGetValue(submit.QueryId, out var existing)) {
            if (!existing.Task.IsCompleted) {
                // 诱导必须可执行：告诉 AI/用户是谁占用了身份、为什么被拒、两条出路
                _events.Enqueue(new BridgeEvent.Error(
                    $"查询 {submit.QueryId} 仍在进行中，本次提交已忽略（同一 queryId 不能并发复用）。" +
                    $"如需打断它：Interrupt(\"{submit.QueryId}\")；如需另起一条查询：提交时不指定 queryId 或换一个新 id。")
                { QueryId = submit.QueryId });
                return;
            }
            _runs.TryRemove(submit.QueryId, out _);
            existing.Dispose();
        }

        // G3 D8 — 查询开始前消费新会话待清标记（清历史先于查询执行器）
        Task? newConversationTask = null;
        if (_newConversationPending) {
            _newConversationPending = false;
            newConversationTask = _startNewConversation?.Invoke();
        }

        var cts = new CancellationTokenSource();
        // ADR 0165 — 每条查询独立 consumer，_fullResponse/_thinkingContent/_messageId 绝不跨查询共享
        var consumer = new InProcessEventConsumer(_events, queryId: submit.QueryId, messageIds: _messageIds);
        _events.Enqueue(new BridgeEvent.TurnStarted { QueryId = submit.QueryId });
        var task = RunTurnAsync(ProjectImageAttachments(submit.Input, submit.Images), consumer,
            cts.Token, newConversationTask, submit.QueryId);
        var run = new QueryRun(cts, task);
        _runs[submit.QueryId] = run;
        _ = task.ContinueWith(static (_, state) => (state as QueryRun)?.Dispose(), run,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private abstract record TurnCommand(TaskCompletionSource Completion);
    private sealed record TurnSubmitCommand(string Input, IReadOnlyList<BridgeImageAttachment>? Images, string QueryId, TaskCompletionSource Completion) : TurnCommand(Completion);
    private sealed record TurnInterruptCommand(string? QueryId, TaskCompletionSource Completion) : TurnCommand(Completion);
    private sealed record TurnStartNewCommand(TaskCompletionSource Completion) : TurnCommand(Completion);
    private sealed record TurnDisposeCommand(TaskCompletionSource Completion) : TurnCommand(Completion);

    /// <summary>单条查询运行槽（ADR 0165）— CTS/Task 与 queryId 一一对应，完成即释放 CTS。</summary>
    private sealed class QueryRun(CancellationTokenSource cts, Task task) {
        internal CancellationTokenSource Cts { get; } = cts;
        internal Task Task { get; } = task;
        private int _disposed;

        internal void Cancel() {
            try { Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        internal void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Cancel();
            Cts.Dispose();
        }
    }
}
