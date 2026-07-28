
namespace Services.OAuth;

/// <summary>
/// Token 存储接口
/// 安全地存储和检索 OAuth Token
/// </summary>
public interface ITokenStorage
{
    /// <summary>
    /// 保存 Token
    /// </summary>
    Task SaveTokenAsync(string provider, OAuthToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载 Token
    /// </summary>
    Task<OAuthToken?> LoadTokenAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除 Token
    /// </summary>
    Task DeleteTokenAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有存储的提供商
    /// </summary>
    Task<IReadOnlyList<string>> GetStoredProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否有存储的 Token
    /// </summary>
    Task<bool> HasTokenAsync(string provider, CancellationToken cancellationToken = default);
}

/// <summary>
/// OAuth Token 信息
/// </summary>
public sealed record OAuthToken
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlyList<string> Scope { get; init; } = Array.Empty<string>();
    public DateTimeOffset ObtainedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    public TimeSpan GetRemainingTime()
    {
        var remaining = ExpiresAt - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

/// <summary>
/// Token 存储实现
/// </summary>
[Register]
public sealed partial class TokenStorage : ITokenStorage
{
    private readonly string _storagePath;
    [Inject] private readonly ILogger<TokenStorage>? _logger;
    private readonly IFileSystem _fs;

    public TokenStorage(IFileSystem fs, string? storagePath = null, ILogger<TokenStorage>? logger = null)
    {
        _fs = fs;
        _storagePath = storagePath ?? GetDefaultStoragePath();
        _logger = logger;

        EnsureStorageDirectory();
    }

    public async Task SaveTokenAsync(string provider, OAuthToken token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentNullException.ThrowIfNull(token);

        var filePath = GetTokenFilePath(provider);
        var json = JsonSerializer.Serialize(token, OAuthTokenJsonContext.Default.OAuthToken);
        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Token saved for provider: {Provider}", provider);
    }

    public async Task<OAuthToken?> LoadTokenAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);

        var filePath = GetTokenFilePath(provider);

        if (!_fs.FileExists(filePath))
            return null;

        try
        {
            return await _fs.ReadAndDeserializeAsync(filePath, OAuthTokenJsonContext.Default.OAuthToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load token for provider: {Provider}", provider);
            return null;
        }
    }

    public Task DeleteTokenAsync(string provider, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);

        var filePath = GetTokenFilePath(provider);

        if (_fs.FileExists(filePath))
        {
            _fs.DeleteFile(filePath);
            _logger?.LogInformation("Token deleted for provider: {Provider}", provider);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetStoredProvidersAsync(CancellationToken cancellationToken = default)
    {
        if (!_fs.DirectoryExists(_storagePath))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var files = _fs.GetFiles(_storagePath, "*.token", SearchOption.TopDirectoryOnly);

        var providers = files
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(providers);
    }

    public async Task<bool> HasTokenAsync(string provider, CancellationToken cancellationToken = default)
    {
        var token = await LoadTokenAsync(provider, cancellationToken).ConfigureAwait(false);
        return token != null;
    }

    private string GetTokenFilePath(string provider)
    {
        var safeProvider = string.Join("_", provider.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storagePath, $"{safeProvider}.token");
    }

    private void EnsureStorageDirectory()
        => DirectoryHelper.EnsureDirectoryExists(_fs, _storagePath);

    private static string GetDefaultStoragePath()
        => WorkflowConstants.Paths.TokensDirectory;
}
