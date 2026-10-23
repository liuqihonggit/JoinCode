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

    /// <summary>收到的新会话次数（G3 — StartNewConversation 调用计数）。</summary>
    public int NewConversations;

    /// <summary>收到的带附件提交（G3 图片重载 — 每项 = (提交文本, 附件数)；单参提交不进此列表）。</summary>
    public List<(string Text, int Images)> SubmitsWithImages { get; } = [];

    /// <summary>收到的图片附件快照（G3 图片重载 — 每次 Submit 重载的附件列表原样记录，供 bridge DTO 字段断言）。</summary>
    public List<IReadOnlyList<BridgeImageAttachment>> SubmittedImages { get; } = [];

    /// <summary>待排空事件队列（DrainEvents 一次清空）。</summary>
    public Queue<BridgeEvent> Events { get; } = new();

    /// <summary>引擎存活标记（测试可置 false 模拟死亡）。</summary>
    public bool Alive = true;

    /// <summary>记录提交。</summary>
    public void Submit(string text) => Submits.Add(text);

    /// <summary>记录带附件提交（G3 图片重载 — 三面记录：SubmitsWithImages 文本+计数、
    /// SubmittedImages 附件本体、转调单参共用 Submits）。</summary>
    public void Submit(string text, IReadOnlyList<BridgeImageAttachment>? images)
    {
        SubmitsWithImages.Add((text, images?.Count ?? 0));
        SubmittedImages.Add(images ?? []);
        Submit(text);
    }

    /// <summary>记录新会话请求（G3 — D8 Clear 引擎调用断言面）。</summary>
    public void StartNewConversation() => NewConversations++;

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
