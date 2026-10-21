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
    private readonly Lock _turnLock = new();
    private CancellationTokenSource? _turnCts;
    private Task? _turnTask;

    /// <summary>构造函数 — 注入回合执行器（生产侧绑定 SessionController.StreamResponseAsync）、权限模式应用器（可选）
    /// 与后台任务事件泵（M8-8 可选 — 回合终态后快照下发，null = 无后台任务事件）。</summary>
    public EngineTurnClient(TurnExecutor executeTurn, PermissionModeApplier? applyPermissionMode = null,
        BackgroundTaskEventPump? backgroundPump = null) {
        _consumer = new InProcessEventConsumer(_events);
        _executeTurn = executeTurn;
        _applyPermissionMode = applyPermissionMode;
        _backgroundPump = backgroundPump;
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

    /// <summary>回合进行中（submit 后未 completed/interrupted）。</summary>
    public bool IsTurnRunning => _turnTask is { IsCompleted: false };

    /// <summary>提交回合 — 忙时拒绝（Error 诱导，对齐 bridge 语义：不打断在途回合）；空闲时 turn_started + Reset consumer + 后台执行。</summary>
    public void Submit(string input) {
        lock (_turnLock) {
            if (_turnTask is { IsCompleted: false }) {
                _events.Enqueue(new BridgeEvent.Error(
                    "回合进行中，本次提交已忽略。如需提交新输入，请先按 Esc 打断当前回合。"));
                return;
            }

            _turnCts?.Dispose();
            _turnCts = new CancellationTokenSource();
            var ct = _turnCts.Token;
            _consumer.Reset();
            _events.Enqueue(new BridgeEvent.TurnStarted());
            _turnTask = RunTurnAsync(input, ct);
        }
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
    /// M8-8：回合终态后发后台任务快照（background_task 行晚于 turn_completed — 对齐 bridge 路径时序）。</summary>
    private async Task RunTurnAsync(string input, CancellationToken ct) {
        try {
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
