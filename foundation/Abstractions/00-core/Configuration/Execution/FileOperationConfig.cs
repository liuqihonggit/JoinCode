namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// 文件操作配置
/// </summary>
/// <remarks>
/// 手动注册（有自定义验证逻辑），不使用 [RegisterOptions]
/// </remarks>
public sealed class FileOperationConfig
{
    /// <summary>
    /// 最大读取大小（字节，默认 256KB，与 TS 对齐）
    /// </summary>
    [Range(1024, 1024L * 1024 * 1024, ErrorMessage = "MaxReadSize must be between 1KB and 1GB")]
    public long MaxReadSize { get; set; } = 256 * 1024;

    /// <summary>
    /// 最大读取Token数（默认 25000，与 TS 对齐）
    /// TS: DEFAULT_MAX_OUTPUT_TOKENS = 25000
    /// 读取后按token估算截断，防止超大文件消耗全部上下文
    /// </summary>
    [Range(1000, 500000, ErrorMessage = "MaxReadTokens must be between 1000 and 500000")]
    public int MaxReadTokens { get; set; } = 25000;

    /// <summary>
    /// 最大写入大小（字节，默认 10MB）
    /// </summary>
    [Range(1024, 1024 * 1024 * 1024, ErrorMessage = "MaxWriteSize must be between 1KB and 1GB")]
    public int MaxWriteSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// 缓冲区大小（字节，默认 8KB）
    /// </summary>
    [Range(512, 1024 * 1024, ErrorMessage = "BufferSize must be between 512 bytes and 1MB")]
    public int BufferSize { get; set; } = 8 * 1024;

    /// <summary>
    /// 二进制文件检测缓冲区大小（字节，默认 8KB）
    /// </summary>
    [Range(1024, 64 * 1024, ErrorMessage = "BinaryDetectionBufferSize must be between 1KB and 64KB")]
    public int BinaryDetectionBufferSize { get; set; } = 8 * 1024;

    #region 图像限制（对齐 TS: constants/apiLimits.ts）

    /// <summary>
    /// API 最大 base64 编码图像大小（5MB）。
    /// API 拒绝超过此值的 base64 字符串。
    /// 注意：这是 base64 长度，不是原始字节数。Base64 增加约 33% 大小。
    /// TS: API_IMAGE_MAX_BASE64_SIZE = 5 * 1024 * 1024
    /// </summary>
    public const int ApiImageMaxBase64Size = 5 * 1024 * 1024;

    /// <summary>
    /// 目标原始图像大小（3.75MB），确保 base64 编码后不超过上限。
    /// Base64 编码增加 4/3 大小：raw_size * 4/3 = base64_size → raw_size = base64_size * 3/4
    /// TS: IMAGE_TARGET_RAW_SIZE = (API_IMAGE_MAX_BASE64_SIZE * 3) / 4
    /// </summary>
    public const int ImageTargetRawSize = (ApiImageMaxBase64Size * 3) / 4;

    /// <summary>
    /// 客户端最大图像宽度（像素）。
    /// API 内部会调整超过 1568px 的图像（服务端处理），
    /// 客户端限制稍大以保留质量。
    /// TS: IMAGE_MAX_WIDTH = 2000
    /// </summary>
    public const int ImageMaxWidth = 2000;

    /// <summary>
    /// 客户端最大图像高度（像素）。
    /// TS: IMAGE_MAX_HEIGHT = 2000
    /// </summary>
    public const int ImageMaxHeight = 2000;

    #endregion

    #region PDF 限制（对齐 TS: constants/apiLimits.ts）

    /// <summary>
    /// PDF 目标原始大小（20MB）。
    /// API 有 32MB 总请求限制，base64 编码增加约 33%，
    /// 20MB 原始 → ~27MB base64，留余量给对话上下文。
    /// TS: PDF_TARGET_RAW_SIZE = 20 * 1024 * 1024
    /// </summary>
    public const int PdfTargetRawSize = 20 * 1024 * 1024;

    /// <summary>
    /// PDF 提取大小阈值（3MB）。
    /// 超过此大小的 PDF 需要提取页面而非整文件发送。
    /// TS: PDF_EXTRACT_SIZE_THRESHOLD = 3 * 1024 * 1024
    /// </summary>
    public const int PdfExtractSizeThreshold = 3 * 1024 * 1024;

    /// <summary>
    /// PDF 单次读取最大页数。
    /// 对齐 TS: PDF_MAX_PAGES_PER_READ = 20
    /// </summary>
    public const int PdfMaxPagesPerRead = 20;

    /// <summary>
    /// PDF 内联读取最大页数。
    /// 超过此页数的 PDF 必须使用 pages 参数指定范围，不允许一次性读取。
    /// TS: PDF_AT_MENTION_INLINE_THRESHOLD = 10
    /// </summary>
    public const int PdfMaxInlinePageCount = 10;

    #endregion
}
