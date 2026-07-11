
namespace Core.Telemetry;

public sealed class TelemetrySpan : ITelemetrySpan, IDisposable
{
    private readonly Activity _activity;
    private readonly TelemetrySpanKind _kind;
    private readonly TelemetryService _service;
    private readonly List<TelemetrySpanEvent> _events = [];
    private int _isDisposed;

    public Activity UnderlyingActivity => _activity;

    public string SpanId => _activity.SpanId.ToString();
    public string TraceId => _activity.TraceId.ToString();
    public string? ParentSpanId => _activity.ParentSpanId.ToString();
    public string Name => _activity.DisplayName;
    public TelemetrySpanKind Kind => _kind;
    public TelemetryStatusCode Status { get; private set; } = TelemetryStatusCode.Unset;
    public string? StatusDescription { get; private set; }
    public bool IsRecording => _isDisposed == 0 && _activity.IsAllDataRequested;

    internal TelemetrySpan(Activity activity, TelemetrySpanKind kind, TelemetryService service)
    {
        _activity = activity;
        _kind = kind;
        _service = service;
    }

    public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null)
    {
        Status = statusCode;
        StatusDescription = description;
        _activity.SetStatus(MapActivityStatus(statusCode), description);
        return this;
    }

    public ITelemetrySpan SetTag(string key, string value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    public ITelemetrySpan SetTag(string key, double value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    public ITelemetrySpan SetTag(string key, bool value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null)
    {
        var evt = new TelemetrySpanEvent
        {
            Name = name,
            Timestamp = DateTimeOffset.UtcNow,
            Tags = tags ?? []
        };
        _events.Add(evt);

        var activityEvent = new ActivityEvent(name);
        _activity.AddEvent(activityEvent);
        return this;
    }

    public ITelemetrySpan RecordException(Exception exception)
    {
        _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity.AddEvent(new ActivityEvent("exception",
            tags: new ActivityTagsCollection
            {
                ["exception.type"] = exception.GetType().FullName,
                ["exception.message"] = exception.Message,
                ["exception.stacktrace"] = exception.StackTrace ?? string.Empty
            }));
        Status = TelemetryStatusCode.Error;
        return this;
    }

    public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal)
    {
        return _service.StartSpan(name, kind, this);
    }

    public TelemetrySpanData ToSpanData()
    {
        return new TelemetrySpanData
        {
            Name = _activity.DisplayName,
            SpanId = _activity.SpanId.ToString(),
            TraceId = _activity.TraceId.ToString(),
            ParentSpanId = _activity.ParentSpanId.ToString(),
            Kind = _kind,
            Status = Status,
            StatusDescription = StatusDescription,
            StartTime = _activity.StartTimeUtc,
            EndTime = _activity.StartTimeUtc + _activity.Duration,
            Duration = _activity.Duration,
            Tags = _activity.Tags?.ToDictionary(t => t.Key, t => t.Value?.ToString() ?? string.Empty) ?? [],
            Events = _events.ToList()
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _activity.Dispose();
        _service.RemoveActiveSpan(SpanId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _activity.Dispose();
        _service.RemoveActiveSpan(SpanId);
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private static ActivityStatusCode MapActivityStatus(TelemetryStatusCode statusCode) => statusCode switch
    {
        TelemetryStatusCode.Ok => ActivityStatusCode.Ok,
        TelemetryStatusCode.Error => ActivityStatusCode.Error,
        _ => ActivityStatusCode.Unset
    };
}
