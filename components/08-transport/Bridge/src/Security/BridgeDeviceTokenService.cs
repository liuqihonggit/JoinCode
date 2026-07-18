
namespace Core.Bridge;

/// <summary>
/// Bridge 设备令牌服务 — 对齐 TS 端 trustedDevice.ts
/// Bridge 会话具有 ELEVATED 安全等级，需要额外的设备令牌
/// best-effort 设计：任何失败只记日志不阻塞主流程
/// </summary>
public sealed class BridgeDeviceTokenService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private readonly string _authFilePath;
    private readonly IFileSystem _fs;
    private string? _cachedToken;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>受信设备令牌环境变量名</summary>
    private const string TrustedDeviceTokenEnvVar = "CLAUDE_TRUSTED_DEVICE_TOKEN";

    public BridgeDeviceTokenService(HttpClient httpClient, IFileSystem fs, ILogger? logger = null)
        : this(httpClient, fs, logger, authFilePath: null)
    {
    }

    /// <summary>
    /// 可注入 authFilePath 用于测试
    /// </summary>
    internal BridgeDeviceTokenService(HttpClient httpClient, IFileSystem fs, ILogger? logger, string? authFilePath)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _authFilePath = authFilePath
            ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppDataConstants.AppDataFolder, "auth.json");
    }

    /// <summary>
    /// 获取受信设备令牌 — 对齐 TS 端 getTrustedDeviceToken
    /// 优先级: 缓存 > 环境变量 > 安全存储
    /// </summary>
    public async Task<string?> GetTrustedDeviceTokenAsync(CancellationToken ct = default)
    {
        // 1. 返回缓存
        if (_cachedToken is not null) return _cachedToken;

        // 2. 环境变量
        var envToken = Environment.GetEnvironmentVariable(TrustedDeviceTokenEnvVar);
        if (!string.IsNullOrEmpty(envToken))
        {
            _cachedToken = envToken;
            return _cachedToken;
        }

        // 3. 安全存储（auth.json 中的 device_token 字段）
        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 双重检查
                if (_cachedToken is not null) return _cachedToken;

                var token = await ReadTokenFromStorageAsync(ct).ConfigureAwait(false);
                if (token is not null)
                {
                    _cachedToken = token;
                }

                return _cachedToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[BridgeDeviceToken] 读取设备令牌失败（best-effort）");
            return null;
        }
    }

    /// <summary>
    /// 清除令牌缓存 — 对齐 TS 端 clearTrustedDeviceTokenCache
    /// </summary>
    public void ClearCache()
    {
        _cachedToken = null;
    }

    /// <summary>
    /// 删除设备令牌 — 对齐 TS 端 clearTrustedDeviceToken
    /// 登录前调用，避免旧账号令牌残留
    /// </summary>
    public async Task ClearTokenAsync(CancellationToken ct = default)
    {
        ClearCache();

        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await DeleteTokenFromStorageAsync(ct).ConfigureAwait(false);
                _logger?.LogInformation("[BridgeDeviceToken] 已清除设备令牌");
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[BridgeDeviceToken] 清除设备令牌失败（best-effort）");
        }
    }

    /// <summary>
    /// 注册受信设备 — 对齐 TS 端 enrollTrustedDevice
    /// POST /api/auth/trusted_devices，获取 device_token 持久化
    /// 服务端限制注册必须在登录后10分钟内
    /// </summary>
    public async Task EnrollTrustedDeviceAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/trusted_devices");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                // 对齐 TS 端: { display_name: "Claude Code on ${hostname()} · ${process.platform}" }
                var displayName = $"JoinCode on {Environment.MachineName} · {Environment.OSVersion.Platform}";
                request.Content = new StringContent(
                    $"{{\"display_name\":\"{EscapeJsonString(displayName)}\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var je = JsonDocument.Parse(body).RootElement;

                if (je.TryGetProperty("device_token", out var tokenProp))
                {
                    var token = tokenProp.GetString();
                    if (token is not null)
                    {
                        _cachedToken = token;
                        await SaveTokenToStorageAsync(token, ct).ConfigureAwait(false);
                        _logger?.LogInformation("[BridgeDeviceToken] 设备注册成功");
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[BridgeDeviceToken] 设备注册失败（best-effort）");
        }
    }

    /// <summary>从安全存储读取令牌</summary>
    private async Task<string?> ReadTokenFromStorageAsync(CancellationToken ct)
    {
        if (!_fs.FileExists(_authFilePath)) return null;

        await using var fs = _fs.CreateStream(_authFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("device_token", out var tokenProp))
        {
            return tokenProp.GetString();
        }

        return null;
    }

    /// <summary>保存令牌到安全存储</summary>
    private async Task SaveTokenToStorageAsync(string token, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_authFilePath)!;
        _fs.CreateDirectory(dir);

        // 读取现有内容 — 存储 (key, rawJson) 对
        var data = new Dictionary<string, string>();
        if (_fs.FileExists(_authFilePath))
        {
            await using (var fs = _fs.CreateStream(_authFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    data[prop.Name] = prop.Value.GetRawText();
                }
            } // fs 在此处关闭
        }

        // 更新 device_token — 手写 JSON 值避免 AOT 不兼容
        data["device_token"] = $"\"{EscapeJsonString(token)}\"";

        // 写回
        var sb = new StringBuilder(256);
        sb.Append('{');
        var first = true;
        foreach (var (key, rawValue) in data)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(EscapeJsonString(key)).Append("\":").Append(rawValue);
            first = false;
        }

        sb.Append('}');

        await using var writeFs = _fs.CreateStream(_authFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await writeFs.WriteAsync(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
    }

    /// <summary>从安全存储删除令牌</summary>
    private async Task DeleteTokenFromStorageAsync(CancellationToken ct)
    {
        if (!_fs.FileExists(_authFilePath)) return;

        // 先读取并解析，然后关闭文件句柄，再写回
        Dictionary<string, string> data;
        await using (var fs = _fs.CreateStream(_authFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
            data = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("device_token")) continue;
                data[prop.Name] = prop.Value.GetRawText();
            }
        } // fs 在此处关闭

        // 写回（无 device_token）
        var sb = new StringBuilder(256);
        sb.Append('{');
        var first = true;
        foreach (var (key, rawValue) in data)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(EscapeJsonString(key)).Append("\":").Append(rawValue);
            first = false;
        }

        sb.Append('}');

        await using var writeFs = _fs.CreateStream(_authFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await writeFs.WriteAsync(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
    }

    /// <summary>JSON 字符串转义</summary>
    private static string EscapeJsonString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
