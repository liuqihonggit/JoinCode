namespace NativeTui.Host;

/// <summary>
/// 引擎回合客户端 — BridgeServeLoop 的进程内直连版本（ADR 0143 阶段二）：
/// 回合生命周期事件（turn_started/completed/interrupted/error）由本类统一发出，回合内部事件由
/// InProcessEventConsumer 发（共享队列 FIFO 保序：turn_started 必先于回合内部事件，终态必最后）。
/// 主循环消费面与 bridge 形态的 BridgeClient 对齐（DrainEvents 排空模式）。
/// 委托注入回合执行器与权限模式应用器 — 组装层绑定 SessionController/PermissionChecker，本类零 engine DI 依赖。
/// </summary>
public sealed class EngineTurnClient : IDisposable {
    /// <summary>合法权限模式值清单（Error 诱导提示用，对齐 bridge 协议与 abs_guard [EnumValue]）。</summary>
    private const string ValidPermissionModes = "plan, auto, ask, bypass, unattended";

    private readonly ConcurrentQueue<BridgeEvent> _events = new();
    private readonly InProcessEventConsumer _consumer;
    private readonly TurnExecutor _executeTurn;
    private readonly PermissionModeApplier? _applyPermissionMode;
    private readonly Lock _turnLock = new();
    private CancellationTokenSource? _turnCts;
    private Task? _turnTask;

    /// <summary>构造函数 — 注入回合执行器（生产侧绑定 SessionController.StreamResponseAsync）与权限模式应用器（可选）。</summary>
    public EngineTurnClient(TurnExecutor executeTurn, PermissionModeApplier? applyPermissionMode = null) {
        _consumer = new InProcessEventConsumer(_events);
        _executeTurn = executeTurn;
        _applyPermissionMode = applyPermissionMode;
    }

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

    /// <summary>执行单个回合 — 终态翻译对齐 BridgeServeCommand.RunTurnAsync：成功 → turn_completed；
    /// 取消 → turn_interrupted；异常 → error(异常消息含诱导上下文)。终态事件入队先于回合 Task 完成（无竞态窗口）。</summary>
    private async Task RunTurnAsync(string input, CancellationToken ct) {
        try {
            await _executeTurn(input, _consumer, ct).ConfigureAwait(false);
            _events.Enqueue(new BridgeEvent.TurnCompleted());
        } catch (OperationCanceledException) {
            _events.Enqueue(new BridgeEvent.TurnInterrupted());
        } catch (Exception ex) {
            _events.Enqueue(new BridgeEvent.Error($"回合执行失败: {ex.Message}"));
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
