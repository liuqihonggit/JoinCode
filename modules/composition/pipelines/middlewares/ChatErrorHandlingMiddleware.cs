namespace JoinCode.Pipelines.Middlewares;

/// <summary>
/// 错误处理中间件 — 捕获管道异常，分类转换为友好消息后重新抛出
/// 不将异常转换为流事件（避免错误被当作 AI 回复持久化），
/// 仅提供结构化日志记录 + 友好异常转换，异常由上层 ChatService 处理
/// </summary>
internal sealed partial class ChatErrorHandlingMiddleware : ServiceEntity, Core.Context.IChatMiddleware {
    private readonly ILogger<ChatErrorHandlingMiddleware> _logger;

    /// <summary>构造 ChatErrorHandlingMiddleware。</summary>
    /// <param name="logger">日志记录器</param>
    public ChatErrorHandlingMiddleware(ILogger<ChatErrorHandlingMiddleware> logger) {
        _logger = logger;
    }

    /// <summary>执行中间件。</summary>
    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
        Exception? error = null;
        await using var enumerator = next(context, ct).GetAsyncEnumerator(ct);

        while (true) {
            try {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) {
                    break;
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                error = ex;
                break;
            }

            yield return enumerator.Current;
        }

        if (error is not null) {
            if (error is ContextOverflowException overflowEx) {
                _logger?.LogError(overflowEx, "[ChatErrorHandling] 上下文溢出（兜底拦截）: Turn={Turn}", context.ConversationTurn);
                Diag.WriteError($"[ChatErrorHandling] Turn={context.ConversationTurn}, 上下文溢出兜底拦截", overflowEx);
                yield return ChatStreamEvent.Text(
                    $"⚠ 上下文溢出：{overflowEx.Message} " +
                    "请用 /compact 压缩对话历史、运行 /clear 清空上下文，" +
                    "或切换到更大上下文窗口的模型。");
                yield break;
            }
            var classified = ClassifyException(error);
            var errorCode = classified is WorkflowException wfEx ? wfEx.ErrorCode : ErrorCode.WorkflowExecution.ToValue();
            _logger.LogError(error, "[ChatErrorHandling] 管道异常: Turn={Turn}, DryRun={DryRun}, Code={Code}",
                context.ConversationTurn, context.IsDryRun, errorCode);
            Diag.WriteError($"[ChatErrorHandling] Turn={context.ConversationTurn}, DryRun={context.IsDryRun}, Code={errorCode}", error);
            throw classified;
        }
    }

    /// <summary>
    /// 将原始异常分类转换：
    /// 1. 已带错误码的 WorkflowException 子类（ApiException/ConfigurationException 等）原样保留类型
    /// 2. 纯 HTTP/超时类异常转换为带友好消息的 ApiException
    /// 3. 其余异常转换为 WorkflowExecution 通用 ApiException
    /// </summary>
    internal static Exception ClassifyException(Exception ex) {
        if (ex is WorkflowException workflowEx)
            return workflowEx;

        if (ex is System.Net.Http.HttpRequestException httpEx) {
            // TASK069-E1 — SSL/TLS 证书失败先判（对齐基准 formatAPIError 的 isSSLError 分支）：
            // 这类失败 StatusCode 恒 null，落进通用分支只会变成「网络请求失败: 裸异常消息」，
            // 而实际根因通常是企业代理 / TLS 拦截，需要可操作提示。
            if (IsSslFailure(httpEx)) {
                return ApiException.SslCertificate(GetEndpointHint(ex), DescribeSslFailure(httpEx));
            }

            var statusCode = (int?)httpEx.StatusCode;
            return statusCode switch {
                401 => ApiException.Authentication(GetEndpointHint(ex), "API Key 无效或已过期。请检查供应商专属环境变量（如 OPENAI_API_KEY、DEEPSEEK_API_KEY）配置。"),
                403 => new ApiException("API 访问被拒绝。请检查账户权限和 API Key 配置。", ex, statusCode: 403, endpoint: GetEndpointHint(ex), errorCode: ErrorCode.ApiAuthorization.ToValue()),
                429 => ApiException.RateLimit(GetEndpointHint(ex)),
                502 => ApiException.ResponseError(GetEndpointHint(ex), 502, "网关错误（Bad Gateway），上游服务异常，请稍后重试"),
                503 => ApiException.ResponseError(GetEndpointHint(ex), 503, "服务端暂时不可用（Service Unavailable），请稍后重试"),
                504 => ApiException.ResponseError(GetEndpointHint(ex), 504, "网关超时（Gateway Timeout），上游服务未及时响应，请稍后重试"),
                >= 500 => ApiException.ResponseError(GetEndpointHint(ex), statusCode ?? 500, ex.Message),
                null when ex.InnerException is System.Net.Sockets.SocketException => ApiException.Connection(GetEndpointHint(ex), ex),
                null when ex.InnerException is TimeoutException or System.Threading.Tasks.TaskCanceledException => ApiException.Timeout(GetEndpointHint(ex)),
                _ => new ApiException($"网络请求失败: {ex.Message}", ex, errorCode: ErrorCode.ApiConnection.ToValue())
            };
        }

        if (ex is TimeoutException)
            return ApiException.Timeout(GetEndpointHint(ex));

        if (ex is System.Threading.Tasks.TaskCanceledException)
            return ApiException.Timeout(GetEndpointHint(ex));

        return new ApiException($"对话管道异常: {ex.Message}", ex, errorCode: ErrorCode.WorkflowExecution.ToValue());
    }

    /// <summary>
    /// 是否 TLS/证书握手失败（TASK069-E1）：.NET 侧以 <see cref="System.Net.Http.HttpRequestError.SecureConnectionError"/>
    /// 标记，或内部异常为 <see cref="System.Security.Authentication.AuthenticationException"/>
    ///（证书链/名称校验失败的统一载体 — 对齐基准 SSL_ERROR_CODES 一组 Node 错误码的语义）。
    /// </summary>
    internal static bool IsSslFailure(System.Net.Http.HttpRequestException ex) =>
        ex.HttpRequestError == System.Net.Http.HttpRequestError.SecureConnectionError
        || ex.InnerException is System.Security.Authentication.AuthenticationException;

    /// <summary>
    /// 证书失败原因本地化（基准 formatAPIError 的自签/过期/吊销/主机名/尚未生效分支 —
    /// .NET 把这些都折成 AuthenticationException 的 OpenSSL 式文本，只能按关键字还原）。
    /// </summary>
    internal static string DescribeSslFailure(Exception ex) {
        var message = ex.InnerException?.Message ?? ex.Message;
        if (message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            return "SSL 证书已过期";
        if (message.Contains("revoked", StringComparison.OrdinalIgnoreCase))
            return "SSL 证书已被吊销";
        if (message.Contains("not yet valid", StringComparison.OrdinalIgnoreCase))
            return "SSL 证书尚未生效";
        if (message.Contains("name mismatch", StringComparison.OrdinalIgnoreCase)
            || message.Contains("RemoteCertificateNameMismatch", StringComparison.OrdinalIgnoreCase)
            || message.Contains("hostname", StringComparison.OrdinalIgnoreCase))
            return "SSL 证书主机名不匹配";
        if (message.Contains("self-signed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("self signed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SELF_SIGNED", StringComparison.OrdinalIgnoreCase))
            return "检测到自签证书，证书链不受信任";
        return "SSL 证书校验失败（证书链不受信任）";
    }

    internal static string GetEndpointHint(Exception ex) {
        var msg = ex.Message;
        if (msg.Contains("localhost") || msg.Contains("127.0.0.1"))
            return "本地服务";
        if (msg.Contains("api.openai.com"))
            return "OpenAI";
        if (msg.Contains("api.anthropic.com"))
            return "Anthropic";
        return "API 服务";
    }
}