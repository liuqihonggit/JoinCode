namespace McpProtocol;

public interface IMcpServer
{
    void RegisterTool<T>(T toolInstance) where T : class;
    void RegisterToolHandler(IMcpProtocolHandler handler);
    void RegisterResourceHandler(IResourceHandler handler);
    void RegisterPromptHandler(IPromptHandler handler);
    Task RunAsync(CancellationToken cancellationToken = default);
}
