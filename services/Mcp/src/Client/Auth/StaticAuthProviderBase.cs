
namespace McpClient;

public abstract class StaticAuthProviderBase : IMcpAuthProvider
{
    public abstract McpAuthType AuthType { get; }
    public abstract bool IsAuthenticated { get; }
    public string? StepUpPendingScope => null;
    public bool NeedsStepUp => false;

    public abstract Task<Dictionary<string, string>> GetAuthHeadersAsync(CancellationToken cancellationToken = default);
    public abstract Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    public Task<bool> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public void MarkStepUpPending(string scope) { }
    public void ClearStepUpPending() { }
}
