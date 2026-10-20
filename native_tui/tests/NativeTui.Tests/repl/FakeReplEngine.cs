namespace NativeTui.Tests.Repl;

/// <summary>记录型假引擎 — 捕获 Submit/Interrupt/SetPermissionMode 调用 + 可注入事件队列（ReplShell 测试共享）。</summary>
internal sealed class FakeReplEngine : IReplEngine
{
    /// <summary>收到的提交文本。</summary>
    public List<string> Submits { get; } = [];

    /// <summary>收到的打断次数。</summary>
    public int Interrupts;

    /// <summary>收到的权限模式。</summary>
    public List<string> PermissionModes { get; } = [];

    /// <summary>待排空事件队列（DrainEvents 一次清空）。</summary>
    public Queue<BridgeEvent> Events { get; } = new();

    /// <summary>引擎存活标记（测试可置 false 模拟死亡）。</summary>
    public bool Alive = true;

    /// <summary>记录提交。</summary>
    public void Submit(string text) => Submits.Add(text);

    /// <summary>记录打断。</summary>
    public void Interrupt() => Interrupts++;

    /// <summary>记录权限模式。</summary>
    public void SetPermissionMode(string mode) => PermissionModes.Add(mode);

    /// <summary>排空事件队列。</summary>
    public IReadOnlyList<BridgeEvent> DrainEvents()
    {
        var drained = Events.ToList();
        Events.Clear();
        return drained;
    }

    /// <summary>存活标记。</summary>
    public bool IsAlive => Alive;
}
