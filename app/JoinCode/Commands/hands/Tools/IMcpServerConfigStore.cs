namespace JoinCode.ChatCommands;

/// <summary>
/// MCP 服务器配置存储接口 - 管理MCP服务器配置的增删查
/// </summary>
public interface IMcpServerConfigStore
{
    /// <summary>
    /// 获取配置文件路径
    /// </summary>
    string GetConfigPath(string scope);

    /// <summary>
    /// 加载指定作用域的配置
    /// </summary>
    Task<McpConfigFile> LoadAsync(string scope, CancellationToken ct = default);

    /// <summary>
    /// 保存指定作用域的配置
    /// </summary>
    Task SaveAsync(string scope, McpConfigFile config, CancellationToken ct = default);

    /// <summary>
    /// 添加MCP服务器
    /// </summary>
    Task AddServerAsync(string name, McpServerConfigEntry entry, string scope, CancellationToken ct = default);

    /// <summary>
    /// 移除MCP服务器
    /// </summary>
    Task<bool> RemoveServerAsync(string name, string scope, CancellationToken ct = default);

    /// <summary>
    /// 获取所有作用域的服务器
    /// </summary>
    Task<Dictionary<string, (string Scope, McpServerConfigEntry Entry)>> GetAllServersAsync(CancellationToken ct = default);
}
