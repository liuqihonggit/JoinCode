using JoinCode.Abstractions.Attributes;

namespace Core.Bridge;

/// <summary>
/// 工作密钥条目 - 记录加密的 API 密钥或工作秘密
/// </summary>
public sealed partial class WorkSecretEntry
{
    /// <summary>密钥唯一标识</summary>
    [JsonPropertyName("secretId")]
    public required string SecretId { get; init; }

    /// <summary>密钥名称（可读标识）</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>加密后的密钥值（Base64 编码的 AES-GCM 密文）</summary>
    [JsonPropertyName("encryptedValue")]
    public required string EncryptedValue { get; init; }

    /// <summary>创建时间（UTC）</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>过期时间（UTC），null 表示永不过期</summary>
    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>是否已撤销</summary>
    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; set; }

    /// <summary>是否已轮换（被新密钥替代）</summary>
    [JsonPropertyName("isRotated")]
    public bool IsRotated { get; set; }

    /// <summary>轮换后的新密钥 ID</summary>
    [JsonPropertyName("rotatedToSecretId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RotatedToSecretId { get; set; }
}

/// <summary>
/// 工作密钥存储接口 - 密钥管理抽象
/// </summary>
public interface IWorkSecretStore
{
    /// <summary>创建新的工作密钥</summary>
    ValueTask<WorkSecretEntry> CreateAsync(string name, string plainValue, DateTimeOffset? expiresAt = null, CancellationToken ct = default);

    /// <summary>获取指定密钥条目</summary>
    ValueTask<WorkSecretEntry?> GetAsync(string secretId, CancellationToken ct = default);

    /// <summary>轮换密钥 - 生成新密钥并标记旧密钥为已轮换</summary>
    ValueTask<WorkSecretEntry> RotateAsync(string secretId, string newPlainValue, DateTimeOffset? expiresAt = null, CancellationToken ct = default);

    /// <summary>撤销密钥</summary>
    ValueTask<bool> RevokeAsync(string secretId, CancellationToken ct = default);

    /// <summary>验证密钥 - 检查未过期、未撤销且值匹配</summary>
    ValueTask<bool> ValidateAsync(string secretId, string plainValue, CancellationToken ct = default);
}

/// <summary>
/// 工作密钥存储 - 基于 ConcurrentDictionary 的线程安全内存实现
/// 使用 AES-GCM 加密密钥值，兼容 NativeAOT
/// </summary>
[Register]
public sealed partial class WorkSecretStore : IWorkSecretStore, IDisposable
{
    private readonly ConcurrentDictionary<string, WorkSecretEntry> _secrets;
    [Inject] private readonly ILogger<WorkSecretStore>? _logger;
    private readonly IClockService _clock;
    private readonly byte[] _encryptionKey;

    // AES-GCM 常量
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32; // AES-256

    public WorkSecretStore(BridgeConfig? config = null, ILogger<WorkSecretStore>? logger = null, IClockService? clock = null)
    {
        var keyBytes = string.IsNullOrEmpty(config?.EncryptionKeyBase64)
            ? RandomNumberGenerator.GetBytes(KeySizeBytes)
            : Convert.FromBase64String(config.EncryptionKeyBase64);

        if (keyBytes.Length != KeySizeBytes)
            throw new ArgumentException($"加密密钥长度必须为 {KeySizeBytes} 字节", nameof(config));

        _encryptionKey = keyBytes.ToArray(); // 防御性拷贝
        _secrets = new ConcurrentDictionary<string, WorkSecretEntry>(StringComparer.Ordinal);
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public ValueTask<WorkSecretEntry> CreateAsync(string name, string plainValue, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainValue);

        var secretId = Guid.NewGuid().ToString("N");
        var encryptedValue = Encrypt(plainValue);

        var entry = new WorkSecretEntry
        {
            SecretId = secretId,
            Name = name,
            EncryptedValue = encryptedValue,
            ExpiresAt = expiresAt
        };

        if (!_secrets.TryAdd(secretId, entry))
        {
            throw new InvalidOperationException($"密钥 ID 冲突: {secretId}");
        }

        _logger?.LogInformation("[WorkSecret] 创建密钥: {Name} (ID: {SecretId})", name, secretId);
        return new ValueTask<WorkSecretEntry>(entry);
    }

    /// <inheritdoc />
    public ValueTask<WorkSecretEntry?> GetAsync(string secretId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        _secrets.TryGetValue(secretId, out var entry);
        return new ValueTask<WorkSecretEntry?>(entry);
    }

    /// <inheritdoc />
    public ValueTask<WorkSecretEntry> RotateAsync(string secretId, string newPlainValue, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPlainValue);

        if (!_secrets.TryGetValue(secretId, out var oldEntry))
        {
            throw new InvalidOperationException($"密钥不存在: {secretId}");
        }

        if (oldEntry.IsRevoked)
        {
            throw new InvalidOperationException($"密钥已撤销，无法轮换: {secretId}");
        }

        // 创建新密钥
        var newSecretId = Guid.NewGuid().ToString("N");
        var encryptedValue = Encrypt(newPlainValue);

        var newEntry = new WorkSecretEntry
        {
            SecretId = newSecretId,
            Name = oldEntry.Name,
            EncryptedValue = encryptedValue,
            ExpiresAt = expiresAt ?? oldEntry.ExpiresAt
        };

        if (!_secrets.TryAdd(newSecretId, newEntry))
        {
            throw new InvalidOperationException($"密钥 ID 冲突: {newSecretId}");
        }

        // 标记旧密钥为已轮换
        oldEntry.IsRotated = true;
        oldEntry.RotatedToSecretId = newSecretId;

        _logger?.LogInformation("[WorkSecret] 轮换密钥: {OldId} -> {NewId}", secretId, newSecretId);
        return new ValueTask<WorkSecretEntry>(newEntry);
    }

    /// <inheritdoc />
    public ValueTask<bool> RevokeAsync(string secretId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        if (!_secrets.TryGetValue(secretId, out var entry))
        {
            _logger?.LogWarning("[WorkSecret] 密钥不存在，无法撤销: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }

        if (entry.IsRevoked)
        {
            _logger?.LogWarning("[WorkSecret] 密钥已被撤销: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }

        entry.IsRevoked = true;
        _logger?.LogInformation("[WorkSecret] 撤销密钥: {SecretId}", secretId);
        return new ValueTask<bool>(true);
    }

    /// <inheritdoc />
    public ValueTask<bool> ValidateAsync(string secretId, string plainValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentException.ThrowIfNullOrWhiteSpace(plainValue);

        if (!_secrets.TryGetValue(secretId, out var entry))
        {
            _logger?.LogWarning("[WorkSecret] 密钥不存在: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }

        // 检查是否已撤销
        if (entry.IsRevoked)
        {
            _logger?.LogWarning("[WorkSecret] 密钥已撤销: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }

        // 检查是否已过期
        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < _clock.GetUtcNowOffset())
        {
            _logger?.LogWarning("[WorkSecret] 密钥已过期: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }

        // 解密并比对值
        try
        {
            var decryptedValue = Decrypt(entry.EncryptedValue);
            var isValid = string.Equals(decryptedValue, plainValue, StringComparison.Ordinal);

            if (!isValid)
            {
                _logger?.LogWarning("[WorkSecret] 密钥值不匹配: {SecretId}", secretId);
            }

            return new ValueTask<bool>(isValid);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[WorkSecret] 解密失败: {SecretId}", secretId);
            return new ValueTask<bool>(false);
        }
    }

    /// <summary>
    /// 使用 AES-GCM 加密明文
    /// 输出格式: Base64(nonce || tag || ciphertext)
    /// </summary>
    private string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_encryptionKey, TagSizeBytes);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // 拼接: nonce(12) + tag(16) + ciphertext
        var result = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// 使用 AES-GCM 解密密文
    /// 输入格式: Base64(nonce || tag || ciphertext)
    /// </summary>
    private string Decrypt(string encryptedBase64)
    {
        var data = Convert.FromBase64String(encryptedBase64);

        if (data.Length < NonceSizeBytes + TagSizeBytes)
            throw new FormatException("加密数据格式无效：数据过短");

        var nonce = new byte[NonceSizeBytes];
        Buffer.BlockCopy(data, 0, nonce, 0, NonceSizeBytes);

        var tag = new byte[TagSizeBytes];
        Buffer.BlockCopy(data, NonceSizeBytes, tag, 0, TagSizeBytes);

        var cipherBytes = new byte[data.Length - NonceSizeBytes - TagSizeBytes];
        Buffer.BlockCopy(data, NonceSizeBytes + TagSizeBytes, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_encryptionKey, TagSizeBytes);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public void Dispose()
    {
        Array.Clear(_encryptionKey);
    }
}
