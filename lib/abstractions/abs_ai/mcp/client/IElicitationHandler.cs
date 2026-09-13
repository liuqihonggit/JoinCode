namespace JoinCode.Abstractions.Mcp.Client;

public interface IElicitationHandler
{
    Task<ElicitResult> HandleElicitationAsync(string serverName, JsonRpcId requestId, ElicitRequestParams @params, CancellationToken cancellationToken);
}
