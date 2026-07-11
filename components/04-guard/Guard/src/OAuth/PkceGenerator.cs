
namespace Services.OAuth;

/// <summary>
/// PKCE 生成器接口
/// 生成 PKCE 参数用于 OAuth 2.0 授权码流程
/// </summary>
public interface IPkceGenerator
{
    /// <summary>
    /// 生成 PKCE 参数
    /// </summary>
    /// <returns>PKCE 参数</returns>
    PkceParameters Generate();

    /// <summary>
    /// 验证 code_verifier
    /// </summary>
    /// <param name="verifier">code_verifier</param>
    /// <param name="challenge">code_challenge</param>
    /// <param name="method">code_challenge_method</param>
    /// <returns>是否验证通过</returns>
    bool Verify(string verifier, string challenge, string method);
}

/// <summary>
/// PKCE 参数
/// </summary>
public sealed record PkceParameters
{
    /// <summary>
    /// code_verifier (43-128 字符)
    /// </summary>
    public required string CodeVerifier { get; init; }

    /// <summary>
    /// code_challenge
    /// </summary>
    public required string CodeChallenge { get; init; }

    /// <summary>
    /// code_challenge_method (S256 或 plain)
    /// </summary>
    public required string CodeChallengeMethod { get; init; }
}

/// <summary>
/// PKCE 生成器实现
/// 使用 S256 方法
/// </summary>
[Register]
public sealed class PkceGenerator : IPkceGenerator
{
    private const int MinVerifierLength = 43;
    private const int MaxVerifierLength = 128;
    private const int DefaultVerifierLength = 128;
    private const string CodeChallengeMethod = "S256";

    private static readonly char[] UrlSafeChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~".ToCharArray();

    /// <inheritdoc />
    public PkceParameters Generate()
    {
        var verifier = GenerateCodeVerifier();
        var challenge = GenerateCodeChallenge(verifier);

        return new PkceParameters
        {
            CodeVerifier = verifier,
            CodeChallenge = challenge,
            CodeChallengeMethod = CodeChallengeMethod
        };
    }

    /// <inheritdoc />
    public bool Verify(string verifier, string challenge, string method)
    {
        if (string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(challenge))
        {
            return false;
        }

        if (verifier.Length < MinVerifierLength || verifier.Length > MaxVerifierLength)
        {
            return false;
        }

        return method.ToUpperInvariant() switch
        {
            "S256" => GenerateCodeChallenge(verifier) == challenge,
            "PLAIN" => verifier == challenge,
            _ => false
        };
    }

    /// <summary>
    /// 生成 code_verifier
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[DefaultVerifierLength];
        RandomNumberGenerator.Fill(bytes);

        return string.Concat(bytes.Select(b => UrlSafeChars[b % UrlSafeChars.Length]));
    }

    /// <summary>
    /// 生成 code_challenge (SHA256 base64url)
    /// </summary>
    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes = Encoding.ASCII.GetBytes(verifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Base64Url 编码
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
