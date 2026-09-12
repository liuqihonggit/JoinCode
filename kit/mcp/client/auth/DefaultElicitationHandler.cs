namespace McpClient;

public sealed class DefaultElicitationHandler : IElicitationHandler
{
    public Task<ElicitResult> HandleElicitationAsync(string serverName, JsonRpcId requestId, ElicitRequestParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ElicitResult { Action = ElicitActionConstants.Cancel });
    }
}
