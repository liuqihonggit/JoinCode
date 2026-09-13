
namespace Core.Context.Compression;

/// <summary>
/// 上下文压缩器基础实现
/// </summary>
[Register(typeof(IContextCompressor), JoinCode.Abstractions.Attributes.ServiceLifetime.Transient)]
public sealed partial class ContextCompressor : ServiceEntity, IContextCompressor
{
    private readonly ICompressionStrategyFactory _strategyFactory;
    private readonly CompressionOptions _defaultOptions;

    /// <summary>
    /// 构造上下文压缩器
    /// </summary>
    /// <param name="strategyFactory">压缩策略工厂</param>
    /// <param name="defaultOptions">默认压缩选项，为 null 时使用默认配置</param>
    public ContextCompressor(
        ICompressionStrategyFactory strategyFactory,
        CompressionOptions? defaultOptions = null)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _defaultOptions = defaultOptions ?? CompressionOptions.Default;
    }

    /// <summary>
    /// 压缩指定内容
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="options">压缩选项，为 null 时使用默认选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    public async Task<CompressionResult> CompressAsync(
        string content,
        ContentType contentType,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var effectiveOptions = options ?? _defaultOptions;

        try
        {
            if (!CanCompress(content, contentType))
            {
                return CreateNoCompressionResult(content, contentType, stopwatch.ElapsedMilliseconds);
            }

            var strategy = _strategyFactory.GetStrategy(content, contentType);
            if (strategy == null)
            {
                return CreateErrorResult(
                    content,
                    contentType,
                    "No suitable compression strategy found",
                    stopwatch.ElapsedMilliseconds);
            }

            using var timeoutCts = new CancellationTokenSource(effectiveOptions.CompressionTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var compressedContent = await strategy.CompressAsync(
                content, effectiveOptions, linkedCts.Token).ConfigureAwait(false);

            stopwatch.Stop();

            return new CompressionResult
            {
                ContentId = Guid.NewGuid().ToString("N"),
                CompressedContent = compressedContent,
                OriginalLength = content.Length,
                CompressedLength = compressedContent.Length,
                ContentType = contentType,
                StrategyName = strategy.Name,
                IsSuccess = true,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["TargetRatio"] = JsonSerializer.SerializeToElement(effectiveOptions.TargetCompressionRatio, ContextDefaultJsonContext.Default.Double),
                    ["ActualRatio"] = JsonSerializer.SerializeToElement((double)compressedContent.Length / content.Length, ContextDefaultJsonContext.Default.Double),
                    ["StrategyPriority"] = JsonSerializer.SerializeToElement(strategy.Priority, ContextDefaultJsonContext.Default.Int32)
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return CreateErrorResult(
                content,
                contentType,
                "Compression timed out",
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateErrorResult(
                content,
                contentType,
                ex.Message,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// 批量压缩内容
    /// </summary>
    /// <param name="contents">待压缩内容项集合</param>
    /// <param name="options">压缩选项，为 null 时使用默认选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>各内容项的压缩结果列表</returns>
    public async Task<IReadOnlyList<CompressionResult>> CompressBatchAsync(
        IEnumerable<ContentItem> contents,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contentList = contents.ToList();
        var effectiveOptions = options ?? _defaultOptions;

        // 使用 LINQ 链式编程进行并行处理
        var tasks = contentList
            .Select(item => CompressAsync(item.Content, item.Type, effectiveOptions, cancellationToken));

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    /// 判断指定内容是否可压缩
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>是否可压缩</returns>
    public bool CanCompress(string content, ContentType contentType)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (content.Length < _defaultOptions.MinCompressionThreshold)
            return false;

        return _strategyFactory.HasStrategyFor(contentType);
    }

    /// <summary>
    /// 获取预估的压缩比率
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="options">压缩选项，为 null 时使用默认选项</param>
    /// <returns>预估压缩比率 (0-1)，无策略时返回 1.0</returns>
    public double GetCompressionRatio(
        string content,
        ContentType contentType,
        CompressionOptions? options = null)
    {
        var effectiveOptions = options ?? _defaultOptions;
        var strategy = _strategyFactory.GetStrategy(content, contentType);

        return strategy?.EstimateCompressionRatio(content, effectiveOptions) ?? 1.0;
    }

    private CompressionResult CreateNoCompressionResult(
        string content,
        ContentType contentType,
        long processingTimeMs)
    {
        return new CompressionResult
        {
            ContentId = Guid.NewGuid().ToString("N"),
            CompressedContent = content,
            OriginalLength = content.Length,
            CompressedLength = content.Length,
            ContentType = contentType,
            StrategyName = "None",
            IsSuccess = true,
            ProcessingTimeMs = processingTimeMs,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["Reason"] = JsonSerializer.SerializeToElement("Content does not meet compression criteria", ContextDefaultJsonContext.Default.String)
            }
        };
    }

    private CompressionResult CreateErrorResult(
        string content,
        ContentType contentType,
        string errorMessage,
        long processingTimeMs)
    {
        return new CompressionResult
        {
            ContentId = Guid.NewGuid().ToString("N"),
            CompressedContent = content,
            OriginalLength = content.Length,
            CompressedLength = content.Length,
            ContentType = contentType,
            StrategyName = "Error",
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ProcessingTimeMs = processingTimeMs
        };
    }
}
