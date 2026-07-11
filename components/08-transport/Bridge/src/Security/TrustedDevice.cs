
namespace Core.Bridge;

/// <summary>
/// 设备信任等级
/// </summary>
public enum DeviceTrustLevel
{
    /// <summary>不受信任</summary>
    None = 0,

    /// <summary>基础信任（仅限只读操作）</summary>
    Basic = 1,

    /// <summary>完全信任（读写操作）</summary>
    Full = 2
}

/// <summary>
/// 受信任设备条目 - 记录已授权设备信息
/// </summary>
public sealed partial class TrustedDeviceEntry
{
    /// <summary>设备唯一标识</summary>
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    /// <summary>设备名称</summary>
    [JsonPropertyName("deviceName")]
    public required string DeviceName { get; init; }

    /// <summary>公钥指纹（SHA-256）</summary>
    [JsonPropertyName("publicKeyFingerprint")]
    public required string PublicKeyFingerprint { get; init; }

    /// <summary>信任等级</summary>
    [JsonPropertyName("trustLevel")]
    public DeviceTrustLevel TrustLevel { get; init; } = DeviceTrustLevel.Basic;

    /// <summary>信任授予时间（UTC）</summary>
    [JsonPropertyName("trustedAt")]
    public DateTimeOffset TrustedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最后活跃时间（UTC）</summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已撤销</summary>
    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; set; }
}

/// <summary>
/// 受信任设备存储接口 - 设备信任管理抽象
/// </summary>
public interface ITrustedDeviceStore
{
    /// <summary>添加受信任设备</summary>
    ValueTask<TrustedDeviceEntry> AddAsync(TrustedDeviceEntry entry, CancellationToken ct = default);

    /// <summary>移除受信任设备</summary>
    ValueTask<bool> RemoveAsync(string deviceId, CancellationToken ct = default);

    /// <summary>获取指定设备</summary>
    ValueTask<TrustedDeviceEntry?> GetAsync(string deviceId, CancellationToken ct = default);

    /// <summary>获取所有受信任设备</summary>
    ValueTask<IReadOnlyList<TrustedDeviceEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>检查设备是否受信任且未撤销</summary>
    ValueTask<bool> IsTrustedAsync(string deviceId, CancellationToken ct = default);

    /// <summary>撤销设备信任</summary>
    ValueTask<bool> RevokeAsync(string deviceId, CancellationToken ct = default);
}

/// <summary>
/// 受信任设备存储 - 基于 ConcurrentDictionary 的线程安全内存实现
/// </summary>
[Register]
public sealed partial class TrustedDeviceStore : ITrustedDeviceStore
{
    private readonly ConcurrentDictionary<string, TrustedDeviceEntry> _devices;
    [Inject] private readonly ILogger<TrustedDeviceStore>? _logger;

    public TrustedDeviceStore(ILogger<TrustedDeviceStore>? logger = null)
    {
        _devices = new ConcurrentDictionary<string, TrustedDeviceEntry>(StringComparer.Ordinal);
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<TrustedDeviceEntry> AddAsync(TrustedDeviceEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrEmpty(entry.DeviceId))
            throw new ArgumentException("DeviceId 不能为空", nameof(entry));

        if (_devices.TryAdd(entry.DeviceId, entry))
        {
            _logger?.LogInformation("[TrustedDevice] 添加设备: {DeviceId} ({DeviceName}), 信任等级: {TrustLevel}",
                entry.DeviceId, entry.DeviceName, entry.TrustLevel);
            return new ValueTask<TrustedDeviceEntry>(entry);
        }

        throw new InvalidOperationException($"设备已存在: {entry.DeviceId}");
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var removed = _devices.TryRemove(deviceId, out var entry);
        if (removed)
        {
            _logger?.LogInformation("[TrustedDevice] 移除设备: {DeviceId}", deviceId);
        }
        else
        {
            _logger?.LogWarning("[TrustedDevice] 设备不存在，无法移除: {DeviceId}", deviceId);
        }

        return new ValueTask<bool>(removed);
    }

    /// <inheritdoc />
    public ValueTask<TrustedDeviceEntry?> GetAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _devices.TryGetValue(deviceId, out var entry);
        return new ValueTask<TrustedDeviceEntry?>(entry);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TrustedDeviceEntry>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TrustedDeviceEntry> result = _devices.Values.ToList();
        return new ValueTask<IReadOnlyList<TrustedDeviceEntry>>(result);
    }

    /// <inheritdoc />
    public ValueTask<bool> IsTrustedAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var isTrusted = _devices.TryGetValue(deviceId, out var entry) &&
                        !entry.IsRevoked &&
                        entry.TrustLevel != DeviceTrustLevel.None;

        return new ValueTask<bool>(isTrusted);
    }

    /// <inheritdoc />
    public ValueTask<bool> RevokeAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (!_devices.TryGetValue(deviceId, out var entry))
        {
            _logger?.LogWarning("[TrustedDevice] 设备不存在，无法撤销: {DeviceId}", deviceId);
            return new ValueTask<bool>(false);
        }

        if (entry.IsRevoked)
        {
            _logger?.LogWarning("[TrustedDevice] 设备已被撤销: {DeviceId}", deviceId);
            return new ValueTask<bool>(false);
        }

        entry.IsRevoked = true;
        _logger?.LogInformation("[TrustedDevice] 撤销设备信任: {DeviceId}", deviceId);
        return new ValueTask<bool>(true);
    }
}
