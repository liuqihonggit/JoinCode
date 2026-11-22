
namespace JoinCode.ChatCommands;

/// <summary>
/// MCP 服务器配置存储接口 - 管理MCP服务器配置的增删查
/// </summary>
public interface IMcpServerConfigStore : IStore {
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
    Task<Dictionary<string, McpServerScopedEntry>> GetAllServersAsync(CancellationToken ct = default);
}

/// <summary>
/// 带作用域的 MCP 服务器配置条目 — 同一个服务器名可能同时存在于用户级与项目级配置。
/// </summary>
/// <param name="Scope">作用域（用户级 / 项目级）。</param>
/// <param name="Entry">MCP 服务器配置条目。</param>
public sealed record McpServerScopedEntry(string Scope, McpServerConfigEntry Entry);