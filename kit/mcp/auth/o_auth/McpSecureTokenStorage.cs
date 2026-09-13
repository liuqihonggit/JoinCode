
namespace McpClient;

/// <summary>
/// 安全令牌存储 — AES 加密持久化 OAuth 令牌到本地文件
/// <para>密钥派生自机器名+用户名（PBKDF2-SHA256），跨机器/用户隔离</para>
/// </summary>
public sealed partial class McpSecureTokenStorage
{
    private readonly IFileSystem _fs;
    private readonly ILogger<McpSecureTokenStorage>? _logger;
    private readonly string _storagePath;
    private readonly byte[] _encryptionKey;

    /// <summary>
    /// 创建 McpSecureTokenStorage 实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="storagePath">存储文件路径</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpSecureTokenStorage(IFileSystem fs, string storagePath, ILogger<McpSecureTokenStorage>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        _fs = fs;
        _storagePath = storagePath;
        _logger = logger;
        _encryptionKey = DeriveKey();
    }

    /// <summary>
    /// 异步保存键值对（值经 AES 加密后持久化）
    /// </summary>
    /// <param name="key">键名</param>
    /// <param name="value">明文值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var data = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        data[key] = Encrypt(value);

        var json = JsonSerializer.Serialize(data, McpClientJsonContext.Default.DictionaryStringString);
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fs.CreateDirectory(directory);
        }

        await _fs.WriteAllTextAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("安全存储已保存: {Key}", key);
    }

    /// <summary>
    /// 异步加载并解密指定键的值
    /// </summary>
    /// <param name="key">键名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解密后的明文值；键不存在或解密失败返回 null</returns>
    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var data = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        if (!data.TryGetValue(key, out var encrypted))
        {
            return null;
        }

        try
        {
            return Decrypt(encrypted);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "解密安全存储值失败: {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// 异步删除指定键值对
    /// </summary>
    /// <param name="key">键名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除成功返回 true；键不存在返回 false</returns>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var data = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        if (!data.Remove(key))
        {
            return false;
        }

        var json = JsonSerializer.Serialize(data, McpClientJsonContext.Default.DictionaryStringString);
        await _fs.WriteAllTextAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!_fs.FileExists(_storagePath))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return await _fs.ReadAndDeserializeAsync(_storagePath, McpClientJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private string Encrypt(string plaintext)
    {
        var iv = new byte[16];
        RandomNumberGenerator.Fill(iv);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        var result = new byte[iv.Length + ciphertext.Length];
        iv.CopyTo(result, 0);
        ciphertext.CopyTo(result, iv.Length);

        return Convert.ToBase64String(result);
    }

    private string Decrypt(string encrypted)
    {
        var data = Convert.FromBase64String(encrypted);

        var iv = new byte[16];
        var ciphertext = new byte[data.Length - 16];
        Array.Copy(data, 0, iv, 0, 16);
        Array.Copy(data, 16, ciphertext, 0, ciphertext.Length);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static byte[] DeriveKey()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var salt = Encoding.UTF8.GetBytes($"MCP-SecureStorage-{machineName}-{userName}");
        var password = Encoding.UTF8.GetBytes("JoinCode-MCP-Token-Storage-Key");

        return Pbkdf2DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256, 32);
    }

#pragma warning disable SYSLIB0060
    private static byte[] Pbkdf2DeriveBytes(byte[] password, byte[] salt, int iterations, HashAlgorithmName hashAlgorithm, int outputLength)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations, hashAlgorithm);
        return deriveBytes.GetBytes(outputLength);
    }
#pragma warning restore SYSLIB0060
}