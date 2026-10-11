namespace McpClient;

/// <summary>
/// MCP OAuth 认证服务 — 编排 PKCE 授权完整流程：生成授权 URL → 监听回调 → 交换授权码
/// </summary>
[Register(typeof(McpOAuthService), ServiceLifetime.Singleton)]
public sealed partial class McpOAuthService : ServiceEntity {
    private readonly McpOAuthOptions _options;
    private readonly McpPkceAuthProvider _authProvider;
    private readonly ILogger<McpOAuthService>? _logger;
    private readonly AsyncLock _stateLock = new();
    private HttpListener? _callbackListener;

    /// <summary>
    /// 创建 McpOAuthService 实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="httpClientProvider">HTTP 客户端提供者</param>
    /// <param name="options">OAuth 选项（为 null 时用空默认值）</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpOAuthService(
        IFileSystem fs,
        IHttpClientProvider httpClientProvider,
        McpOAuthOptions? options = null,
        ILogger<McpOAuthService>? logger = null) {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(httpClientProvider);
        _options = options ?? new McpOAuthOptions {
            ClientId = string.Empty,
            AuthorizationUrl = string.Empty,
            TokenUrl = string.Empty
        };
        _logger = logger;
        _authProvider = new McpPkceAuthProvider(_options, fs, httpClientProvider.GetClient(), logger: null);
    }

    /// <summary>
    /// 底层认证提供者 — 暴露给外部直接调用令牌刷新等操作
    /// </summary>
    public IMcpAuthProvider AuthProvider => _authProvider;

    /// <summary>
    /// 启动 PKCE 授权流程 — 生成授权 URL 并监听回调接收授权码，超时由 AuthorizationTimeout 控制
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>授权成功返回 true；超时、回调错误或授权码交换失败返回 false</returns>
    public async Task<bool> StartAuthorizationFlowAsync(CancellationToken cancellationToken = default) {
        var authUrl = await _authProvider.GetAuthorizationUrlAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("PKCE 授权流程启动");

        try {
            _callbackListener = new HttpListener();
            _callbackListener.Prefixes.Add($"{_options.RedirectUrl.TrimEnd('/')}/");
            _callbackListener.Start();

            _logger?.LogInformation("请在浏览器中打开以下 URL 完成授权: {AuthUrl}", authUrl);

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, _options.AuthorizationTimeout);

            var context = await _callbackListener.GetContextAsync().ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            if (!string.IsNullOrEmpty(error)) {
                var errorDesc = context.Request.QueryString["error_description"] ?? error;
                _logger?.LogError("授权回调错误: {Error}", errorDesc);

                await SendCallbackResponseAsync(context, false, errorDesc).ConfigureAwait(false);
                return false;
            }

            if (string.IsNullOrEmpty(code)) {
                _logger?.LogError("授权回调缺少授权码");
                await SendCallbackResponseAsync(context, false, "Missing authorization code").ConfigureAwait(false);
                return false;
            }

            var success = await _authProvider.ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
            await SendCallbackResponseAsync(context, success, null).ConfigureAwait(false);

            return success;
        } catch (OperationCanceledException) {
            _logger?.LogWarning("PKCE 授权流程超时");
            return false;
        } catch (HttpListenerException ex) {
            _logger?.LogError(ex, "HTTP 监听器异常");
            return false;
        } finally {
            StopCallbackListener();
        }
    }

    /// <summary>
    /// 异步刷新令牌 — 委托给底层认证提供者
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>刷新成功返回 true；失败返回 false</returns>
    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default) {
        return await _authProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步获取访问令牌 — 委托给底层认证提供者
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>访问令牌；获取失败返回 null</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) {
        return await _authProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 是否已认证 — 委托给底层认证提供者
    /// </summary>
    public bool IsAuthenticated => _authProvider.IsAuthenticated;

    private static async Task SendCallbackResponseAsync(HttpListenerContext context, bool success, string? error) {
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

    private void StopCallbackListener() {
        try {
            _callbackListener?.Stop();
            _callbackListener?.Close();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "停止 OAuth 回调监听器失败");
        }
        _callbackListener = null;
    }

    /// <summary>异步释放资源 — 停止回调监听器、异步释放认证提供者与基类,同步释放状态锁。</summary>
    public override async ValueTask DisposeAsync() {
        StopCallbackListener();
        await _authProvider.DisposeAsync().ConfigureAwait(false);
        _stateLock.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}