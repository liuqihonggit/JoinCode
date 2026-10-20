namespace NativeTui.Core.Bridge;

/// <summary>
/// 模拟引擎 — Demo 模拟回合的事件化改造（ReplShell 提取后替代 DemoSession 内嵌状态机）：
/// 时间驱动三阶段（Thinking 1.2s → ToolUse 2.4s → Responding 5.4s），
/// 阶段迁移产出 BridgeEvent（ToolCall/ToolResult/TokenDelta/TurnCompleted），
/// 与真实 bridge 路径共用同一事件协议（ReplBridgeDriver 唯一翻译层）。
/// 时钟注入（默认墙钟）— 单元测试可注入虚拟时钟做确定性验证。
/// spinner Message 不经事件协议（真实路径空消息+随机动词采样，模拟直改属可弃糖）。
/// </summary>
public sealed class SimulatedEngine : IReplEngine
{
    /// <summary>Thinking → ToolUse 迁移时刻（ms）。</summary>
    private const long ToolUseAtMs = 1200;

    /// <summary>ToolUse → Responding 迁移时刻（ms）。</summary>
    private const long RespondingAtMs = 2400;

    /// <summary>回合完成时刻（ms）。</summary>
    private const long CompletedAtMs = 5400;

    /// <summary>Responding 阶段每拍 token 增量。</summary>
    private const int TokenDeltaPerTick = 9;

    /// <summary>回合计时起点（clock 值）。</summary>
    private long _turnStart;

    /// <summary>已推进到的阶段（0=Thinking / 1=ToolUse / 2=Responding / 3=完成）。</summary>
    private int _stage;

    /// <summary>回合进行中。</summary>
    private bool _inTurn;

    /// <summary>待排空事件队列（Submit/Interrupt 立即入队，DrainEvents 阶段迁移追加）。</summary>
    private readonly Queue<BridgeEvent> _pending = new();

    /// <summary>时钟（默认墙钟 — 测试注入虚拟时钟）。</summary>
    private readonly Func<long> _clock;

    /// <summary>默认时间源 — 墙钟毫秒。</summary>
    private static long DefaultClock() => Environment.TickCount64;

    /// <summary>创建模拟引擎（clock=时间源，毫秒）。</summary>
    public SimulatedEngine(Func<long>? clock = null) => _clock = clock ?? DefaultClock;

    /// <summary>进程内模拟 — 恒存活。</summary>
    public bool IsAlive => true;

    /// <summary>提交回合：忙时拒绝回 Error 诱导（对齐 EngineTurnClient 语义），否则入队 TurnStarted 并开始计时。</summary>
    public void Submit(string text)
    {
        if (_inTurn)
        {
            _pending.Enqueue(new BridgeEvent.Error("回合进行中，请等待完成或按 Esc 打断后再提交。"));
            return;
        }

        _inTurn = true;
        _stage = 0;
        _turnStart = _clock();
        _pending.Enqueue(new BridgeEvent.TurnStarted());
    }

    /// <summary>打断当前回合（闲时 no-op）。</summary>
    public void Interrupt()
    {
        if (!_inTurn)
        {
            return;
        }

        _inTurn = false;
        _pending.Enqueue(new BridgeEvent.TurnInterrupted());
    }

    /// <summary>权限模式本地立即回执（统一事件语义 — 与真实引擎回执路径一致）。</summary>
    public void SetPermissionMode(string mode) =>
        _pending.Enqueue(new BridgeEvent.PermissionModeChanged(mode));

    /// <summary>排空事件：先推进时间驱动状态机（产出到期迁移事件），再清空队列。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents()
    {
        AdvanceStages();
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    /// <summary>按已流逝时间推进阶段状态机（到期迁移入队事件；Responding 阶段每拍 token 增量）。</summary>
    private void AdvanceStages()
    {
        if (!_inTurn)
        {
            return;
        }

        var elapsed = _clock() - _turnStart;
        if (_stage == 0 && elapsed >= ToolUseAtMs)
        {
            _stage = 1;
            _pending.Enqueue(new BridgeEvent.ToolCall("Read", "demo/simulated.cs"));
        }

        if (_stage == 1 && elapsed >= RespondingAtMs)
        {
            _stage = 2;
            _pending.Enqueue(new BridgeEvent.ToolResult("7 lines (simulated)"));
        }

        if (_stage == 2 && elapsed >= CompletedAtMs)
        {
            _stage = 3;
            _inTurn = false;
            _pending.Enqueue(new BridgeEvent.AssistantMessage("演示回复 — diff 渲染管线运行正常。"));
            _pending.Enqueue(new BridgeEvent.TurnCompleted());
            return;
        }

        if (_stage == 2)
        {
            _pending.Enqueue(new BridgeEvent.TokenDelta(TokenDeltaPerTick));
        }
    }
}
