namespace NativeTui.Core.Repl.Attachments;

/// <summary>
/// 待发送图片附件集合 — 对齐基准 pastedContents 的 image 部分（PromptInput.tsx:1173-1176
/// setPastedContents 增量登记）+ nextPasteIdRef 顺序编号（PromptInput.tsx:1154）+
/// 孤儿修剪（PromptInput.tsx:1185-1200 — 输入文本中 [Image #N] 占位被删即清理附件，
/// 覆盖 pill 退格/Ctrl+U/逐字符删除等一切移除引用的编辑）。纯数据类：不做键盘/渲染/IO 决策，
/// 输入域持有并在粘贴入口联动（[Image #N] 引用形态与 PastedContentTracker.ReferenceRegex 的
/// Image 分支互通 — ExpandReferences 不展开 Image 引用，提交时原样保留转引擎 image 块，E4 接线）。
/// </summary>
public sealed partial class AttachmentList
{
    /// <summary>已登记附件（id → 附件 — 基准 Record&lt;number, PastedContent&gt;）。</summary>
    private readonly Dictionary<int, ImageAttachment> _items = [];

    /// <summary>下一编号（基准 nextPasteIdRef — 会话内顺序递增，Clear 随输入清空重置）。</summary>
    private int _nextId = 1;

    /// <summary>已登记附件数。</summary>
    public int Count => _items.Count;

    /// <summary>附件全量（按 Id 升序 — 提交/附件列表展示的消费序）。</summary>
    public IReadOnlyList<ImageAttachment> Items => [.. _items.Values.OrderBy(item => item.Id)];

    /// <summary>
    /// 登记附件并分配编号（基准 onImagePaste — PromptInput.tsx:1154-1183：pasteId 先取后构造，
    /// 占位引用插入输入缓冲）。
    /// </summary>
    /// <param name="attachment">未登记附件（Id 任意 — 登记时覆盖分配）。</param>
    /// <returns>(登记后附件（Id 已分配）, 输入区占位引用 [Image #N] — 基准 insertTextAtCursor(formatImageRef(pasteId)))。</returns>
    public (ImageAttachment Registered, string Placeholder) Add(ImageAttachment attachment)
    {
        var registered = attachment with { Id = _nextId++ };
        _items[registered.Id] = registered;
        return (registered, FormatImageRef(registered.Id));
    }

    /// <summary>按编号取附件（未登记返回 null）。</summary>
    /// <param name="id">附件编号。</param>
    /// <returns>附件；未登记 null。</returns>
    public ImageAttachment? TryGet(int id) => _items.TryGetValue(id, out var attachment) ? attachment : null;

    /// <summary>移除附件（基准孤儿清理的显式删除面 — 附件管理 UI 移除用）。</summary>
    /// <param name="id">附件编号。</param>
    /// <returns>是否存在并移除。</returns>
    public bool Remove(int id) => _items.Remove(id);

    /// <summary>清空集合并重置编号（提交后随输入缓冲清空联动 — 编号重置对齐 PastedContentTracker.Clear 先例）。</summary>
    public void Clear()
    {
        _items.Clear();
        _nextId = 1;
    }

    /// <summary>
    /// 孤儿修剪（基准 PromptInput.tsx:1185-1200）— 输入文本中 [Image #N] 占位被删的附件清理：
    /// 解析输入现存 Image 引用编号 → 引用不存在的附件移除（覆盖一切移除引用的编辑路径）。
    /// 仅认 Image 引用（[Pasted text #N] 文本引用不参与 — 基准 referencedIds 不含文本引用编号区分，
    /// 但文本登记在 PastedContentTracker 独立编号空间，互不干扰）。
    /// </summary>
    /// <param name="input">当前输入文本（含占位引用形态）。</param>
    /// <returns>被清理的孤儿附件列表（空 = 无修剪）。</returns>
    public IReadOnlyList<ImageAttachment> PruneOrphans(string input)
    {
        var referenced = ImageRefRegex().Matches(input)
            .Select(match => int.TryParse(match.Groups[1].ValueSpan, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
        var orphaned = _items.Values.Where(item => !referenced.Contains(item.Id)).ToList();
        foreach (var orphan in orphaned)
        {
            _items.Remove(orphan.Id);
        }

        return orphaned;
    }

    /// <summary>输入区占位引用格式（基准 formatImageRef — history.ts:58-60）。</summary>
    /// <param name="id">附件编号。</param>
    /// <returns>[Image #N] 占位串。</returns>
    internal static string FormatImageRef(int id) => $"[Image #{id}]";

    /// <summary>
    /// 附件摘要文本（w2 附件列表/占位显示形态 — [图片: 名.png 123KB]；基准无直接对应 UI 文本，
    /// filename 显示语义对齐 config.ts:59 attachment slot 注释）。大小人性化（B/KB/MB）。
    /// </summary>
    /// <param name="attachment">附件。</param>
    /// <returns>摘要串。</returns>
    public static string FormatSummary(ImageAttachment attachment) =>
        $"[图片: {attachment.Filename} {FormatByteSize(attachment.ByteSize)}]";

    /// <summary>字节大小人性化（≥1MB 一位小数 MB；≥1KB 整数 KB；否则 B）。</summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>人性化大小串。</returns>
    internal static string FormatByteSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#}MB"
        : bytes >= 1024 ? $"{bytes / 1024}KB"
        : $"{bytes}B";

    /// <summary>Image 引用识别正则（基准 referencePattern 的 Image 分支 — history.ts:66；
    /// PastedContentTracker.ReferenceRegex 同源互通）。</summary>
    [GeneratedRegex(@"\[Image #(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ImageRefRegex();
}
