
namespace McpClient;

public sealed partial class McpSecureTokenStorage
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<McpSecureTokenStorage>? _logger;
    private readonly string _storagePath;
    private readonly byte[] _encryptionKey;

    public McpSecureTokenStorage(IFileSystem fs, string storagePath, ILogger<McpSecureTokenStorage>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        _fs = fs;
        _storagePath = storagePath;
        _logger = logger;
        _encryptionKey = DeriveKey();
    }

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
            var json = await _fs.ReadAllTextAsync(_storagePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, McpClientJsonContext.Default.DictionaryStringString)
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