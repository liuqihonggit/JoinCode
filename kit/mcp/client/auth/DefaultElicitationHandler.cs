namespace McpClient;

/// <summary>
/// 默认 Elicitation 处理器 — 直接返回 Cancel,作为未注册真实处理器时的占位实现。
/// </summary>
public sealed class DefaultElicitationHandler : IElicitationHandler
{
    /// <summary>
    /// 处理 Elicitation 请求 — 始终返回 Cancel 动作,拒绝服务器的 elicitation 请求。
    /// </summary>
    /// <param name="serverName">发起请求的服务器名称。</param>
    /// <param name="requestId">JSON-RPC 请求标识。</param>
    /// <param name="params">Elicitation 请求参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>始终返回 Action 为 Cancel 的 ElicitResult。</returns>
    public Task<ElicitResult> HandleElicitationAsync(string serverName, JsonRpcId requestId, ElicitRequestParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ElicitResult { Action = ElicitActionEnumConstants.Cancel });
    }
}
