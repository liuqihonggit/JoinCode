namespace JoinCode.Abstractions.Brain.Context.Compression;

public sealed record CompressionReportOptions(
    int OriginalTokenCount,
    int CompressedTokenCount,
    List<string> PreservedInfo,
    List<string> LostInfo,
    CompressionRequest? Request = null,
    string? StrategyName = null,
    long ProcessingTimeMs = 0,
    bool IsSuccess = true,
    string? ErrorMessage = null);

public sealed record CompressionReport {
    /// <summary>获取报告标识。</summary>
    public required string ReportId { get; init; }

    /// <summary>获取原始 token 数。</summary>
    public required int OriginalTokenCount { get; init; }

    /// <summary>获取压缩后 token 数。</summary>
    public required int CompressedTokenCount { get; init; }

    /// <summary>获取压缩比率。</summary>
    public required double CompressionRatio { get; init; }

    /// <summary>获取保留的信息列表。</summary>
    public required List<string> PreservedInfo { get; init; }

    /// <summary>获取丢失的信息列表。</summary>
    public required List<string> LostInfo { get; init; }

    /// <summary>获取时间戳。</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>获取目标上下文层类型。</summary>
    public ContextLayerType? TargetLayer { get; init; }

    /// <summary>获取策略名称。</summary>
    public string? StrategyName { get; init; }

    /// <summary>获取处理时长（毫秒）。</summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>获取压缩请求。</summary>
    public CompressionRequest? Request { get; init; }

    /// <summary>获取一个值，指示压缩是否成功。</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>获取节省的 token 数。</summary>
    public int SavedTokens => OriginalTokenCount - CompressedTokenCount;

    /// <summary>获取压缩效率百分比。</summary>
    public double CompressionEfficiency => OriginalTokenCount > 0
        ? (1 - CompressionRatio) * 100
        : 0;

    /// <summary>从选项创建压缩报告。</summary>
    public static CompressionReport Create(CompressionReportOptions options) {
        var ratio = options.OriginalTokenCount > 0
            ? (double)options.CompressedTokenCount / options.OriginalTokenCount
            : 0;

        return new CompressionReport {
            ReportId = Guid.NewGuid().ToString("N"),
            OriginalTokenCount = options.OriginalTokenCount,
            CompressedTokenCount = options.CompressedTokenCount,
            CompressionRatio = ratio,
            PreservedInfo = options.PreservedInfo,
            LostInfo = options.LostInfo,
            Timestamp = DateTime.UtcNow,
            TargetLayer = options.Request?.TargetLayer,
            StrategyName = options.StrategyName,
            ProcessingTimeMs = options.ProcessingTimeMs,
            Request = options.Request,
            IsSuccess = options.IsSuccess,
            ErrorMessage = options.ErrorMessage
        };
    }

    /// <summary>创建压缩失败报告。</summary>
    public static CompressionReport CreateFailed(
        int originalTokenCount,
        string errorMessage,
        CompressionRequest? request = null) {
        return new CompressionReport {
            ReportId = Guid.NewGuid().ToString("N"),
            OriginalTokenCount = originalTokenCount,
            CompressedTokenCount = originalTokenCount,
            CompressionRatio = 1.0,
            PreservedInfo = new List<string>(),
            LostInfo = new List<string>(),
            Timestamp = DateTime.UtcNow,
            TargetLayer = request?.TargetLayer,
            Request = request,
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}