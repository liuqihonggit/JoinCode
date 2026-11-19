namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpAuthConfigProvider {
    /// <summary>尝试获取认证配置。</summary>
    /// <param name="authName">认证名称</param>
    /// <param name="config">认证配置</param>
    /// <returns>认证配置存在且类型受支持返回 true</returns>
    bool TryGetAuthConfig(string authName, [NotNullWhen(true)] out McpAuthConfig? config);
}