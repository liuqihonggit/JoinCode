namespace Core.Telemetry;

/// <summary>
/// 分析事件 — 记录到 .jcc/analytics/ 目录的 JSONL 文件
/// </summary>
public sealed record AnalyticsEvent
{
    /// <summary>事件名称（如 "tool.invoked"、"cache.break"）</summary>
    public required string Name { get; init; }
    /// <summary>时间戳（UTC ISO 8601）</summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>会话 ID</summary>
    public string? SessionId { get; init; }
    /// <summary>事件标签（键值对）</summary>
    public Dictionary<string, string> Tags { get; init; } = [];
    /// <summary>事件数值（可选，用于度量事件）</summary>
    public double? Value { get; init; }
}

/// <summary>
/// 分析事件汇熔断开关 — 控制事件写入的启用/禁用和采样率
/// </summary>
public sealed class AnalyticsSinkKillswitch
{
    private volatile bool _enabled = true;
    private double _sampleRate = 1.0;
    private long _droppedCount;
    private long _writtenCount;

    /// <summary>是否启用事件写入</summary>
    public bool IsEnabled => _enabled;

    /// <summary>采样率（0.0-1.0，1.0=全量写入）</summary>
    public double SampleRate => Volatile.Read(ref _sampleRate);

    /// <summary>已丢弃事件数（采样率过滤）</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>已写入事件数</summary>
    public long WrittenCount => Interlocked.Read(ref _writtenCount);

    /// <summary>启用/禁用事件写入</summary>
    public void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>设置采样率（0.0-1.0）</summary>
    public void SetSampleRate(double rate)
    {
        if (rate < 0) rate = 0;
        if (rate > 1) rate = 1;
        Volatile.Write(ref _sampleRate, rate);
    }

    /// <summary>检查事件是否应该写入（启用 + 采样率通过）</summary>
    public bool ShouldWrite()
    {
        if (!_enabled)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        var sampleRate = Volatile.Read(ref _sampleRate);
        if (sampleRate >= 1.0)
        {
            Interlocked.Increment(ref _writtenCount);
            return true;
        }

        if (sampleRate <= 0.0)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        var sampled = Random.Shared.NextDouble() < sampleRate;
        if (sampled)
        {
            Interlocked.Increment(ref _writtenCount);
        }
        else
        {
            Interlocked.Increment(ref _droppedCount);
        }

        return sampled;
    }
}

/// <summary>
/// 分析事件文件汇 — 批量写入事件到 .jcc/analytics/events.jsonl 文件
/// 使用 Channel&lt;T&gt; 异步队列，定时 flush，不阻塞调用方
/// </summary>
[Register(typeof(IAnalyticsFileSink), ServiceLifetime.Singleton)]
public sealed partial class AnalyticsFileSink : IAnalyticsFileSink, IAsyncDisposable
{
    private readonly IFileSystem? _fileSystem;
    private readonly ILogger<AnalyticsFileSink>? _logger;
    private readonly AnalyticsSinkKillswitch _killswitch;
    private readonly Channel<AnalyticsEvent> _channel;
    private readonly TimeSpan _flushInterval;
    private readonly int _batchSize;
    private readonly string _outputDirectory;
    private readonly CancellationTokenSource _cts;
    private Task? _flushTask;
    private int _isDisposed;
    private static int s_fileCounter;

    /// <summary>
    /// 创建 AnalyticsFileSink
    /// </summary>
    /// <param name="fileSystem">文件系统抽象（null 则不写入）</param>
    /// <param name="killswitch">熔断开关</param>
    /// <param name="logger">日志器</param>
    /// <param name="flushInterval">flush 间隔（默认 5s）</param>
    /// <param name="batchSize">批量写入大小（默认 100）</param>
    /// <param name="outputDirectory">输出目录（默认 .jcc/analytics）</param>
    public AnalyticsFileSink(
        IFileSystem? fileSystem = null,
        AnalyticsSinkKillswitch? killswitch = null,
        ILogger<AnalyticsFileSink>? logger = null,
        TimeSpan? flushInterval = null,
        int batchSize = 100,
        string outputDirectory = ".jcc/analytics")
    {
        _fileSystem = fileSystem;
        _killswitch = killswitch ?? new AnalyticsSinkKillswitch();
        _logger = logger;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _batchSize = batchSize;
        _outputDirectory = outputDirectory;
        _channel = Channel.CreateBounded<AnalyticsEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();

        if (_fileSystem is not null)
        {
            _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
        }
    }

    /// <inheritdoc />
    public AnalyticsSinkKillswitch Killswitch => _killswitch;

    /// <inheritdoc />
    public void LogEvent(AnalyticsEvent @event)
    {
        if (_fileSystem is null || !_killswitch.ShouldWrite())
        {
            return;
        }

        if (!_channel.Writer.TryWrite(@event))
        {
            _logger?.LogDebug("分析事件队列已满，丢弃事件: {EventName}", @event.Name);
        }
    }

    /// <inheritdoc />
    public void LogEvent(string name, Dictionary<string, string>? tags = null, double? value = null, string? sessionId = null)
    {
        LogEvent(new AnalyticsEvent
        {
            Name = name,
            Timestamp = DateTimeOffset.UtcNow,
            Tags = tags ?? [],
            Value = value,
            SessionId = sessionId
        });
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_fileSystem is null)
        {
            return;
        }

        var events = new List<AnalyticsEvent>(_batchSize);
        while (_channel.Reader.TryRead(out var evt))
        {
            events.Add(evt);
            if (events.Count >= _batchSize)
            {
                await WriteBatchAsync(events, cancellationToken).ConfigureAwait(false);
                events.Clear();
            }
        }

        if (events.Count > 0)
        {
            await WriteBatchAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, ct).ConfigureAwait(false);
                await FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "分析事件 flush 循环异常");
            }
        }

        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "分析事件最终 flush 异常");
        }
    }

    private async Task WriteBatchAsync(List<AnalyticsEvent> events, CancellationToken ct)
    {
        if (events.Count == 0 || _fileSystem is null)
        {
            return;
        }

        try
        {
            if (!_fileSystem.DirectoryExists(_outputDirectory))
            {
                _fileSystem.CreateDirectory(_outputDirectory);
            }

            var counter = Interlocked.Increment(ref s_fileCounter);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"events-{timestamp}-{counter}.jsonl";
            var filePath = _fileSystem.CombinePath(_outputDirectory, fileName);

            var sb = new StringBuilder(events.Count * 128);
            foreach (var evt in events)
            {
                var json = JsonSerializer.Serialize(evt, AnalyticsJsonContext.Default.AnalyticsEvent);
                sb.Append(json).Append('\n');
            }

            await _fileSystem.WriteAllTextAsync(filePath, sb.ToString(), ct).ConfigureAwait(false);
            _logger?.LogDebug("分析事件已写入: {FilePath} ({Count} events)", filePath, events.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "分析事件写入失败（不影响主流程）");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        if (_flushTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _flushTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "分析事件 flush 任务在 dispose 时异常");
            }
        }

        _cts.Dispose();
    }
}

/// <summary>
/// 分析事件文件汇接口
/// </summary>
public interface IAnalyticsFileSink : IAsyncDisposable
{
    /// <summary>熔断开关</summary>
    AnalyticsSinkKillswitch Killswitch { get; }

    /// <summary>记录事件</summary>
    void LogEvent(AnalyticsEvent @event);

    /// <summary>记录事件（便捷重载）</summary>
    void LogEvent(string name, Dictionary<string, string>? tags = null, double? value = null, string? sessionId = null);

    /// <summary>手动 flush 待写入事件</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 分析事件 JSON 序列化上下文 — AOT 友好
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AnalyticsEvent))]
public sealed partial class AnalyticsJsonContext : JsonSerializerContext;
