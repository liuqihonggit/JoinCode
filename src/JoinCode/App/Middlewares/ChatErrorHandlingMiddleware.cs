using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Exceptions;

namespace JoinCode.App.Middlewares;

/// <summary>
/// 错误处理中间件 — 捕获管道异常，分类转换为友好消息后重新抛出
/// 不将异常转换为流事件（避免错误被当作 AI 回复持久化），
/// 仅提供结构化日志记录 + 友好异常转换，异常由上层 ChatService 处理
/// </summary>
[Register(typeof(Core.Context.IChatMiddleware))]
internal sealed partial class ChatErrorHandlingMiddleware : Core.Context.IChatMiddleware
{
    private readonly ILogger<ChatErrorHandlingMiddleware> _logger;

    public ChatErrorHandlingMiddleware(ILogger<ChatErrorHandlingMiddleware> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> InvokeAsync(
        Core.Context.ChatMiddlewareContext context,
        JoinCode.Abstractions.Pipeline.StreamMiddlewareDelegate<Core.Context.ChatMiddlewareContext, JoinCode.Abstractions.LLM.Chat.ChatStreamEvent> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Exception? error = null;
        var enumerator = next(context, ct).GetAsyncEnumerator(ct);

        try
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    error = ex;
                    break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (error is not null)
        {
            var friendly = ClassifyException(error);
            _logger.LogError(error, "[ChatErrorHandling] 管道异常: Turn={Turn}, DryRun={DryRun}, Code={Code}",
                context.ConversationTurn, context.IsDryRun, friendly.ErrorCode);
            throw friendly;
        }
    }

    /// <summary>
    /// 将原始异常分类转换为带友好消息的 ApiException
    /// </summary>
    private static ApiException ClassifyException(Exception ex)
    {
        if (ex is ApiException apiEx)
            return apiEx;

        if (ex is System.Net.Http.HttpRequestException httpEx)
        {
            var statusCode = (int?)httpEx.StatusCode;
            return statusCode switch
            {
                401 => ApiException.Authentication(GetEndpointHint(ex), "API Key 无效或已过期。请检查 JCC_API_KEY 配置。"),
                403 => new ApiException("API 访问被拒绝。请检查账户权限和 API Key 配置。", ex, statusCode: 403, errorCode: ErrorCode.ApiAuthorization.ToValue()),
                429 => ApiException.RateLimit(GetEndpointHint(ex)),
                >= 500 => ApiException.ResponseError(GetEndpointHint(ex), statusCode ?? 500, ex.Message),
                null when ex.InnerException is System.Net.Sockets.SocketException => ApiException.Connection(GetEndpointHint(ex), ex),
                null when ex.InnerException is System.Threading.Tasks.TaskCanceledException => ApiException.Timeout(GetEndpointHint(ex)),
                _ => new ApiException($"网络请求失败: {ex.Message}", ex, errorCode: ErrorCode.ApiConnection.ToValue())
            };
        }

        if (ex is TimeoutException)
            return ApiException.Timeout(GetEndpointHint(ex));

        if (ex is System.Threading.Tasks.TaskCanceledException tce)
        {
            if (tce.InnerException is TimeoutException)
                return ApiException.Timeout(GetEndpointHint(ex));
            return ApiException.Timeout(GetEndpointHint(ex));
        }

        return new ApiException($"对话管道异常: {ex.Message}", ex, errorCode: ErrorCode.WorkflowExecution.ToValue());
    }

    private static string GetEndpointHint(Exception ex)
    {
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
