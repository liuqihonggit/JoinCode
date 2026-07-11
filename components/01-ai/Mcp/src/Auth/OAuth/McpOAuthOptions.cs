
namespace McpClient;

public sealed class McpOAuthOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string TokenUrl { get; init; } = string.Empty;
    public string RedirectUrl { get; init; } = "http://localhost:8765/callback";
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();
    public TimeSpan AuthorizationTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public string TokenStoragePath { get; init; } = string.Empty;

    public bool HasPreconfiguredClient => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(AuthorizationUrl) && !string.IsNullOrEmpty(TokenUrl);
}
