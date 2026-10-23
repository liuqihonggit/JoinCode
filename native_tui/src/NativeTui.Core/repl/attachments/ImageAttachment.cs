namespace NativeTui.Core.Repl.Attachments;

/// <summary>
/// 图片尺寸（基准 imageResizer.ts:138-143 ImageDimensions — 原始/显示宽高四元组，
/// 显示尺寸为缩放降采样后值；全部可空 — 剪贴板读取失败或未解码时缺省）。
/// </summary>
/// <param name="OriginalWidth">原始宽度 px。</param>
/// <param name="OriginalHeight">原始高度 px。</param>
/// <param name="DisplayWidth">显示（缩放后）宽度 px。</param>
/// <param name="DisplayHeight">显示（缩放后）高度 px。</param>
public sealed record ImageDimensions(
    int? OriginalWidth = null,
    int? OriginalHeight = null,
    int? DisplayWidth = null,
    int? DisplayHeight = null);

/// <summary>
/// 图片附件 DTO — 对齐基准 PastedContent 的 image 形态（config.ts:54-62）与
/// onImagePaste 构造默认值（PromptInput.tsx:1151-1164）：base64 数据在内存
///（基准 content 字段 — pasteStore.ts 只管大文本寻址存储，图片落盘走 image-cache 会话目录，
/// 此处 CachePath 记录落盘路径供链接/外部打开）。Id 由 <see cref="AttachmentList.Add"/> 分配
///（基准 nextPasteIdRef 顺序编号 — 0 = 未登记）。纯数据类：不做键盘/渲染/IO 决策。
/// </summary>
/// <param name="Id">附件编号（会话内顺序递增 — 0 = 未登记，登记后用于 [Image #N] 引用）。</param>
/// <param name="Base64">图片数据 base64（基准 PastedContent.content）。</param>
/// <param name="MediaType">MIME 类型（如 image/png — 基准 mediaType）。</param>
/// <param name="Filename">显示名（基准 filename — 附件插槽显示名）。</param>
/// <param name="SourcePath">源文件路径（终端拖拽粘贴的原图路径 — 基准 sourcePath）。</param>
/// <param name="Dimensions">宽高四元组（可空 — 基准 dimensions）。</param>
/// <param name="CachePath">落盘缓存路径（基准 imageStore storedImagePaths — null = 未落盘）。</param>
public sealed record ImageAttachment(
    int Id,
    string Base64,
    string MediaType,
    string Filename,
    string? SourcePath = null,
    ImageDimensions? Dimensions = null,
    string? CachePath = null)
{
    /// <summary>默认 MIME（基准 PromptInput.tsx:1159 — mediaType || 'image/png'）。</summary>
    public const string DefaultMediaType = "image/png";

    /// <summary>默认显示名（基准 PromptInput.tsx:1161 — filename || 'Pasted image'）。</summary>
    public const string DefaultFilename = "Pasted image";

    /// <summary>
    /// 工厂构造 — 未登记形态（Id=0，<see cref="AttachmentList.Add"/> 登记时分配编号），
    /// mediaType/filename 缺省走基准默认值。
    /// </summary>
    /// <param name="base64">图片数据 base64。</param>
    /// <param name="mediaType">MIME 类型（null = image/png）。</param>
    /// <param name="filename">显示名（null = Pasted image）。</param>
    /// <param name="sourcePath">源文件路径（可选）。</param>
    /// <param name="dimensions">宽高四元组（可选）。</param>
    /// <returns>未登记附件。</returns>
    public static ImageAttachment Of(
        string base64,
        string? mediaType = null,
        string? filename = null,
        string? sourcePath = null,
        ImageDimensions? dimensions = null) =>
        new(0, base64, mediaType ?? DefaultMediaType, filename ?? DefaultFilename, sourcePath, dimensions);

    /// <summary>原始字节数（base64 长度换算 — 4 字符 ↔ 3 字节，扣尾部 = 填充；供占位文本大小显示）。</summary>
    public long ByteSize
    {
        get
        {
            var span = Base64.AsSpan();
            var padding = span.Length - span.TrimEnd('=').Length;
            return span.Length / 4 * 3 - padding;
        }
    }

    /// <summary>输入区占位引用（基准 formatImageRef — history.ts:58-60 [Image #N]；
    /// 未登记（Id=0）退化为 [Image]（基准 UserImageMessage.tsx:26 无 id 形态））。</summary>
    public string Placeholder => Id > 0 ? $"[Image #{Id}]" : "[Image]";
}
