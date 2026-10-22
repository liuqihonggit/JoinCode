namespace NativeTui.Core.Bridge;

/// <summary>
/// REPL 引擎抽象 — ReplShell 的回合交互面（ADR 0143 阶段二 ReplShell 提取）：
/// 提交/打断/权限模式请求 + 事件排空（拉模型，每拍一次）。
/// 实现者：进程内引擎适配（EngineTurnClient）、子进程 bridge（BridgeClient）、模拟引擎（SimulatedEngine）。
/// 回合生命周期全事件驱动 — shell 不乐观预挂 busy，以 TurnStarted/TurnCompleted/TurnInterrupted 事件为准。
/// </summary>
public interface IReplEngine
{
    /// <summary>提交回合（空文本由 shell 过滤；忙时由实现拒绝并回 Error 事件诱导）。</summary>
    void Submit(string text);

    /// <summary>打断当前回合（实现负责回 turn_interrupted；闲时 no-op）。</summary>
    void Interrupt();

    /// <summary>请求权限模式切换（footer 以 permission_mode_changed 回执为准 — 本地实现立即回执）。</summary>
    void SetPermissionMode(string mode);

    /// <summary>排空待应用事件（shell 每拍调用 — 实现可在此推进内部状态机产出到期事件）。</summary>
    IReadOnlyList<BridgeEvent> DrainEvents();

    /// <summary>排空诊断旁路（shell 写 stderr；无诊断实现返回空 — 默认空）。</summary>
    IReadOnlyList<string> DrainDiagnostics() => Array.Empty<string>();

    /// <summary>停止后台任务（M8-8 — kind: fork|teammate；默认空实现 — 无后台任务能力的实现
    /// （模拟引擎/测试桩）零成本，对齐 DrainDiagnostics 默认成员先例；进程内 EngineTurnClient
    /// 泵分派 / 子进程 BridgeEngine 写 stop_task 请求为显式实现）。</summary>
    /// <param name="kind">任务类别（fork|teammate）</param>
    /// <param name="taskId">任务标识</param>
    void StopBackgroundTask(string kind, string taskId) { }

    /// <summary>引擎存活（进程内恒 true；子进程退出后 false — shell 空闲时追加退出消息并结束）。</summary>
    bool IsAlive { get; }
}
