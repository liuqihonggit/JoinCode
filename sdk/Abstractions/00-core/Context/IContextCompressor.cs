namespace JoinCode.Abstractions.Interfaces.Context;

/// <summary>
/// 上下文压缩器接口
/// </summary>
public interface IContextCompressor
{
    /// <summary>
    /// 异步压缩上下文内容
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="options">压缩选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    Task<CompressionResult> CompressAsync(
        string content,
        ContentType contentType,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步压缩上下文内容（批量）
    /// </summary>
    /// <param name="contents">内容列表</param>
    /// <param name="options">压缩选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果列表</returns>
    Task<IReadOnlyList<CompressionResult>> CompressBatchAsync(
        IEnumerable<ContentItem> contents,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断是否可以压缩指定内容
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>是否可以压缩</returns>
    bool CanCompress(string content, ContentType contentType);

    /// <summary>
    /// 获取预估的压缩比率
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="options">压缩选项</param>
    /// <returns>预估压缩比率 (0-1)</returns>
    double GetCompressionRatio(
        string content,
        ContentType contentType,
        CompressionOptions? options = null);
}

/// <summary>
/// 内容类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ContentTypeConstants + ContentTypeExtensions
/// </summary>
public enum ContentType
{
    /// <summary>代码内容</summary>
    [EnumValue("code")] Code = 0,

    /// <summary>对话历史</summary>
    [EnumValue("dialogue")] Dialogue = 1,

    /// <summary>引用索引</summary>
    [EnumValue("reference_index")] ReferenceIndex = 2,

    /// <summary>文本内容</summary>
    [EnumValue("text")] Text = 3,

    /// <summary>日志内容</summary>
    [EnumValue("log")] Log = 4
}

/// <summary>
/// 内容项
/// </summary>
public record ContentItem
{
    /// <summary>
    /// 内容标识
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 内容类型
    /// </summary>
    public required ContentType Type { get; init; }

    /// <summary>
    /// 内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>
/// 压缩结果
/// </summary>
public record CompressionResult
{
    /// <summary>
    /// 原始内容ID
    /// </summary>
    public required string ContentId { get; init; }

    /// <summary>
    /// 压缩后的内容
    /// </summary>
    public required string CompressedContent { get; init; }

    /// <summary>
    /// 原始内容长度
    /// </summary>
    public required int OriginalLength { get; init; }

    /// <summary>
    /// 压缩后内容长度
    /// </summary>
    public required int CompressedLength { get; init; }

    /// <summary>
    /// 内容类型
    /// </summary>
    public required ContentType ContentType { get; init; }

    /// <summary>
    /// 压缩策略名称
    /// </summary>
    public required string StrategyName { get; init; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 压缩比率
    /// </summary>
    public double CompressionRatio => OriginalLength > 0
        ? (double)CompressedLength / OriginalLength
        : 0;

    /// <summary>
    /// 节省的token数量（估算）
    /// </summary>
    public int SavedTokens => OriginalLength - CompressedLength;

    /// <summary>
    /// 处理时间（毫秒）
    /// </summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}
