
namespace Core.Telemetry;

[Register]
public sealed partial class TelemetryService : ITelemetryService, IDisposable
{
    private readonly TelemetryConfig _config;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ActivityListener _listener;
    private readonly ConsoleTelemetryExporter? _consoleExporter;
    private readonly ConcurrentDictionary<string, ITelemetryCounter> _counters = new();
    private readonly ConcurrentDictionary<string, ITelemetryHistogram> _histograms = new();
    private readonly ConcurrentDictionary<string, ITelemetryGauge> _gauges = new();
    private readonly ConcurrentDictionary<string, TelemetrySpan> _activeSpans = new();
    private int _isDisposed;

    public TelemetryConfig Config => _config;
    public bool IsTracingEnabled => _config.TracingEnabled;
    public bool IsMetricsEnabled => _config.MetricsEnabled;

    public TelemetryService(TelemetryConfig config, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _activitySource = new ActivitySource(config.ServiceName, config.ServiceVersion);
        _meter = new Meter(config.ServiceName, config.ServiceVersion);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == config.ServiceName,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);

        // Console 导出器：当 ExportFormat == Console 时，将 span 信息输出到日志
        if (config.ExportFormat == TelemetryExportFormat.Console)
        {
            _consoleExporter = new ConsoleTelemetryExporter(config.ServiceName, logger);
        }
    }

    public ITelemetrySpan StartSpan(
        string name,
        TelemetrySpanKind kind = TelemetrySpanKind.Internal,
        ITelemetrySpan? parent = null)
    {
        if (!_config.TracingEnabled)
        {
            return new NoOpTelemetrySpan(name, kind);
        }

        var activityKind = MapActivityKind(kind);
        var parentActivity = parent is TelemetrySpan realSpan
            ? realSpan.UnderlyingActivity
            : null;

        var activity = parentActivity != null
            ? _activitySource.StartActivity(name, activityKind, parentActivity.Context)
            : _activitySource.StartActivity(name, activityKind);

        if (activity == null)
        {
            return new NoOpTelemetrySpan(name, kind);
        }

        foreach (var (key, value) in _config.DefaultTags)
        {
            activity.SetTag(key, value);
        }

        var span = new TelemetrySpan(activity, kind, this);
        _activeSpans[activity.SpanId.ToString()] = span;
        return span;
    }

    public ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null)
    {
        return _counters.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryCounter(n);
            }

            var counter = _meter.CreateCounter<double>(n, unit, description);
            return new TelemetryCounter(n, counter);
        });
    }

    public ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null)
    {
        return _histograms.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryHistogram(n);
            }

            var histogram = _meter.CreateHistogram<double>(n, unit, description);
            return new TelemetryHistogram(n, histogram);
        });
    }

    public ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null)
    {
        return _gauges.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryGauge(n);
            }

            var gauge = _meter.CreateGauge<double>(n, unit, description);
            return new TelemetryGauge(n, gauge);
        });
    }

    public IReadOnlyList<TelemetrySpanData> GetActiveSpans()
    {
        return _activeSpans.Values
            .Select(s => s.ToSpanData())
            .ToList();
    }

    public IReadOnlyList<string> GetRegisteredMetrics()
    {
        var names = new List<string>();
        names.AddRange(_counters.Keys);
        names.AddRange(_histograms.Keys);
        names.AddRange(_gauges.Keys);
        return names;
    }

    internal void RemoveActiveSpan(string spanId)
    {
        _activeSpans.TryRemove(spanId, out _);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        foreach (var span in _activeSpans.Values.ToList())
        {
            span.Dispose();
        }

        _consoleExporter?.Dispose();
        _listener.Dispose();
        _activitySource.Dispose();
        _meter.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await Task.WhenAll(_activeSpans.Values.ToList().Select(span => span.DisposeAsync().AsTask())).ConfigureAwait(false);

        _consoleExporter?.Dispose();
        _listener.Dispose();
        _activitySource.Dispose();
        _meter.Dispose();
    }

    private static ActivityKind MapActivityKind(TelemetrySpanKind kind) => kind switch
    {
        TelemetrySpanKind.Internal => ActivityKind.Internal,
        TelemetrySpanKind.Server => ActivityKind.Server,
        TelemetrySpanKind.Client => ActivityKind.Client,
        TelemetrySpanKind.Producer => ActivityKind.Producer,
        TelemetrySpanKind.Consumer => ActivityKind.Consumer,
        _ => ActivityKind.Internal
    };
}
