
namespace Services.OAuth;

/// <summary>
/// Token 存储接口
/// 安全地存储和检索 OAuth Token
/// </summary>
public interface ITokenStorage {
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
public sealed record OAuthToken {
    /// <summary>访问令牌</summary>
    public required string AccessToken { get; init; }
    /// <summary>刷新令牌，可为空</summary>
    public string? RefreshToken { get; init; }
    /// <summary>令牌类型，默认为 "Bearer"</summary>
    public string TokenType { get; init; } = "Bearer";
    /// <summary>过期时间</summary>
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>授权范围列表</summary>
    public IReadOnlyList<string> Scope { get; init; } = Array.Empty<string>();
    /// <summary>获取时间，默认为当前 UTC 时间</summary>
    public DateTimeOffset ObtainedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 获取或设置 Token 是否已过期
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>
    /// 获取 Token 剩余有效时间
    /// </summary>
    /// <returns>剩余时间,已过期返回 Zero</returns>
    public TimeSpan GetRemainingTime() {
        var remaining = ExpiresAt - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

/// <summary>
/// Token 存储实现
/// </summary>
[Register(typeof(ITokenStorage), ServiceLifetime.Singleton)]
public sealed partial class TokenStorage : ServiceEntity, ITokenStorage {
    private readonly string _storagePath;
    private readonly ILogger<TokenStorage>? _logger;
    private readonly IFileSystem _fs;

    /// <summary>
    /// 构造 Token 存储器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="storagePath">存储目录路径,默认使用应用数据目录下的 Tokens 目录</param>
    /// <param name="logger">日志记录器</param>
    public TokenStorage(IFileSystem fs, string? storagePath = null, ILogger<TokenStorage>? logger = null) {
        _fs = fs;
        _storagePath = storagePath ?? GetDefaultStoragePath();
        _logger = logger;

        EnsureStorageDirectory();
    }

    /// <inheritdoc />
    public async Task SaveTokenAsync(string provider, OAuthToken token, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentNullException.ThrowIfNull(token);

        var filePath = GetTokenFilePath(provider);
        var json = RelaxedJsonSerializer.Serialize(token, OAuthTokenJsonContext.Default);
        await _fs.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Token saved for provider: {Provider}", provider);
    }

    /// <inheritdoc />
    public async Task<OAuthToken?> LoadTokenAsync(string provider, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(provider);

        var filePath = GetTokenFilePath(provider);

        if (!_fs.FileExists(filePath))
            return null;

        try {
            return await _fs.ReadAndDeserializeAsync(filePath, OAuthTokenJsonContext.Default.OAuthToken, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to load token for provider: {Provider}", provider);
            return null;
        }
    }

    /// <inheritdoc />
    public Task DeleteTokenAsync(string provider, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(provider);

        var filePath = GetTokenFilePath(provider);

        if (_fs.FileExists(filePath)) {
            _fs.DeleteFile(filePath);
            _logger?.LogInformation("Token deleted for provider: {Provider}", provider);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStoredProvidersAsync(CancellationToken cancellationToken = default) {
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

    /// <inheritdoc />
    public async Task<bool> HasTokenAsync(string provider, CancellationToken cancellationToken = default) {
        var token = await LoadTokenAsync(provider, cancellationToken).ConfigureAwait(false);
        return token != null;
    }

    private string GetTokenFilePath(string provider) {
        var safeProvider = string.Join("_", provider.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storagePath, $"{safeProvider}.token");
    }

    private void EnsureStorageDirectory()
        => DirectoryHelper.EnsureDirectoryExists(_fs, _storagePath);

    private static string GetDefaultStoragePath()
        => AppDataConstants.Paths.TokensDirectory;
}