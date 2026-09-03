
namespace Services.SystemPower;

[Register(typeof(IPreventSleepService), ServiceLifetime.Singleton)]
public sealed partial class PreventSleepService : ServiceEntity, IPreventSleepService
{

    public PreventSleepService(ILogger<PreventSleepService>? logger = null, ITelemetryService? telemetryService = null)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ILogger<PreventSleepService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly AsyncLock _lock = new();
    private uint _previousExecutionState;
    private bool _isSleepPrevented;
    private bool _disposed;

    public bool IsSleepPrevented => _isSleepPrevented;

    public async Task<bool> PreventSleepAsync(SleepPreventionType type = SleepPreventionType.Continuous, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var guard = _lock.TryLock(cancellationToken) ?? throw new System.TimeoutException("锁等待超时");

        if (_isSleepPrevented)
        {
            _logger?.LogDebug(L.T(StringKey.PreventSleepAlreadyActive));
            return true;
        }

        var flags = type == SleepPreventionType.Continuous
            ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED
            : ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED;

        var result = SetThreadExecutionState(flags);
        if (result == 0)
        {
            _logger?.LogError(L.T(StringKey.PreventSleepSetStateFailed));
            return false;
        }

        _previousExecutionState = result;
        _isSleepPrevented = true;

        _logger?.LogInformation(L.T(StringKey.PreventSleepActivated), type);
        RecordSleepMetrics("prevent", true);
        return true;
    
    }

    public async Task<bool> AllowSleepAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var guard = _lock.TryLock(cancellationToken) ?? throw new System.TimeoutException("锁等待超时");

        if (!_isSleepPrevented)
        {
            return true;
        }

        var result = SetThreadExecutionState(ES_CONTINUOUS);
        if (result == 0)
        {
            _logger?.LogError(L.T(StringKey.PreventSleepRestoreFailed));
            return false;
        }

        _isSleepPrevented = false;

        _logger?.LogInformation(L.T(StringKey.PreventSleepDeactivated));
        RecordSleepMetrics("allow", true);
        return true;
    
    }

    protected override void OnDispose()
    {
        if (_disposed) return;

        if (_isSleepPrevented)
        {
            SetThreadExecutionState(ES_CONTINUOUS);
            _isSleepPrevented = false;
        }

        _lock.Dispose();
        _disposed = true;
    }

    [global::System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    private void RecordSleepMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("sleep.prevention.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Sleep prevention operation count");
}
