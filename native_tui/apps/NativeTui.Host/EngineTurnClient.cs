namespace NativeTui.Host;

/// <summary>
/// 引擎回合客户端 — BridgeServeLoop 的进程内直连版本（ADR 0143 阶段二）：
/// 回合生命周期事件（turn_started/completed/interrupted/error）由本类统一发出，回合内部事件由
/// InProcessEventConsumer 发（共享队列 FIFO 保序：turn_started 必先于回合内部事件，终态必最后）。
/// 实现 IReplEngine 供 ReplShell 消费（主循环消费面与 bridge 形态的 BridgeClient 对齐 — DrainEvents 排空模式）。
/// 委托注入回合执行器与权限模式应用器 — 组装层绑定 SessionController/PermissionChecker，本类零 engine DI 依赖。
/// </summary>
public sealed class EngineTurnClient : IReplEngine, IDisposable {
    /// <summary>合法权限模式值清单（Error 诱导提示用，对齐 bridge 协议与 abs_guard [EnumValue]）。</summary>
    private const string ValidPermissionModes = "plan, auto, ask, bypass, unattended";

    private readonly ConcurrentQueue<BridgeEvent> _events = new();
    private readonly InProcessEventConsumer _consumer;
    private readonly TurnExecutor _executeTurn;
    private readonly PermissionModeApplier? _applyPermissionMode;
    private readonly BackgroundTaskEventPump? _backgroundPump;
    private readonly NewConversationStarter? _startNewConversation;
    private readonly Lock _turnLock = new();
    private CancellationTokenSource? _turnCts;
    private Task? _turnTask;

    /// <summary>新会话待清标记（G3 D8 — StartNewConversation 置位，下回合 Submit lock 内消费：
    /// 启动清上下文任务并串行等待完成 — 清上下文不打断在途回合，忙时调用挂起到回合结束后的下回合）。</summary>
    private bool _newConversationPending;

    /// <summary>构造函数 — 注入回合执行器（生产侧绑定 SessionController.StreamResponseAsync）、权限模式应用器（可选）、
    /// 后台任务事件泵（M8-8 可选 — 回合终态后快照下发，null = 无后台任务事件）与新会话启动器
    ///（G3 可选 — 生产侧绑定 IChatService.ClearHistoryAsync，null = 无会话概念宿主纯标记零成本）。</summary>
    public EngineTurnClient(TurnExecutor executeTurn, PermissionModeApplier? applyPermissionMode = null,
        BackgroundTaskEventPump? backgroundPump = null, NewConversationStarter? startNewConversation = null) {
        _consumer = new InProcessEventConsumer(_events);
        _executeTurn = executeTurn;
        _applyPermissionMode = applyPermissionMode;
        _backgroundPump = backgroundPump;
        _startNewConversation = startNewConversation;
    }

    /// <summary>进程内事件出口 — 组装层后台任务泵 Bind 用（M8-8；全程单实例）。</summary>
    internal IEventConsumer Consumer => _consumer;

    /// <summary>回合执行器 — 生产侧绑定 SessionController，测试侧注入假执行器模拟 LLM 流（对齐 BridgeServeLoop.TurnExecutor）。</summary>
    /// <param name="input">用户提交文本</param>
    /// <param name="consumer">回合事件消费器（执行器内触发回调实时推送事件）</param>
    /// <param name="ct">回合取消令牌（interrupt 触发）</param>
    public delegate Task TurnExecutor(string input, IEventConsumer consumer, CancellationToken ct);

    /// <summary>权限模式应用器 — 解析字符串并应用到引擎权限系统，返回实际生效值；无效或无权限系统返回 null。</summary>
    /// <param name="mode">模式字符串（plan/auto/ask/bypass/unattended）</param>
    public delegate string? PermissionModeApplier(string mode);

    /// <summary>新会话启动器（G3 D8 — Clear 语义引擎侧落点）— 清引擎对话上下文（生产侧绑定
    /// IChatService.ClearHistoryAsync，对齐引擎 /clear 斜杠命令 ClearCommand.ExecuteAsync 同一 API）；
    /// 返回 Task 由回合开始前串行等待（防清历史与回合读上下文竞态），失败降级 Error 诱导继续原会话。</summary>
    /// <returns>清上下文任务。</returns>
    public delegate Task NewConversationStarter();

    /// <summary>回合进行中（submit 后未 completed/interrupted）。</summary>
    public bool IsTurnRunning => _turnTask is { IsCompleted: false };

    /// <summary>提交回合（单参形态）— 转调图片重载（无附件路径，G3 统一入口零分叉）。</summary>
    public void Submit(string input) => Submit(input, images: null);

    /// <summary>提交回合 — 忙时拒绝（Error 诱导，对齐 bridge 语义：不打断在途回合）；空闲时
    /// G3 D8 新会话串行化（待清标记消费：先启动清上下文任务，RunTurnAsync 回合执行器前 await 完成
    /// — 防清历史与回合读上下文竞态）+ turn_started + Reset consumer + 后台执行（附件投影见
    /// <see cref="ProjectImageAttachments"/>）。</summary>
    /// <param name="input">用户提交文本（图片附件引用段由投影追加）。</param>
    /// <param name="images">图片附件（null/空 = 无附件等价单参提交）。</param>
    public void Submit(string input, IReadOnlyList<BridgeImageAttachment>? images) {
        Task? newConversationTask = null;
        lock (_turnLock) {
            if (_turnTask is { IsCompleted: false }) {
                _events.Enqueue(new BridgeEvent.Error(
                    "回合进行中，本次提交已忽略。如需提交新输入，请先按 Esc 打断当前回合。"));
                return;
            }

            // G3 D8 — 回合开始前消费新会话待清标记：启动清上下文任务（IChatService.ClearHistoryAsync），
            // RunTurnAsync 串行等待完成再进回合执行器；忙时 StartNewConversation 只置标记
            //（清上下文不打断在途回合 — 本回合结束后的下回合生效）
            if (_newConversationPending) {
                _newConversationPending = false;
                newConversationTask = _startNewConversation?.Invoke();
            }

            _turnCts?.Dispose();
            _turnCts = new CancellationTokenSource();
            var ct = _turnCts.Token;
            _consumer.Reset();
            _events.Enqueue(new BridgeEvent.TurnStarted());
            _turnTask = RunTurnAsync(ProjectImageAttachments(input, images), ct, newConversationTask);
        }
    }

    /// <summary>开始新会话（G3 D8 — Clear 语义引擎侧落点）：置待清标记，下一回合 Submit 时分派
    /// <see cref="NewConversationStarter"/>（IChatService.ClearHistoryAsync）并在回合执行器前串行等待；
    /// 忙时调用 = 标记挂起（清上下文不打断在途回合）；无启动器（null）纯标记 — 回合照常（测试桩零成本）。</summary>
    public void StartNewConversation() {
        lock (_turnLock)
            _newConversationPending = true;
    }

    /// <summary>打断在途回合（取消回合令牌；终态 turn_interrupted 由执行器的 OperationCanceledException 路径发出）。</summary>
    public void Interrupt() {
        lock (_turnLock)
            _turnCts?.Cancel();
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
    /// CancelFork/StopTeammate）。fire-and-forget：泵内部吞异常降级 + cancelled 终态 upsert 推回
    /// 面板（事件队列），调用方（主循环键位）无需等待结果 — 同步面禁止 GetAwaiter().GetResult。</summary>
    /// <param name="kind">任务类别（fork|teammate）</param>
    /// <param name="taskId">任务标识</param>
    public void StopBackgroundTask(string kind, string taskId) =>
        _ = _backgroundPump?.StopTaskAsync(kind, taskId);

    /// <summary>排空事件队列 — 主循环每 tick 调用（生命周期 + 回合内部事件同队 FIFO）。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents() {
        List<BridgeEvent>? drained = null;
        while (_events.TryDequeue(out var bridgeEvent))
            (drained ??= []).Add(bridgeEvent);
        return (IReadOnlyList<BridgeEvent>?)drained ?? [];
    }

    /// <summary>进程内引擎 — 恒存活（引擎异常经回合 Error 事件路径报告）。</summary>
    bool IReplEngine.IsAlive => true;

    /// <summary>显式接口实现 — ReplShell 消费面（bool 版本保留供 selftest 断言）。</summary>
    void IReplEngine.SetPermissionMode(string mode) => SetPermissionMode(mode);

    /// <summary>执行单个回合 — 终态翻译对齐 BridgeServeCommand.RunTurnAsync：成功 → turn_completed；
    /// 取消 → turn_interrupted；异常 → error(异常消息含诱导上下文)。终态事件入队先于回合 Task 完成（无竞态窗口）。
    /// G3 D8 — 回合执行器前串行等待新会话清理任务完成（清历史先于回合读上下文 — 防竞态；
    /// 清理失败降级 Error 诱导继续原会话，不阻断回合）。
    /// M8-8：回合终态后发后台任务快照（background_task 行晚于 turn_completed — 对齐 bridge 路径时序）。</summary>
    /// <param name="input">用户提交文本（附件引用段已由 <see cref="ProjectImageAttachments"/> 追加）。</param>
    /// <param name="ct">回合取消令牌（interrupt 触发）。</param>
    /// <param name="newConversationTask">新会话清理任务（null = 无待清 — StartNewConversation 未置位）。</param>
    private async Task RunTurnAsync(string input, CancellationToken ct, Task? newConversationTask) {
        try {
            await AwaitNewConversationAsync(newConversationTask).ConfigureAwait(false);
            await _executeTurn(input, _consumer, ct).ConfigureAwait(false);
            _events.Enqueue(new BridgeEvent.TurnCompleted());
        } catch (OperationCanceledException) {
            _events.Enqueue(new BridgeEvent.TurnInterrupted());
        } catch (Exception ex) {
            _events.Enqueue(new BridgeEvent.Error($"回合执行失败: {ex.Message}"));
        } finally {
            if (_backgroundPump is not null)
                await _backgroundPump.PublishSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>新会话清理串行等待（G3 D8 — 清历史先于回合执行器）— 失败降级：Error 诱导提示
    /// 已按原会话继续（可稍后重试新会话），不阻断本回合（清历史失败不应吞掉用户提交）。</summary>
    /// <param name="newConversationTask">清理任务（null = 无待清，直通）。</param>
    private async Task AwaitNewConversationAsync(Task? newConversationTask) {
        if (newConversationTask is null)
            return;

        try {
            await newConversationTask.ConfigureAwait(false);
        } catch (Exception ex) {
            _events.Enqueue(new BridgeEvent.Error(
                $"新会话清理失败: {ex.Message} — 本回合按原会话继续，可稍后重试新会话。"));
        }
    }

    /// <summary>附件投影为引擎回合输入（E4+G3 — 引擎提交链路 string-only 结论：
    /// SessionController.StreamResponseAsync(string)（SessionController.cs:63）→ AgentBase.CurrentInput
    /// / IChatService.StreamWithEventsAsync(string)（IChatService.cs:21）→ 落库 AddUserMessageAsync(string)
    ///（IChatContextManager.cs:25）— 用户消息无 image content block 通道（LLM 适配层 ApiMessage.ContentBlocks
    /// 仅工具结果路径消费，ChatContextManager.AddToolResultMessageAsync contentBlocks 形参），降级投影为
    /// 文本引用段附加到回合输入（模型感知图片存在 + 元数据；base64 全量内嵌会以数十万 token 计
    /// 直接击穿上下文上限，不可用）。
    /// TODO（引擎用户消息 ContentBlocks 通道）：AddUserMessageAsync/CurrentInput 支持图片块后
    /// 升级为真实多模态提交（基准 Image refs become content blocks 完整语义）。</summary>
    /// <param name="input">用户提交文本（占位已剥离 — 剥离后空文本由本投影段兜底非空）。</param>
    /// <param name="images">图片附件（null/空 = 原文透传零改动）。</param>
    /// <returns>投影后回合输入。</returns>
    private static string ProjectImageAttachments(string input, IReadOnlyList<BridgeImageAttachment>? images) {
        if (images is not { Count: > 0 })
            return input;

        var summary = string.Join("\n", images.Select(static (image, i) =>
            $"[Image #{i + 1}: {image.Filename} ({image.MediaType})]"));
        return input.Length == 0 ? summary : $"{input}\n\n{summary}";
    }

    /// <summary>释放 — 取消在途回合并等待收尾（回合 Task 内无阻塞等待，Dispose 快速返回）。</summary>
    public void Dispose() {
        CancellationTokenSource? cts;
        Task? turnTask;
        lock (_turnLock) {
            cts = _turnCts;
            turnTask = _turnTask;
            _turnCts = null;
            _turnTask = null;
        }

        cts?.Cancel();
        if (turnTask is { IsCompleted: false })
            _ = turnTask.ContinueWith(static _ => { }, TaskScheduler.Default);
        cts?.Dispose();
    }
}
