
using JoinCode.Abstractions.Attributes;

namespace McpClient;

[Register]
public sealed partial class McpOAuthService
{
    private readonly McpOAuthOptions _options;
    private readonly McpPkceAuthProvider _authProvider;
    [Inject] private readonly ILogger<McpOAuthService>? _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private HttpListener? _callbackListener;

    public McpOAuthService(
        IFileSystem fs,
        IHttpClientProvider httpClientProvider,
        McpOAuthOptions? options = null,
        ILogger<McpOAuthService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(httpClientProvider);
        _options = options ?? new McpOAuthOptions
        {
            ClientId = string.Empty,
            AuthorizationUrl = string.Empty,
            TokenUrl = string.Empty
        };
        _logger = logger;
        _authProvider = new McpPkceAuthProvider(_options, fs, httpClientProvider.GetClient(), logger: null);
    }

    public IMcpAuthProvider AuthProvider => _authProvider;

    public async Task<bool> StartAuthorizationFlowAsync(CancellationToken cancellationToken = default)
    {
        var authUrl = await _authProvider.GetAuthorizationUrlAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("PKCE 授权流程启动");

        try
        {
            _callbackListener = new HttpListener();
            _callbackListener.Prefixes.Add($"{_options.RedirectUrl.TrimEnd('/')}/");
            _callbackListener.Start();

            _logger?.LogInformation("请在浏览器中打开以下 URL 完成授权: {AuthUrl}", authUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.AuthorizationTimeout);

            var context = await _callbackListener.GetContextAsync().ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            if (!string.IsNullOrEmpty(error))
            {
                var errorDesc = context.Request.QueryString["error_description"] ?? error;
                _logger?.LogError("授权回调错误: {Error}", errorDesc);

                await SendCallbackResponseAsync(context, false, errorDesc).ConfigureAwait(false);
                return false;
            }

            if (string.IsNullOrEmpty(code))
            {
                _logger?.LogError("授权回调缺少授权码");
                await SendCallbackResponseAsync(context, false, "Missing authorization code").ConfigureAwait(false);
                return false;
            }

            var success = await _authProvider.ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
            await SendCallbackResponseAsync(context, success, null).ConfigureAwait(false);

            return success;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("PKCE 授权流程超时");
            return false;
        }
        catch (HttpListenerException ex)
        {
            _logger?.LogError(ex, "HTTP 监听器异常");
            return false;
        }
        finally
        {
            StopCallbackListener();
        }
    }

    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        return await _authProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return await _authProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool IsAuthenticated => _authProvider.IsAuthenticated;

    private static async Task SendCallbackResponseAsync(HttpListenerContext context, bool success, string? error)
    {
        var response = context.Response;
        var html = success
            ? "<html><body><h1>Authorization successful</h1><p>You can close this window.</p></body></html>"
            : $"<html><body><h1>Authorization failed</h1><p>{(error ?? "Unknown error").Replace("<", "&lt;").Replace(">", "&gt;").Replace("&", "&amp;")}</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;

        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    private void StopCallbackListener()
    {
        try
        {
            _callbackListener?.Stop();
            _callbackListener?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"McpOAuthService: Failed to stop callback listener: {ex.Message}");
        }
        _callbackListener = null;
    }

    public void Dispose()
    {
        StopCallbackListener();
        _authProvider.Dispose();
        _stateLock.Dispose();
    }
}
