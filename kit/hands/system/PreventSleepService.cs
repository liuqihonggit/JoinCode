
namespace Services.SystemPower;

/// <summary>
/// 防睡眠服务 — 通过 SetThreadExecutionState 阻止系统进入睡眠状态
/// <para>单例服务,线程安全(AsyncLock 保护),支持连续防睡眠与一次性防睡眠两种模式</para>
/// <para>Dispose 时自动恢复系统默认执行状态</para>
/// </summary>
[Register(typeof(IPreventSleepService), ServiceLifetime.Singleton)]
public sealed partial class PreventSleepService : ServiceEntity, IPreventSleepService {

    /// <summary>
    /// 初始化防睡眠服务实例
    /// </summary>
    /// <param name="logger">日志记录器,为 null 时静默运行</param>
    /// <param name="telemetryService">遥测服务,为 null 时不记录指标</param>
    public PreventSleepService(ILogger<PreventSleepService>? logger = null, ITelemetryService? telemetryService = null) {
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ILogger<PreventSleepService>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly AsyncLock _lock = new();
    private uint _previousExecutionState;
    private bool _isSleepPrevented;
    private bool _disposed;

    /// <summary>
    /// 获取当前是否已阻止系统睡眠
    /// </summary>
    public bool IsSleepPrevented => _isSleepPrevented;

    /// <summary>
    /// 阻止系统进入睡眠状态 — 根据 <paramref name="type"/> 设置对应的执行状态标志
    /// <para>若已处于防睡眠状态,直接返回 true(幂等)</para>
    /// </summary>
    /// <param name="type">防睡眠类型,默认为 Continuous(连续防睡眠)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功阻止返回 true;SetThreadExecutionState 失败返回 false</returns>
    public async Task<bool> PreventSleepAsync(SleepPreventionType type = SleepPreventionType.Continuous, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (_isSleepPrevented) {
            _logger?.LogDebug(L.T(StringKey.PreventSleepAlreadyActive));
            return true;
        }

        var flags = type == SleepPreventionType.Continuous
            ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED
            : ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED;

        var result = SetThreadExecutionState(flags);
        if (result == 0) {
            _logger?.LogError(L.T(StringKey.PreventSleepSetStateFailed));
            return false;
        }

        _previousExecutionState = result;
        _isSleepPrevented = true;

        _logger?.LogInformation(L.T(StringKey.PreventSleepActivated), type);
        RecordSleepMetrics("prevent", true);
        return true;

    }

    /// <summary>
    /// 恢复系统默认执行状态,允许系统进入睡眠
    /// <para>若未处于防睡眠状态,直接返回 true(幂等)</para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功恢复返回 true;SetThreadExecutionState 失败返回 false</returns>
    public async Task<bool> AllowSleepAsync(CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var guard = await _lock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时");

        if (!_isSleepPrevented) {
            return true;
        }

        var result = SetThreadExecutionState(ES_CONTINUOUS);
        if (result == 0) {
            _logger?.LogError(L.T(StringKey.PreventSleepRestoreFailed));
            return false;
        }

        _isSleepPrevented = false;

        _logger?.LogInformation(L.T(StringKey.PreventSleepDeactivated));
        RecordSleepMetrics("allow", true);
        return true;

    }

    /// <summary>
    /// 释放资源 — 若当前处于防睡眠状态,恢复系统默认执行状态并释放锁
    /// </summary>
    public override void Dispose() {
        if (_disposed) return;

        if (_isSleepPrevented) {
            SetThreadExecutionState(ES_CONTINUOUS);
            _isSleepPrevented = false;
        }

        _lock.Dispose();
        _disposed = true;
        base.Dispose();
    }

    [global::System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    private void RecordSleepMetrics(string operation, bool isSuccess)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "sleep.prevention.count", operation, isSuccess, "Sleep prevention operation count");
}