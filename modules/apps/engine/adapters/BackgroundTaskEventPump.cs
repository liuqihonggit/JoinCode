namespace JoinCode.Adapters;

/// <summary>
/// 后台任务事件泵（M8-8）— 绕开回合作用域的 SubAgentEventChannel，把后台 fork/teammate 生命周期
/// 直推 <see cref="IEventConsumer.OnBackgroundTask"/>（TUI 常驻任务面板事件源）：
/// <para>结构性动机：SubAgentEventChannel 是 AsyncLocal 回合作用域，idle 期到达的后台终态事件被死通道
/// 静默丢弃 — 泵改为进程级订阅 <see cref="IForkSubAgentManager.ForkCompleted"/> 与
/// <see cref="IInProcessTeammateTaskExecutor.TeammateCompleted"/>（两事件源全仓此前零订阅者，现成接入杠杆），
/// 事件到达即推 consumer，与回合边界解耦。</para>
/// <para>两查询面均为可选注入（引擎未注册时对应路径静默跳过，对齐 AgentForkMiddleware 可选依赖形态）；
/// <see cref="Bind"/> 延迟绑定推送出口（消费方实例晚于泵构造 — BridgeServeLoop 内建 consumer 后绑定）。</para>
/// <para>线程安全：事件从 Actor 后台线程到达，consumer 推送安全由实现方自保（BridgeEventConsumer 走
/// TextWriter.Synchronized、InProcessEventConsumer 入队 FIFO），泵侧仅以 volatile 引用交接，不加锁。</para>
/// </summary>
public sealed class BackgroundTaskEventPump : IAsyncDisposable {
    private readonly IForkSubAgentManager? _forkManager;
    private readonly IInProcessTeammateTaskExecutor? _teammateExecutor;
    private readonly ILogger<BackgroundTaskEventPump>? _logger;
    private volatile IEventConsumer? _consumer;
    private int _subscribed;

    /// <summary>构造函数 — 注入 fork/teammate 查询与完成事件面（均可选）与可选日志。</summary>
    /// <param name="forkManager">fork 管理器（ForkCompleted 终态事件 + GetActiveForksAsync 快照；null 时 fork 路径静默跳过）</param>
    /// <param name="teammateExecutor">teammate 执行器（TeammateCompleted 终态事件 + GetActiveTeammateSnapshotsAsync 快照；null 时 teammate 路径静默跳过）</param>
    /// <param name="logger">可选日志（快照查询异常降级记录）</param>
    public BackgroundTaskEventPump(
        IForkSubAgentManager? forkManager,
        IInProcessTeammateTaskExecutor? teammateExecutor,
        ILogger<BackgroundTaskEventPump>? logger = null) {
        _forkManager = forkManager;
        _teammateExecutor = teammateExecutor;
        _logger = logger;
    }

    /// <summary>绑定推送出口（组装层在 consumer 就绪后调用；重绑覆盖 — consumer 全程单实例场景）。</summary>
    /// <param name="consumer">事件消费出口（OnBackgroundTask 四参 upsert）</param>
    public void Bind(IEventConsumer consumer) {
        ArgumentNullException.ThrowIfNull(consumer);
        _consumer = consumer;
    }

    /// <summary>订阅两完成事件面（幂等 — 重复调用零副作用；无异步等待，订阅即生效）。</summary>
    public Task StartAsync() {
        if (Interlocked.Exchange(ref _subscribed, 1) != 0)
            return Task.CompletedTask;

        if (_forkManager is not null)
            _forkManager.ForkCompleted += OnForkCompleted;
        if (_teammateExecutor is not null)
            _teammateExecutor.TeammateCompleted += OnTeammateCompleted;
        return Task.CompletedTask;
    }

    /// <summary>全量快照 upsert（回合结束边界调用）— fork 按快照实际 State 推（running 补发 + 终态幂等重发，
    /// 避免把 TUI 已定格的终态条目拉回 running 的倒退）并透传 ForkSubAgent.TaskDescription（Identity 同源，
    /// 空/空白仍传空串 — TUI 侧空 description 回退显示 taskId）；teammate 快照为活跃集合，全发 running。
    /// 单查询面异常降级日志不中断（Actor 可能已释放，监控路径不得杀回合收尾）。</summary>
    /// <param name="ct">取消令牌</param>
    public async Task PublishSnapshotAsync(CancellationToken ct = default) {
        var consumer = _consumer;
        if (consumer is null)
            return;

        if (_forkManager is not null) {
            try {
                var forks = await _forkManager.GetActiveForksAsync(ct).ConfigureAwait(false);
                foreach (var fork in forks)
                    consumer.OnBackgroundTask(BackgroundTaskKinds.Fork, fork.ForkId,
                        NormalizeForkDescription(fork.TaskDescription), MapForkState(fork.State));
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "后台任务泵 fork 快照查询失败，跳过本批");
            }
        }

        if (_teammateExecutor is not null) {
            try {
                var teammates = await _teammateExecutor.GetActiveTeammateSnapshotsAsync(ct).ConfigureAwait(false);
                foreach (var teammate in teammates)
                    consumer.OnBackgroundTask(BackgroundTaskKinds.Teammate, teammate.TeammateId, teammate.Task, BackgroundTaskStates.Running);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "后台任务泵 teammate 快照查询失败，跳过本批");
            }
        }
    }

    /// <summary>停止后台任务（M8-8 后续 — 基准 BackgroundTasksDialog.tsx:268-289 x 停止）：
    /// kind 分派 — fork → <see cref="IForkSubAgentManager.CancelForkAsync"/>、teammate →
    /// <see cref="IInProcessTeammateTaskExecutor.StopTeammateAsync"/>（Stop 档：取消生命周期并清理资源；
    /// Interrupt 只断当前 work 不杀生命周期、Terminate 走消息收尾 — 两档语义不同不适用）。
    /// 分派后无条件发终态 upsert（state=cancelled，description 空串 — 面板 Upsert 空描述保留旧值）：
    /// 引擎调用失败也发（TUI 面板立即反馈；引擎后续真实终态事件到达再覆盖 — upsert 幂等）。
    /// kind 对应查询面未注册（引擎无该能力）或未知 kind → 零分派零推送（面板不可能存在对应条目）。
    /// 引擎调用异常降级日志不中断（对齐 PublishSnapshotAsync 防御形态）。</summary>
    /// <param name="kind">任务类别（BackgroundTaskKinds.Fork / Teammate）</param>
    /// <param name="taskId">任务标识（forkId / teammateId）</param>
    /// <param name="ct">取消令牌</param>
    public async Task StopTaskAsync(string kind, string taskId, CancellationToken ct = default) {
        var dispatched = false;
        try {
            if (kind == BackgroundTaskKinds.Fork && _forkManager is { } forkManager) {
                await forkManager.CancelForkAsync(taskId, ct).ConfigureAwait(false);
                dispatched = true;
            } else if (kind == BackgroundTaskKinds.Teammate && _teammateExecutor is { } teammateExecutor) {
                await teammateExecutor.StopTeammateAsync(taskId, ct).ConfigureAwait(false);
                dispatched = true;
            }
        } catch (Exception ex) {
            // 引擎停止调用失败仍发终态 — TUI 反馈优先，真实终态事件随后覆盖
            dispatched = true;
            _logger?.LogWarning(ex, "后台任务泵停止 {Kind} {TaskId} 引擎调用失败，仍下发 cancelled 终态", kind, taskId);
        }

        if (dispatched)
            _consumer?.OnBackgroundTask(kind, taskId, string.Empty, BackgroundTaskStates.Cancelled);
    }

    /// <summary>退订两完成事件面（幂等 — 未订阅/重复退订零副作用；无超时无等待，退订即完成）。</summary>
    public Task StopAsync() {
        if (Interlocked.Exchange(ref _subscribed, 0) != 0) {
            if (_forkManager is not null)
                _forkManager.ForkCompleted -= OnForkCompleted;
            if (_teammateExecutor is not null)
                _teammateExecutor.TeammateCompleted -= OnTeammateCompleted;
        }
        return Task.CompletedTask;
    }

    /// <summary>释放 — 等价 StopAsync 退订（无资源需异步清理）。</summary>
    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>ForkCompleted → OnBackgroundTask("fork", forkId, taskDescription, 终态)。
    /// Bind 前到达的事件丢弃（组装顺序保证泵先 Bind 后 Start，仅测试直接触发可能越过）。</summary>
    private void OnForkCompleted(object? sender, ForkCompletedEventArgs e) =>
        _consumer?.OnBackgroundTask(BackgroundTaskKinds.Fork, e.ForkId, e.TaskDescription, MapForkState(e.State));

    /// <summary>TeammateCompleted → OnBackgroundTask("teammate", teammateId, task, 终态)。
    /// 事件参数只有 IsSuccess 二态 — 被终止/失败统一映射 failed（无 cancelled 信号源，见泵 XML 注释）。</summary>
    private void OnTeammateCompleted(object? sender, TeammateCompletedEventArgs e) =>
        _consumer?.OnBackgroundTask(
            BackgroundTaskKinds.Teammate, e.TeammateId, e.Task,
            e.IsSuccess ? BackgroundTaskStates.Completed : BackgroundTaskStates.Failed);

    /// <summary>ForkState → 协议四值：[EnumValue] 唯一数据源取值，merged 折叠 completed
    /// （合并即成功终态 — MergeFork 不发完成事件，快照路径亦过滤 merged，此处仅防御性折叠）。</summary>
    private static string MapForkState(ForkState state) =>
        state.ToValue() == "merged" ? BackgroundTaskStates.Completed : state.ToValue();

    /// <summary>快照描述归一化 — 非空白透传（与终态事件路径 TaskDescription 同源），空/空白回退空串
    /// （对齐既有 fallback 形态，TUI 侧空 description 显示 taskId）。</summary>
    private static string NormalizeForkDescription(string? taskDescription) =>
        string.IsNullOrWhiteSpace(taskDescription) ? string.Empty : taskDescription;
}

/// <summary>后台任务种类（M8-8 — 协议 kind 字段值；有限集合字符串常量，后续枚举化随面板扩展收编）。</summary>
public static class BackgroundTaskKinds {
    /// <summary>后台 fork 子代理（AgentForkMiddleware 回退路径 — 继承父上下文分叉）。</summary>
    public const string Fork = "fork";

    /// <summary>后台 teammate 子代理（AgentForkMiddleware 优先路径 — ContinuousMode 消息驱动）。</summary>
    public const string Teammate = "teammate";
}

/// <summary>后台任务状态（M8-8 — 协议 state 字段值；与 native_tui 侧约定四值）。</summary>
public static class BackgroundTaskStates {
    /// <summary>运行中。</summary>
    public const string Running = "running";

    /// <summary>成功终态。</summary>
    public const string Completed = "completed";

    /// <summary>失败终态。</summary>
    public const string Failed = "failed";

    /// <summary>取消终态。</summary>
    public const string Cancelled = "cancelled";
}
