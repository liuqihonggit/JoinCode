
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用遥测服务 — 记录计数与直方图调用，便于验证业务指标。
/// </summary>
public sealed class FakeTelemetryService : ITelemetryService
{
    private readonly List<CounterRecord> _counters = new();
    private readonly List<HistogramRecord> _histograms = new();

    public IReadOnlyList<CounterRecord> Counters => _counters;
    public IReadOnlyList<HistogramRecord> Histograms => _histograms;

    public TelemetryConfig Config => new();

    public bool IsTracingEnabled => false;

    public bool IsMetricsEnabled => true;

    public ITelemetrySpan StartSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal, ITelemetrySpan? parent = null)
        => new NullTelemetrySpan();

    public ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null)
        => new FakeCounter(this, name);

    public ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null)
        => new FakeHistogram(this, name);

    public ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null)
        => new FakeGauge(name);

    public IReadOnlyList<TelemetrySpanData> GetActiveSpans() => Array.Empty<TelemetrySpanData>();

    public IReadOnlyList<string> GetRegisteredMetrics() => Array.Empty<string>();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void AddCounter(string name, double value, Dictionary<string, string>? tags)
        => _counters.Add(new CounterRecord(name, value, tags));

    private void AddHistogram(string name, double value, Dictionary<string, string>? tags)
        => _histograms.Add(new HistogramRecord(name, value, tags));

    public sealed record CounterRecord(string Name, double Value, Dictionary<string, string>? Tags);

    public sealed record HistogramRecord(string Name, double Value, Dictionary<string, string>? Tags);

    private sealed class FakeCounter : ITelemetryCounter
    {
        private readonly FakeTelemetryService _service;

        public FakeCounter(FakeTelemetryService service, string name)
        {
            _service = service;
            Name = name;
        }

        public string Name { get; }

        public void Add(double value, Dictionary<string, string>? tags = null)
            => _service.AddCounter(Name, value, tags);
    }

    private sealed class FakeHistogram : ITelemetryHistogram
    {
        private readonly FakeTelemetryService _service;

        public FakeHistogram(FakeTelemetryService service, string name)
        {
            _service = service;
            Name = name;
        }

        public string Name { get; }

        public void Record(double value, Dictionary<string, string>? tags = null)
            => _service.AddHistogram(Name, value, tags);
    }

    private sealed class FakeGauge : ITelemetryGauge
    {
        public FakeGauge(string name) => Name = name;

        public string Name { get; }

        public void Record(double value, Dictionary<string, string>? tags = null)
        {
        }
    }

    private sealed class NullTelemetrySpan : ITelemetrySpan
    {
        public string SpanId => Guid.Empty.ToString();

        public string TraceId => Guid.Empty.ToString();

        public string? ParentSpanId => null;

        public string Name => string.Empty;

        public TelemetrySpanKind Kind => TelemetrySpanKind.Internal;

        public TelemetryStatusCode Status => TelemetryStatusCode.Unset;

        public bool IsRecording => false;

        public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null) => this;

        public ITelemetrySpan SetTag(string key, string value) => this;

        public ITelemetrySpan SetTag(string key, double value) => this;

        public ITelemetrySpan SetTag(string key, bool value) => this;

        public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null) => this;

        public ITelemetrySpan RecordException(Exception exception) => this;

        public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal) => this;

        public TelemetrySpanData ToSpanData() => new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
