namespace JoinCode.ChatCommands;

[Register]
public sealed class McpServerConfigStore : IMcpServerConfigStore
{
    private readonly string _userConfigDir = WorkflowConstants.Paths.JccDirectory;
    private readonly IFileSystem _fs;

    public McpServerConfigStore(IFileSystem fs)
    {
        _fs = fs;
    }

    private string UserConfigPath => Path.Combine(_userConfigDir, "mcp_servers.json");

    public string GetConfigPath(string scope)
    {
        var scopeEnum = AgentMemoryScopeExtensions.FromValue(scope);
        return scopeEnum switch
        {
            AgentMemoryScope.User => UserConfigPath,
            _ => Path.Combine(_fs.GetCurrentDirectory(), ".mcp.json")
        };
    }

    public async Task<McpConfigFile> LoadAsync(string scope, CancellationToken ct = default)
    {
        var path = GetConfigPath(scope);
        if (!_fs.FileExists(path))
            return new McpConfigFile();

        try
        {
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, McpConfigJsonContext.Default.McpConfigFile);
            return result ?? new McpConfigFile();
        }
        catch
        {
            return new McpConfigFile();
        }
    }

    public async Task SaveAsync(string scope, McpConfigFile config, CancellationToken ct = default)
    {
        var path = GetConfigPath(scope);
        var dir = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        var json = JsonSerializer.Serialize(config, McpConfigJsonContext.Default.McpConfigFile);
        var tmpPath = path + ".tmp";
        await _fs.WriteAllTextAsync(tmpPath, json, ct).ConfigureAwait(false);
        _fs.MoveFile(tmpPath, path, overwrite: true);
    }

    public async Task AddServerAsync(string name, McpServerConfigEntry entry, string scope, CancellationToken ct = default)
    {
        ValidateServerName(name);

        var config = await LoadAsync(scope, ct).ConfigureAwait(false);

        if (config.McpServers.ContainsKey(name))
        {
            throw new InvalidOperationException($"MCP 服务器 '{name}' 已存在于 {scope} 配置中");
        }

        config.McpServers[name] = entry;
        await SaveAsync(scope, config, ct).ConfigureAwait(false);
    }

    public async Task<bool> RemoveServerAsync(string name, string scope, CancellationToken ct = default)
    {
        var config = await LoadAsync(scope, ct).ConfigureAwait(false);

        if (!config.McpServers.Remove(name))
            return false;

        await SaveAsync(scope, config, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<Dictionary<string, (string Scope, McpServerConfigEntry Entry)>> GetAllServersAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, (string Scope, McpServerConfigEntry Entry)>(StringComparer.OrdinalIgnoreCase);

        var userConfig = await LoadAsync(AgentMemoryScope.User.ToValue(), ct).ConfigureAwait(false);
        foreach (var (name, entry) in userConfig.McpServers)
        {
            result[name] = (AgentMemoryScope.User.ToValue(), entry);
        }

        var projectConfig = await LoadAsync(AgentMemoryScope.Project.ToValue(), ct).ConfigureAwait(false);
        foreach (var (name, entry) in projectConfig.McpServers)
        {
            if (!result.ContainsKey(name))
            {
                result[name] = (AgentMemoryScope.Project.ToValue(), entry);
            }
        }

        return result;
    }

    private static void ValidateServerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("服务器名称不能为空");

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                throw new ArgumentException($"服务器名称 '{name}' 包含非法字符 '{c}'，只允许字母、数字、下划线和连字符");
            }
        }
    }
}
