
namespace Core.Context.Compression;

/// <summary>
/// 上下文压缩器基础实现
/// </summary>
[Register(typeof(IContextCompressor), JoinCode.Abstractions.Attributes.ServiceLifetime.Transient)]
public sealed partial class ContextCompressor : IContextCompressor
{
    private readonly ICompressionStrategyFactory _strategyFactory;
    private readonly CompressionOptions _defaultOptions;

    public ContextCompressor(
        ICompressionStrategyFactory strategyFactory,
        CompressionOptions? defaultOptions = null)
    {
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _defaultOptions = defaultOptions ?? CompressionOptions.Default;
    }

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

    public bool CanCompress(string content, ContentType contentType)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (content.Length < _defaultOptions.MinCompressionThreshold)
            return false;

        return _strategyFactory.HasStrategyFor(contentType);
    }

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
