namespace JoinCode.ChatCommands;

/// <summary>
/// MCP 服务器配置存储实现 — 管理用户级和项目级 MCP 服务器配置的增删查
/// </summary>
[Register(typeof(IMcpServerConfigStore), ServiceLifetime.Singleton)]
public sealed partial class McpServerConfigStore : ServiceEntity, IMcpServerConfigStore {
    private readonly string _userConfigDir = AppDataConstants.Paths.JccDirectory;
    private readonly IFileSystem _fs;

    /// <summary>
    /// 构造 MCP 服务器配置存储实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    public McpServerConfigStore(IFileSystem fs) {
        _fs = fs;
    }

    private string UserConfigPath => Path.Combine(_userConfigDir, "mcp_servers.json");

    /// <summary>
    /// 获取指定作用域的配置文件路径
    /// </summary>
    /// <param name="scope">作用域（user 或 project）</param>
    /// <returns>配置文件完整路径</returns>
    public string GetConfigPath(string scope) {
        var scopeEnum = AgentMemoryScopeExtensions.FromValue(scope);
        return scopeEnum switch {
            AgentMemoryScope.User => UserConfigPath,
            _ => Path.Combine(_fs.GetCurrentDirectory(), ".mcp.json")
        };
    }

    /// <summary>
    /// 异步加载指定作用域的 MCP 配置
    /// </summary>
    /// <param name="scope">作用域（user 或 project）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>MCP 配置文件对象，文件不存在或解析失败时返回空配置</returns>
    public async Task<McpConfigFile> LoadAsync(string scope, CancellationToken ct = default) {
        var path = GetConfigPath(scope);
        if (!_fs.FileExists(path))
            return new McpConfigFile();

        try {
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var result = RelaxedJsonSerializer.Deserialize(json, McpConfigJsonContext.Default.McpConfigFile);
            return result ?? new McpConfigFile();
        } catch {
            return new McpConfigFile();
        }
    }

    /// <summary>
    /// 异步保存指定作用域的 MCP 配置（原子写入）
    /// </summary>
    /// <param name="scope">作用域（user 或 project）</param>
    /// <param name="config">MCP 配置文件对象</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步保存操作的任务</returns>
    public async Task SaveAsync(string scope, McpConfigFile config, CancellationToken ct = default) {
        var path = GetConfigPath(scope);
        var dir = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        var json = RelaxedJsonSerializer.Serialize(config, McpConfigJsonContext.Default);
        var tmpPath = path + ".tmp";
        await _fs.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
        _fs.MoveFile(tmpPath, path, overwrite: true);
    }

    /// <summary>
    /// 异步添加 MCP 服务器到指定作用域配置
    /// </summary>
    /// <param name="name">服务器名称（仅允许字母、数字、下划线和连字符）</param>
    /// <param name="entry">服务器配置条目</param>
    /// <param name="scope">作用域（user 或 project）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步添加操作的任务</returns>
    public async Task AddServerAsync(string name, McpServerConfigEntry entry, string scope, CancellationToken ct = default) {
        ValidateServerName(name);

        var path = GetConfigPath(scope);
        var dir = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        try {
            await _fs.EditFileAsync<bool>(path, async (bytes, cancellationToken) => {
                var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
                McpConfigFile config;
                try {
                    config = RelaxedJsonSerializer.Deserialize(content, McpConfigJsonContext.Default.McpConfigFile) ?? new McpConfigFile();
                } catch {
                    config = new McpConfigFile();
                }
                if (config.McpServers.ContainsKey(name)) {
                    throw new InvalidOperationException($"[MCP009] MCP 服务器 '{name}' 已存在于 {scope} 配置中");
                }
                config.McpServers[name] = entry;
                var json = RelaxedJsonSerializer.Serialize(config, McpConfigJsonContext.Default);
                var newBytes = FileEncodingDetector.EncodeString(json, encoding);
                return (newBytes, true);
            }, ct).ConfigureAwait(false);
        } catch (FileNotFoundException) {
            var config = new McpConfigFile();
            config.McpServers[name] = entry;
            await SaveAsync(scope, config, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步从指定作用域移除 MCP 服务器
    /// </summary>
    /// <param name="name">服务器名称</param>
    /// <param name="scope">作用域（user 或 project）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功移除；服务器不存在时返回 false</returns>
    public async Task<bool> RemoveServerAsync(string name, string scope, CancellationToken ct = default) {
        var path = GetConfigPath(scope);
        var dir = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        try {
            return await _fs.EditFileAsync<bool>(path, async (bytes, cancellationToken) => {
                var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
                McpConfigFile config;
                try {
                    config = RelaxedJsonSerializer.Deserialize(content, McpConfigJsonContext.Default.McpConfigFile) ?? new McpConfigFile();
                } catch {
                    config = new McpConfigFile();
                }
                if (!config.McpServers.Remove(name))
                    return (null, false);
                var json = RelaxedJsonSerializer.Serialize(config, McpConfigJsonContext.Default);
                var newBytes = FileEncodingDetector.EncodeString(json, encoding);
                return (newBytes, true);
            }, ct).ConfigureAwait(false);
        } catch (FileNotFoundException) {
            return false;
        }
    }

    /// <summary>
    /// 异步获取所有作用域（用户级 + 项目级）的 MCP 服务器，用户级优先
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>服务器名到（作用域, 配置条目）的字典</returns>
    public async Task<Dictionary<string, (string Scope, McpServerConfigEntry Entry)>> GetAllServersAsync(CancellationToken ct = default) {
        var result = new Dictionary<string, (string Scope, McpServerConfigEntry Entry)>(StringComparer.OrdinalIgnoreCase);

        var userConfig = await LoadAsync(AgentMemoryScope.User.ToValue(), ct).ConfigureAwait(false);
        foreach (var (name, entry) in userConfig.McpServers) {
            result[name] = (AgentMemoryScope.User.ToValue(), entry);
        }

        var projectConfig = await LoadAsync(AgentMemoryScope.Project.ToValue(), ct).ConfigureAwait(false);
        foreach (var (name, entry) in projectConfig.McpServers) {
            if (!result.ContainsKey(name)) {
                result[name] = (AgentMemoryScope.Project.ToValue(), entry);
            }
        }

        return result;
    }

    private static void ValidateServerName(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("[APP005] 服务器名称不能为空");

        foreach (var c in name) {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') {
                throw new ArgumentException($"[APP008] 服务器名称 '{name}' 包含非法字符 '{c}'，只允许字母、数字、下划线和连字符");
            }
        }
    }
}