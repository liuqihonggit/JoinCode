namespace JoinCode.Abstractions.Mcp.Client;

public interface IElicitationHandler {
    /// <summary>异步处理 MCP 引导请求。</summary>
    Task<ElicitResult> HandleElicitationAsync(string serverName, JsonRpcId requestId, ElicitRequestParams @params, CancellationToken cancellationToken);
}