namespace NativeTui.Core.Repl;

/// <summary>
/// 不完整前缀超时策略 — 基准 App.tsx:120-121（NORMAL_TIMEOUT=50ms / PASTE_TIMEOUT=500ms）+
/// :309-333 flushIncomplete：序列后半段永不到达（慢链路丢失/终端异常）时按形态阈值判定
/// 强制 flush（配合 <see cref="KeyPressParser.TryParseNext"/> force 入口释放输入通道 —
/// 非 force 路径不完整前缀永久滞留 pending = 输入静默失联）。
/// 粘贴形态（200~ 已完整出现）用 500ms — 大块粘贴网络传输窗口更宽。
/// </summary>
public static class EscapeFlushPolicy
{
    /// <summary>普通 ESC 前缀超时阈值（ms — 序列后半段通常原子到达，50ms 足够覆盖慢链路分裂）。</summary>
    public const int NormalTimeoutMs = 50;

    /// <summary>粘贴前缀超时阈值（ms — 大块粘贴跨包传输，基准 PASTE_TIMEOUT）。</summary>
    public const int PasteTimeoutMs = 500;

    /// <summary>滞留时长是否超过该 pending 形态的阈值（paste 前缀 500ms、其余 ESC 前缀 50ms；
    /// 非 ESC 开头不触发 — 文本解析永成功滞留仅 ESC 形态）。</summary>
    public static bool ShouldForceFlush(ReadOnlySpan<char> pending, long stuckMs) =>
        pending.Length > 0
        && pending[0] == '\u001b'
        && stuckMs > (IsPastePrefix(pending) ? PasteTimeoutMs : NormalTimeoutMs);

    private static bool IsPastePrefix(ReadOnlySpan<char> pending) =>
        pending.StartsWith("\u001b[200~");
}
