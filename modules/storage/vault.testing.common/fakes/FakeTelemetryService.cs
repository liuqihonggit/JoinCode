
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用遥测服务 — 记录计数与直方图调用，便于验证业务指标。
/// </summary>
public sealed class FakeTelemetryService : ITelemetryService {
    private readonly List<CounterRecord> _counters = new();
    private readonly List<HistogramRecord> _histograms = new();

    /// <summary>获取计数器记录列表。</summary>
    public IReadOnlyList<CounterRecord> Counters => _counters;

    /// <summary>获取直方图记录列表。</summary>
    public IReadOnlyList<HistogramRecord> Histograms => _histograms;

    /// <summary>获取遥测配置。</summary>
    public TelemetryConfig Config => new();

    /// <summary>获取是否启用链路追踪。</summary>
    public bool IsTracingEnabled => false;

    /// <summary>获取是否启用指标采集。</summary>
    public bool IsMetricsEnabled => true;

    /// <summary>启动新的遥测跨度。</summary>
    public ITelemetrySpan StartSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal, ITelemetrySpan? parent = null)
        => new NullTelemetrySpan();

    /// <summary>获取或创建计数器。</summary>
    public ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null)
        => new FakeCounter(this, name);

    /// <summary>获取或创建直方图。</summary>
    public ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null)
        => new FakeHistogram(this, name);

    /// <summary>获取或创建仪表。</summary>
    public ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null)
        => new FakeGauge(name);

    /// <summary>获取活动跨度列表。</summary>
    public IEnumerable<TelemetrySpanData> GetActiveSpans() => Array.Empty<TelemetrySpanData>();

    /// <summary>获取已注册的指标名称列表。</summary>
    public string[] GetRegisteredMetrics() => Array.Empty<string>();

    /// <summary>异步释放资源。</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void AddCounter(string name, double value, Dictionary<string, string>? tags)
        => _counters.Add(new CounterRecord(name, value, tags));

    private void AddHistogram(string name, double value, Dictionary<string, string>? tags)
        => _histograms.Add(new HistogramRecord(name, value, tags));

    public sealed record CounterRecord(string Name, double Value, Dictionary<string, string>? Tags);

    public sealed record HistogramRecord(string Name, double Value, Dictionary<string, string>? Tags);

    private sealed class FakeCounter : ITelemetryCounter {
        private readonly FakeTelemetryService _service;

        /// <summary>构造计数器实例。</summary>
        /// <param name="service">所属遥测服务。</param>
        /// <param name="name">计数器名称。</param>
        public FakeCounter(FakeTelemetryService service, string name) {
            _service = service;
            Name = name;
        }

        /// <summary>获取计数器名称。</summary>
        public string Name { get; }

        /// <summary>累加计数器值。</summary>
        public void Add(double value, Dictionary<string, string>? tags = null)
            => _service.AddCounter(Name, value, tags);
    }

    private sealed class FakeHistogram : ITelemetryHistogram {
        private readonly FakeTelemetryService _service;

        /// <summary>构造直方图实例。</summary>
        /// <param name="service">所属遥测服务。</param>
        /// <param name="name">直方图名称。</param>
        public FakeHistogram(FakeTelemetryService service, string name) {
            _service = service;
            Name = name;
        }

        /// <summary>获取直方图名称。</summary>
        public string Name { get; }

        /// <summary>记录直方图观测值。</summary>
        public void Record(double value, Dictionary<string, string>? tags = null)
            => _service.AddHistogram(Name, value, tags);
    }

    private sealed class FakeGauge : ITelemetryGauge {
        /// <summary>构造仪表实例。</summary>
        /// <param name="name">仪表名称。</param>
        public FakeGauge(string name) => Name = name;

        /// <summary>获取仪表名称。</summary>
        public string Name { get; }

        /// <summary>记录仪表观测值。</summary>
        public void Record(double value, Dictionary<string, string>? tags = null) {
        }
    }

    private sealed class NullTelemetrySpan : ITelemetrySpan {
        /// <summary>获取跨度标识。</summary>
        public string SpanId => Guid.Empty.ToString();

        /// <summary>获取追踪标识。</summary>
        public string TraceId => Guid.Empty.ToString();

        /// <summary>获取父跨度标识。</summary>
        public string? ParentSpanId => null;

        /// <summary>获取跨度名称。</summary>
        public string Name => string.Empty;

        /// <summary>获取跨度类型。</summary>
        public TelemetrySpanKind Kind => TelemetrySpanKind.Internal;

        /// <summary>获取跨度状态码。</summary>
        public TelemetryStatusCode Status => TelemetryStatusCode.Unset;

        /// <summary>获取是否正在记录。</summary>
        public bool IsRecording => false;

        /// <summary>设置跨度状态。</summary>
        public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null) => this;

        /// <summary>设置字符串标签。</summary>
        public ITelemetrySpan SetTag(string key, string value) => this;

        /// <summary>设置双精度标签。</summary>
        public ITelemetrySpan SetTag(string key, double value) => this;

        /// <summary>设置布尔标签。</summary>
        public ITelemetrySpan SetTag(string key, bool value) => this;

        /// <summary>添加跨度事件。</summary>
        public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null) => this;

        /// <summary>记录异常。</summary>
        public ITelemetrySpan RecordException(Exception exception) => this;

        /// <summary>启动子跨度。</summary>
        public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal) => this;

        /// <summary>转换为跨度数据。</summary>
        public TelemetrySpanData ToSpanData() => new();

        /// <summary>异步释放资源。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
