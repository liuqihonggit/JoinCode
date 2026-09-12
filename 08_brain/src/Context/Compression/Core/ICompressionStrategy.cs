namespace Core.Context.Compression;

/// <summary>
/// 压缩策略接口
/// </summary>
public interface ICompressionStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 策略描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 支持的内容类型
    /// </summary>
    IReadOnlySet<ContentType> SupportedContentTypes { get; }

    /// <summary>
    /// 压缩内容
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="options">压缩选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩后的内容</returns>
    Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断是否可以处理指定内容
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>是否可以处理</returns>
    bool CanHandle(string content, ContentType contentType);

    /// <summary>
    /// 获取预估的压缩比率
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="options">压缩选项</param>
    /// <returns>预估压缩比率 (0-1)</returns>
    double EstimateCompressionRatio(string content, CompressionOptions options);

    /// <summary>
    /// 获取策略优先级（数值越高优先级越高）
    /// </summary>
    int Priority { get; }
}

/// <summary>
/// 压缩策略基类
/// </summary>
public abstract class CompressionStrategyBase : ICompressionStrategy
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract IReadOnlySet<ContentType> SupportedContentTypes { get; }
    public virtual int Priority { get; } = 0;

    public abstract Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default);

    public virtual bool CanHandle(string content, ContentType contentType)
    {
        return SupportedContentTypes.Contains(contentType) &&
               !string.IsNullOrEmpty(content) &&
               content.Length >= GetMinLengthThreshold();
    }

    public abstract double EstimateCompressionRatio(string content, CompressionOptions options);

    /// <summary>
    /// 获取最小长度阈值
    /// </summary>
    protected virtual int GetMinLengthThreshold() => 50;

    /// <summary>
    /// 验证选项
    /// </summary>
    protected virtual void ValidateOptions(CompressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.TargetCompressionRatio < 0 || options.TargetCompressionRatio > 1)
        {
            throw new ArgumentException(
                "TargetCompressionRatio must be between 0 and 1",
                nameof(options));
        }
    }

    /// <summary>
    /// 计算实际压缩比率
    /// </summary>
    protected double CalculateActualRatio(int originalLength, int compressedLength)
    {
        if (originalLength <= 0) return 0;
        return (double)compressedLength / originalLength;
    }

    /// <summary>
    /// 检查是否需要进一步压缩
    /// </summary>
    protected bool NeedsFurtherCompression(
        int originalLength,
        int currentLength,
        CompressionOptions options)
    {
        var currentRatio = CalculateActualRatio(originalLength, currentLength);
        return currentRatio > options.TargetCompressionRatio;
    }
}
