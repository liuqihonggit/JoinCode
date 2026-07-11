
namespace Core.Agents;

/// <summary>
/// Agent 记忆服务实现 — 对齐 TS agentMemory.ts + agentMemorySnapshot.ts
/// 管理三种作用域记忆（user/project/local）和快照机制
/// </summary>
[Register]
public sealed partial class AgentMemoryService : IAgentMemoryService
{
    private const string AgentMemorySubdir = "agent-memory";
    private const string AgentMemoryLocalSubdir = "agent-memory-local";
    private const string SnapshotBaseDir = "agent-memory-snapshots";
    private const string MemoryEntrypointFile = "MEMORY.md";
    private const string SnapshotJsonFile = "snapshot.json";
    private const string SyncedJsonFile = ".snapshot-synced.json";

    /// <summary>
    /// MEMORY.md 最大行数 — 对齐 TS MAX_ENTRYPOINT_LINES
    /// </summary>
    private const int MaxEntrypointLines = 200;

    /// <summary>
    /// MEMORY.md 最大字节数 — 对齐 TS MAX_ENTRYPOINT_BYTES
    /// </summary>
    private const int MaxEntrypointBytes = 25_000;

    [Inject] private readonly ILogger<AgentMemoryService> _logger;
    private readonly IFileSystem _fs;
    private readonly string _memoryBase;   // ~/.jcc
    private readonly string _cwd;          // 当前工作目录

    public AgentMemoryService(ILogger<AgentMemoryService> logger, IFileSystem fs, string? memoryBase = null, string? cwd = null)
    {
        _logger = logger;
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _memoryBase = memoryBase ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder);
        _cwd = cwd ?? fs.GetCurrentDirectory();
    }

    /// <inheritdoc />
    public string GetAgentMemoryDir(string agentType, AgentMemoryScope scope)
    {
        var dirName = SanitizeAgentTypeForPath(agentType);
        return scope switch
        {
            AgentMemoryScope.User => Path.Combine(_memoryBase, AgentMemorySubdir, dirName) + Path.DirectorySeparatorChar,
            AgentMemoryScope.Project => Path.Combine(_cwd, ".claude", AgentMemorySubdir, dirName) + Path.DirectorySeparatorChar,
            AgentMemoryScope.Local => GetLocalAgentMemoryDir(dirName),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    /// <inheritdoc />
    public string GetAgentMemoryEntrypoint(string agentType, AgentMemoryScope scope)
    {
        return Path.Combine(GetAgentMemoryDir(agentType, scope), MemoryEntrypointFile);
    }

    /// <inheritdoc />
    public bool IsAgentMemoryPath(string absolutePath)
    {
        var normalized = Path.GetFullPath(absolutePath);

        // user scope
        var userBase = Path.GetFullPath(Path.Combine(_memoryBase, AgentMemorySubdir)) + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(userBase, StringComparison.OrdinalIgnoreCase))
            return true;

        // project scope
        var projectBase = Path.GetFullPath(Path.Combine(_cwd, ".claude", AgentMemorySubdir)) + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(projectBase, StringComparison.OrdinalIgnoreCase))
            return true;

        // local scope
        var localBase = Path.GetFullPath(Path.Combine(_cwd, ".claude", AgentMemoryLocalSubdir)) + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(localBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <inheritdoc />
    public string GetMemoryScopeDisplay(AgentMemoryScope? scope)
    {
        return scope switch
        {
            AgentMemoryScope.User => $"User ({_memoryBase}{Path.DirectorySeparatorChar}{AgentMemorySubdir}{Path.DirectorySeparatorChar})",
            AgentMemoryScope.Project => $"Project (.claude{Path.DirectorySeparatorChar}{AgentMemorySubdir}{Path.DirectorySeparatorChar})",
            AgentMemoryScope.Local => $"Local ({_cwd}{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}{AgentMemoryLocalSubdir}{Path.DirectorySeparatorChar})",
            null => "None",
            _ => "Unknown"
        };
    }

    /// <inheritdoc />
    public async Task<string> LoadAgentMemoryPromptAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default)
    {
        var scopeNote = GetScopeNote(scope);
        var memoryDir = GetAgentMemoryDir(agentType, scope);

        // fire-and-forget 确保目录存在 — 对齐 TS ensureMemoryDirExists
        _ = Task.Run(() => EnsureDirectoryExists(memoryDir), ct);

        var entrypointPath = Path.Combine(memoryDir, MemoryEntrypointFile);
        var entrypointContent = ReadEntrypointContent(entrypointPath);

        return BuildMemoryPrompt(agentType, memoryDir, scopeNote, entrypointContent);
    }

    /// <inheritdoc />
    public Task EnsureMemoryDirExistsAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default)
    {
        var dir = GetAgentMemoryDir(agentType, scope);
        EnsureDirectoryExists(dir);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AgentMemorySnapshotCheck> CheckSnapshotAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default)
    {
        var snapshotDir = GetSnapshotDirForAgent(agentType);
        var snapshotJsonPath = Path.Combine(snapshotDir, SnapshotJsonFile);

        // 读取快照元数据
        var snapshotMeta = await ReadJsonFileAsync<SnapshotMeta>(snapshotJsonPath, ct).ConfigureAwait(false);
        if (snapshotMeta is null)
            return new AgentMemorySnapshotCheck { Action = AgentMemorySnapshotAction.None };

        // 检查本地是否有 .md 文件
        var memoryDir = GetAgentMemoryDir(agentType, scope);
        var hasLocalMemory = HasMarkdownFiles(memoryDir);

        if (!hasLocalMemory)
            return new AgentMemorySnapshotCheck
            {
                Action = AgentMemorySnapshotAction.Initialize,
                SnapshotTimestamp = snapshotMeta.UpdatedAt
            };

        // 读取同步标记
        var syncedJsonPath = Path.Combine(memoryDir, SyncedJsonFile);
        var syncedMeta = await ReadJsonFileAsync<SyncedMeta>(syncedJsonPath, ct).ConfigureAwait(false);

        if (syncedMeta is null || string.Compare(snapshotMeta.UpdatedAt, syncedMeta.SyncedFrom, StringComparison.Ordinal) > 0)
            return new AgentMemorySnapshotCheck
            {
                Action = AgentMemorySnapshotAction.PromptUpdate,
                SnapshotTimestamp = snapshotMeta.UpdatedAt
            };

        return new AgentMemorySnapshotCheck { Action = AgentMemorySnapshotAction.None };
    }

    /// <inheritdoc />
    public async Task InitializeFromSnapshotAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default)
    {
        await CopySnapshotToLocalAsync(agentType, scope, ct).ConfigureAwait(false);
        await SaveSyncedMetaAsync(agentType, scope, snapshotTimestamp, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceFromSnapshotAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default)
    {
        // 先删除本地 .md 文件
        var memoryDir = GetAgentMemoryDir(agentType, scope);
        DeleteMarkdownFiles(memoryDir);

        await CopySnapshotToLocalAsync(agentType, scope, ct).ConfigureAwait(false);
        await SaveSyncedMetaAsync(agentType, scope, snapshotTimestamp, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task MarkSnapshotSyncedAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default)
    {
        return SaveSyncedMetaAsync(agentType, scope, snapshotTimestamp, ct);
    }

    #region 内部方法

    /// <summary>
    /// 将 Agent 类型名中的冒号替换为短横线 — 对齐 TS sanitizeAgentTypeForPath
    /// Windows 不允许目录名含冒号
    /// </summary>
    private static string SanitizeAgentTypeForPath(string agentType)
        => agentType.Replace(':', '-');

    /// <summary>
    /// 获取 local 作用域的记忆目录 — 对齐 TS getLocalAgentMemoryDir
    /// 支持 JCC_REMOTE_MEMORY_DIR 环境变量覆盖
    /// </summary>
    private string GetLocalAgentMemoryDir(string dirName)
    {
        var remoteDir = Environment.GetEnvironmentVariable(JccEnvVar.RemoteMemoryDir.ToValue());
        if (!string.IsNullOrEmpty(remoteDir))
        {
            var gitRoot = FindGitRoot(_cwd);
            var sanitizedGitRoot = SanitizePathSegment(gitRoot);
            return Path.Combine(remoteDir, "projects", sanitizedGitRoot, AgentMemoryLocalSubdir, dirName) + Path.DirectorySeparatorChar;
        }

        return Path.Combine(_cwd, ".claude", AgentMemoryLocalSubdir, dirName) + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 获取快照目录 — 对齐 TS getSnapshotDirForAgent
    /// 快照目录始终在项目 cwd 下
    /// </summary>
    private string GetSnapshotDirForAgent(string agentType)
    {
        var dirName = SanitizeAgentTypeForPath(agentType);
        return Path.Combine(_cwd, ".claude", SnapshotBaseDir, dirName);
    }

    /// <summary>
    /// 获取作用域的提示说明 — 对齐 TS loadAgentMemoryPrompt 中的 scopeNote
    /// </summary>
    private string GetScopeNote(AgentMemoryScope scope) => scope switch
    {
        AgentMemoryScope.User => "你的记忆是用户级的 — 它们应在所有项目中保持通用性。",
        AgentMemoryScope.Project => "你的记忆是项目级的 — 它们通过版本控制与团队共享，应针对本项目。",
        AgentMemoryScope.Local => "你的记忆是本地级的 — 它们不入版本控制，针对本项目和本机。",
        _ => string.Empty
    };

    /// <summary>
    /// 构建记忆提示词 — 对齐 TS buildMemoryPrompt
    /// </summary>
    private string BuildMemoryPrompt(string agentType, string memoryDir, string scopeNote, string? entrypointContent)
    {
        var sb = new StringBuilder();

        sb.AppendLine(scopeNote);
        sb.AppendLine();
        sb.AppendLine($"# {agentType} Agent 记忆");
        sb.AppendLine();
        sb.AppendLine($"记忆目录: {memoryDir}");

        if (_fs.DirectoryExists(memoryDir.TrimEnd(Path.DirectorySeparatorChar)))
            sb.AppendLine("目录已存在。");
        else
            sb.AppendLine("目录尚未创建。");

        sb.AppendLine();
        sb.AppendLine("## 记忆类型");
        sb.AppendLine("- **user**: 用户偏好和通用知识");
        sb.AppendLine("- **feedback**: 用户反馈和纠正");
        sb.AppendLine("- **project**: 项目特定的知识和约定");
        sb.AppendLine("- **reference**: 参考文档和代码片段");
        sb.AppendLine();
        sb.AppendLine("## 如何保存记忆");
        sb.AppendLine("1. 写独立文件: <topic>.md（带 frontmatter）");
        sb.AppendLine("2. 在 MEMORY.md 添加指针: `- [Title](file.md) — one-line hook`");
        sb.AppendLine();
        sb.AppendLine("## 信任回忆原则");
        sb.AppendLine("当访问记忆时，信任已保存的内容，不要重新验证。");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(entrypointContent))
        {
            var truncated = TruncateEntrypointContent(entrypointContent);
            sb.AppendLine("## 当前记忆索引");
            sb.AppendLine(truncated);
        }
        else
        {
            sb.AppendLine("## 当前记忆");
            sb.AppendLine("（记忆为空 — 尚未保存任何记忆）");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 读取 MEMORY.md 入口文件内容
    /// </summary>
    private string? ReadEntrypointContent(string entrypointPath)
    {
        try
        {
            if (!_fs.FileExists(entrypointPath))
                return null;

            return _fs.ReadAllText(entrypointPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取记忆入口文件失败: {Path}", entrypointPath);
            return null;
        }
    }

    /// <summary>
    /// 截断入口文件内容 — 对齐 TS truncateEntrypointContent
    /// 先按行截断，再按字节截断
    /// </summary>
    private static string TruncateEntrypointContent(string content)
    {
        // 按行截断
        var lines = content.Split('\n');
        if (lines.Length > MaxEntrypointLines)
        {
            var truncated = string.Join('\n', lines[..MaxEntrypointLines]);
            truncated += $"\n\n> ⚠️ MEMORY.md 超过 {MaxEntrypointLines} 行，已截断。请精简内容。";
            content = truncated;
        }

        // 按字节截断
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        if (bytes.Length > MaxEntrypointBytes)
        {
            // 在最后一个换行符处切割
            var cutIndex = MaxEntrypointBytes;
            while (cutIndex > 0 && bytes[cutIndex - 1] != (byte)'\n')
                cutIndex--;

            if (cutIndex == 0) cutIndex = MaxEntrypointBytes;

            content = System.Text.Encoding.UTF8.GetString(bytes[..cutIndex]);
            content += $"\n\n> ⚠️ MEMORY.md 超过 {MaxEntrypointBytes} 字节，已截断。请精简内容。";
        }

        return content;
    }

    /// <summary>
    /// 确保目录存在
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        try
        {
            var dir = path.TrimEnd(Path.DirectorySeparatorChar);
            if (!_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "创建记忆目录失败: {Path}", path);
        }
    }

    /// <summary>
    /// 检查目录中是否有 .md 文件
    /// </summary>
    private bool HasMarkdownFiles(string directory)
    {
        try
        {
            var dir = directory.TrimEnd(Path.DirectorySeparatorChar);
            if (!_fs.DirectoryExists(dir))
                return false;

            return _fs.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 删除目录中所有 .md 文件
    /// </summary>
    private void DeleteMarkdownFiles(string directory)
    {
        try
        {
            var dir = directory.TrimEnd(Path.DirectorySeparatorChar);
            if (!_fs.DirectoryExists(dir))
                return;

            foreach (var file in _fs.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                _fs.DeleteFile(file);
            }
        }
        catch (Exception ex)
        {
            // 删除失败不抛异常 — 对齐 TS 的容错设计
            System.Diagnostics.Trace.WriteLine($"Failed to delete markdown files in directory '{directory}': {ex.Message}");
        }
    }

    /// <summary>
    /// 将快照文件复制到本地记忆目录 — 对齐 TS copySnapshotToLocal
    /// </summary>
    private async Task CopySnapshotToLocalAsync(string agentType, AgentMemoryScope scope, CancellationToken ct)
    {
        try
        {
            var snapshotDir = GetSnapshotDirForAgent(agentType);
            if (!_fs.DirectoryExists(snapshotDir))
                return;

            var memoryDir = GetAgentMemoryDir(agentType, scope).TrimEnd(Path.DirectorySeparatorChar);
            EnsureDirectoryExists(memoryDir + Path.DirectorySeparatorChar);

            foreach (var file in _fs.GetFiles(snapshotDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (fileName == SnapshotJsonFile)
                    continue;

                var destPath = Path.Combine(memoryDir, fileName);
                await using var src = _fs.OpenRead(file);
                await using var dst = _fs.Open(destPath, FileMode.Create);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "复制快照到本地失败: {AgentType}", agentType);
        }
    }

    /// <summary>
    /// 保存同步标记 — 对齐 TS saveSyncedMeta
    /// </summary>
    private async Task SaveSyncedMetaAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct)
    {
        try
        {
            var memoryDir = GetAgentMemoryDir(agentType, scope).TrimEnd(Path.DirectorySeparatorChar);
            EnsureDirectoryExists(memoryDir + Path.DirectorySeparatorChar);

            var syncedPath = Path.Combine(memoryDir, SyncedJsonFile);
            var json = $"{{\"syncedFrom\":\"{snapshotTimestamp}\"}}";
            await _fs.WriteAllTextAsync(syncedPath, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "保存同步标记失败: {AgentType}", agentType);
        }
    }

    /// <summary>
    /// 读取 JSON 文件并校验 — 对齐 TS readJsonFile
    /// </summary>
    private async Task<T?> ReadJsonFileAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            if (!_fs.FileExists(path))
                return null;

            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            // 简单 JSON 解析 — AOT 兼容，不用 JsonSerializer.Deserialize
            if (typeof(T) == typeof(SnapshotMeta))
            {
                var updatedAt = ExtractJsonValue(json, "updatedAt");
                if (updatedAt is null) return null;
                return new SnapshotMeta { UpdatedAt = updatedAt } as T;
            }

            if (typeof(T) == typeof(SyncedMeta))
            {
                var syncedFrom = ExtractJsonValue(json, "syncedFrom");
                if (syncedFrom is null) return null;
                return new SyncedMeta { SyncedFrom = syncedFrom } as T;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取 JSON 文件失败: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 从 JSON 字符串中提取指定键的值 — AOT 兼容的简单解析
    /// </summary>
    private static string? ExtractJsonValue(string json, string key)
    {
        var pattern = $"\"{key}\"\\s*:\\s*\"([^\"]+)\"";
        var match = Regex.Match(json, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 查找 Git 根目录
    /// </summary>
    private string FindGitRoot(string startPath)
    {
        var dir = startPath;
        while (dir != null)
        {
            if (_fs.DirectoryExists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null)
                break;
            dir = parent;
        }
        return startPath;
    }

    /// <summary>
    /// 路径段安全化 — 替换路径分隔符
    /// </summary>
    private static string SanitizePathSegment(string path)
        => path.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');

    #endregion

    #region 内部数据类

    /// <summary>
    /// 快照元数据 — 对齐 TS snapshotMetaSchema
    /// </summary>
    private sealed class SnapshotMeta
    {
        public required string UpdatedAt { get; init; }
    }

    /// <summary>
    /// 同步标记元数据 — 对齐 TS syncedMetaSchema
    /// </summary>
    private sealed class SyncedMeta
    {
        public required string SyncedFrom { get; init; }
    }

    #endregion
}
