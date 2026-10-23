namespace NativeTui.Core.Repl.Attachments;

/// <summary>
/// 图片附件占位项（IScrollItem）— 渲染形态对齐基准 UserImageMessage.tsx:20-58：
/// 正文 = [Image #N] 标签（imageId 无效时退化 [Image] — 基准 26 行），
/// MessageResponse 连接样式（"  ⎿  " dim 前缀 + 默认正文 — 同基准 MessageResponse 包裹形态），
/// 独立成项时 margin 1（基准 addMargin marginTop 1 — 图片无正文独立开新用户回合场景）。
/// Filename 为断言面不参与标签渲染（基准历史消息只显示 [Image #N]，filename 用于附件插槽）。
/// </summary>
public sealed class ImageAttachmentItem(int imageId, string? filename = null) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>附件编号（会话内顺序 — [Image #N] 标签数据源）。</summary>
    public int ImageId => imageId;

    /// <summary>显示名（断言面 — 基准 filename 附件插槽显示语义，不参与本项渲染）。</summary>
    public string? Filename => filename;

    /// <summary>标签（基准 UserImageMessage.tsx:26 — imageId 有效 [Image #N]，否则 [Image]）。</summary>
    public string Label => imageId > 0 ? $"[Image #{imageId}]" : "[Image]";

    /// <summary>内容高度 = margin 1 + 标签行 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>渲染：margin 行留空，标签行画在 frame.Y+1（⎿ dim 前缀 + 默认正文 — MessageResponse 形态）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, Label);

    /// <summary>标签 + 文件名匹配（transcript 搜索）。</summary>
    public bool ContainsText(string query) =>
        Label.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (filename?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}
