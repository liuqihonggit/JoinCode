namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpAuthConfigProvider
{
    McpAuthConfig? GetAuthConfig(string authName);
}
