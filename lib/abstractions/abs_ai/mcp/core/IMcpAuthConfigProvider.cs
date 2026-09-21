namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpAuthConfigProvider {
    /// <summary>获取认证配置。</summary>
    McpAuthConfig? GetAuthConfig(string authName);
}