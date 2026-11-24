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

    /// <summary>预设的 queryId 序列（TASK135 C1 — 多查询测试）；队列空时退回 q-test。</summary>
    public Queue<string> QueryIds { get; } = new();

    /// <summary>待排空事件队列（DrainEvents 一次清空）。</summary>
    public Queue<BridgeEvent> Events { get; } = new();

    /// <summary>引擎存活标记（测试可置 false 模拟死亡）。</summary>
    public bool Alive = true;

    /// <summary>记录提交（ADR 0165 — 返回/回显 queryId）。仅带附件时进图片记录面
    /// （SubmitsWithImages/SubmittedImages 语义 = 「真的带了几个附件」，纯文本提交只进 Submits）。</summary>
    public string Submit(string text, IReadOnlyList<BridgeImageAttachment>? images = null, string? queryId = null)
    {
        if (images is { Count: > 0 }) {
            SubmitsWithImages.Add((text, images.Count));
            SubmittedImages.Add(images);
        }

        Submits.Add(text);
        if (!string.IsNullOrWhiteSpace(queryId)) return queryId;
        return QueryIds.Count > 0 ? QueryIds.Dequeue() : "q-test";
    }

    /// <summary>记录新会话请求（G3 — D8 Clear 引擎调用断言面）。</summary>
    public void StartNewConversation() => NewConversations++;

    /// <summary>记录打断（queryId 仅记录次数 — 本桩不驱动回合）。</summary>
    public void Interrupt(string? queryId = null) => Interrupts++;

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
