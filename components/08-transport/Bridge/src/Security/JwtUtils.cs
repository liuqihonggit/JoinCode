using JoinCode.Abstractions.Attributes;

namespace Core.Bridge;

/// <summary>
/// Bridge JWT 认证服务 - HMAC-SHA256 签名，NativeAOT 兼容
/// </summary>
[Register]
public sealed class BridgeJwtService
{
    private readonly byte[] _secretKey;
    private readonly ILogger? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, string> _revokedTokens = new(StringComparer.Ordinal);

    private const string Algorithm = "HS256";
    private const string TokenType = "JWT";
    private const string Issuer = "JccBridge";
    private const int DefaultExpirationSeconds = 3600;
    private const int RefreshWindowSeconds = 300;

    private static readonly string StaticHeaderJson = $"{{\"alg\":\"{Algorithm}\",\"typ\":\"{TokenType}\"}}";
    private static readonly byte[] StaticHeaderBytes = Encoding.UTF8.GetBytes(StaticHeaderJson);

    public BridgeJwtService(BridgeConfig? config = null, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        var secretKey = string.IsNullOrEmpty(config?.JwtSecretKey)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            : config.JwtSecretKey;
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        _secretKey = Encoding.UTF8.GetBytes(secretKey);
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="expirationSeconds">过期时间（秒），默认 3600</param>
    /// <returns>签名的 JWT Token 字符串</returns>
    public string GenerateToken(string clientId, int expirationSeconds = DefaultExpirationSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        if (expirationSeconds <= 0)
            expirationSeconds = DefaultExpirationSeconds;

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        var payload = new BridgeJwtPayload
        {
            Sub = clientId,
            Iat = now,
            Exp = now + expirationSeconds,
            Iss = Issuer
        };

        var payloadJson = JsonSerializer.Serialize(payload, BridgeJwtJsonContext.Default.BridgeJwtPayload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        var headerSegment = Base64UrlEncode(StaticHeaderBytes);
        var payloadSegment = Base64UrlEncode(payloadBytes);

        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signingInputBytes = Encoding.UTF8.GetBytes(signingInput);

        var signature = ComputeHmacSha256(signingInputBytes);
        var signatureSegment = Base64UrlEncode(signature);

        var token = $"{signingInput}.{signatureSegment}";

        _logger?.LogDebug("[BridgeJwt] Token 已生成，clientId: {ClientId}, 过期: {Exp}s", clientId, expirationSeconds);

        return token;
    }

    /// <summary>
    /// 验证 JWT Token 的签名和有效期
    /// </summary>
    /// <param name="token">JWT Token 字符串</param>
    /// <returns>验证结果</returns>
    public BridgeJwtValidationResult ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            _logger?.LogWarning("[BridgeJwt] Token 格式无效，段数: {Count}", parts.Length);
            return BridgeJwtValidationResult.Fail("Invalid token format: expected 3 segments");
        }

        // 撤销检查
        if (_revokedTokens.ContainsKey(token))
        {
            _logger?.LogWarning("[BridgeJwt] Token 已被撤销");
            return BridgeJwtValidationResult.Fail("Token has been revoked");
        }

        var headerSegment = parts[0];
        var payloadSegment = parts[1];
        var signatureSegment = parts[2];

        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signingInputBytes = Encoding.UTF8.GetBytes(signingInput);

        var expectedSignature = ComputeHmacSha256(signingInputBytes);
        var actualSignature = Base64UrlDecode(signatureSegment);

        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            _logger?.LogWarning("[BridgeJwt] Token 签名验证失败");
            return BridgeJwtValidationResult.Fail("Invalid signature");
        }

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadSegment));
        var payload = JsonSerializer.Deserialize(payloadJson, BridgeJwtJsonContext.Default.BridgeJwtPayload);

        if (payload is null)
        {
            _logger?.LogWarning("[BridgeJwt] Token payload 反序列化失败");
            return BridgeJwtValidationResult.Fail("Invalid payload");
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (payload.Exp <= now)
        {
            _logger?.LogDebug("[BridgeJwt] Token 已过期，exp: {Exp}, now: {Now}", payload.Exp, now);
            return BridgeJwtValidationResult.Fail("Token expired", payload);
        }

        if (payload.Iss != Issuer)
        {
            _logger?.LogWarning("[BridgeJwt] Token 签发者不匹配: {Issuer}", payload.Iss);
            return BridgeJwtValidationResult.Fail("Invalid issuer", payload);
        }

        return BridgeJwtValidationResult.Ok(payload);
    }

    /// <summary>
    /// 刷新 Token - 在有效期内且接近过期时签发新 Token
    /// </summary>
    /// <param name="token">当前 Token</param>
    /// <param name="expirationSeconds">新 Token 过期时间（秒），默认 3600</param>
    /// <returns>刷新结果</returns>
    public BridgeJwtRefreshResult RefreshToken(string token, int expirationSeconds = DefaultExpirationSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var validationResult = ValidateToken(token);
        if (!validationResult.IsValid)
        {
            return new BridgeJwtRefreshResult
            {
                Success = false,
                Error = validationResult.Error,
                NewToken = null
            };
        }

        var payload = validationResult.Payload!;
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var remainingSeconds = payload.Exp - now;

        if (remainingSeconds > RefreshWindowSeconds)
        {
            _logger?.LogDebug("[BridgeJwt] Token 未进入刷新窗口，剩余: {Remaining}s", remainingSeconds);
            return new BridgeJwtRefreshResult
            {
                Success = true,
                NewToken = token,
                Error = null
            };
        }

        var newToken = GenerateToken(payload.Sub, expirationSeconds);

        _logger?.LogDebug(
            "[BridgeJwt] Token 已刷新，clientId: {ClientId}, 原剩余: {Remaining}s",
            payload.Sub,
            remainingSeconds);

        return new BridgeJwtRefreshResult
        {
            Success = true,
            NewToken = newToken,
            Error = null
        };
    }

    /// <summary>
    /// 撤销 Token（使其立即失效）
    /// </summary>
    /// <param name="token">要撤销的 JWT Token</param>
    public void RevokeToken(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _revokedTokens[token] = token;
            _logger?.LogDebug("[BridgeJwt] Token 已撤销");
        }
    }

    /// <summary>
    /// 检查 Token 是否已被撤销
    /// </summary>
    public bool IsTokenRevoked(string token)
    {
        return !string.IsNullOrEmpty(token) && _revokedTokens.ContainsKey(token);
    }

    /// <summary>
    /// 清理已过期的撤销记录（定期调用以防止内存泄漏）
    /// </summary>
    public int CleanupExpiredRevocations()
    {
        var expiredTokens = new List<string>();
        foreach (var kvp in _revokedTokens)
        {
            var claims = GetClaims(kvp.Key);
            if (claims is not null)
            {
                var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
                if (claims.Exp <= now)
                {
                    expiredTokens.Add(kvp.Key);
                }
            }
        }

        foreach (var token in expiredTokens)
        {
            _revokedTokens.TryRemove(token, out _);
        }

        return expiredTokens.Count;
    }

    /// <summary>
    /// 检查 Token 是否已过期
    /// </summary>
    /// <param name="token">JWT Token 字符串</param>
    /// <returns>true 表示已过期</returns>
    public bool IsTokenExpired(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var claims = GetClaims(token);
        if (claims is null)
            return true;

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        return claims.Exp <= now;
    }

    /// <summary>
    /// 获取 Token 中的 Claims（不验证签名）
    /// </summary>
    /// <param name="token">JWT Token 字符串</param>
    /// <returns>Payload，解析失败返回 null</returns>
    public BridgeJwtPayload? GetClaims(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            return JsonSerializer.Deserialize(payloadJson, BridgeJwtJsonContext.Default.BridgeJwtPayload);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeJwt] 解析 Token claims 失败");
            return null;
        }
    }

    /// <summary>
    /// 计算 HMAC-SHA256 签名
    /// </summary>
    private byte[] ComputeHmacSha256(byte[] data)
    {
        using var hmac = new HMACSHA256(_secretKey);
        return hmac.ComputeHash(data);
    }

    /// <summary>
    /// Base64Url 编码（无填充）
    /// </summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Base64Url 解码
    /// </summary>
    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = padded.Length % 4;
        if (padding != 0)
            padded = padded.PadRight(padded.Length + (4 - padding), '=');

        return Convert.FromBase64String(padded);
    }
}

/// <summary>
/// JWT Payload 模型
/// </summary>
public sealed class BridgeJwtPayload
{
    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("iat")]
    public required long Iat { get; init; }

    [JsonPropertyName("exp")]
    public required long Exp { get; init; }

    [JsonPropertyName("iss")]
    public required string Iss { get; init; }
}

/// <summary>
/// Token 验证结果
/// </summary>
public sealed class BridgeJwtValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public BridgeJwtPayload? Payload { get; init; }

    internal static BridgeJwtValidationResult Ok(BridgeJwtPayload payload) => new()
    {
        IsValid = true,
        Payload = payload
    };

    internal static BridgeJwtValidationResult Fail(string error, BridgeJwtPayload? payload = null) => new()
    {
        IsValid = false,
        Error = error,
        Payload = payload
    };
}

/// <summary>
/// Token 刷新结果
/// </summary>
public sealed class BridgeJwtRefreshResult
{
    public bool Success { get; init; }
    public string? NewToken { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Bridge JWT AOT 兼容序列化上下文
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false)]
[JsonSerializable(typeof(BridgeJwtPayload))]
internal partial class BridgeJwtJsonContext : JsonSerializerContext;
