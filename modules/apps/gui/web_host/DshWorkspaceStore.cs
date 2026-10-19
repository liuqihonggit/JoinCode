namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 工作区注册表（ADR 0143）：前端用 <c>directoryPicker/pick</c> 选目录后调用 <c>workspace/create</c>，
/// 之后 <c>workspace/follow</c> 的基线读的就是这份数据。落在 <c>~/.jcc/dsh-workspaces.json</c>
/// （IFileSystem 抽象：生产 PhysicalFileSystem，测试 InMemoryFileSystem），重启后工作区仍在。
/// 会话归属（<c>sessionIds</c>）与归档/置顶集合同样由本存储维护，供 M2 的会话链路消费。
/// </summary>
internal sealed class DshWorkspaceStore {
    private readonly IFileSystem _fs;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DshWorkspaceBaselineValue? _document;

    /// <summary>构造工作区存储</summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="filePath">注册表路径（默认 <c>~/.jcc/dsh-workspaces.json</c>）</param>
    internal DshWorkspaceStore(IFileSystem fs, string? filePath = null) {
        _fs = fs;
        _filePath = filePath ?? AppDataConstants.Paths.DshWorkspacesFilePath;
    }

    /// <summary>注册表文件路径</summary>
    internal string FilePath => _filePath;

    /// <summary>路径是否为已存在目录（workspace/create 的前置校验）</summary>
    internal bool DirectoryExists(string path) => _fs.DirectoryExists(path);

    /// <summary>工作区/归档/置顶的当前快照（供 workspace/follow 基线使用）</summary>
    internal async Task<DshWorkspaceBaselineValue> ReadBaselineAsync(CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            return new DshWorkspaceBaselineValue {
                Items = [.. document.Items],
                ArchivedSessionIds = [.. document.ArchivedSessionIds],
                PinnedSessionIds = [.. document.PinnedSessionIds],
            };
        } finally {
            _gate.Release();
        }
    }

    /// <summary>workspace/create：按路径收养（已存在则原样返回，created=false）</summary>
    internal async Task<(DshWorkspaceItem Workspace, bool Created)> CreateAsync(string path, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Normalize(path);
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            if (document.Items.FirstOrDefault(item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase)) is { } existing) {
                return (existing, false);
            }
            var now = DateTimeOffset.UtcNow.ToString("O");
            var created = new DshWorkspaceItem {
                WorkspaceId = Guid.NewGuid().ToString("n"),
                Path = normalized,
                Title = TitleOf(normalized),
                SessionIds = [],
                CreatedAt = now,
                UpdatedAt = now,
            };
            document.Items.Add(created);
            await SaveAsync(document, cancellationToken);
            return (created, true);
        } finally {
            _gate.Release();
        }
    }

    /// <summary>workspace/rename：改显示名</summary>
    internal async Task<DshWorkspaceItem?> RenameAsync(string workspaceId, string title, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            return await MutateAsync(document, workspaceId, item => {
                item.Title = title;
                return item;
            }, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    /// <summary>workspace/delete：移除工作区行（会话本身留在会话存中）</summary>
    internal async Task<bool> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            var removed = document.Items.RemoveAll(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal)) > 0;
            if (removed) {
                await SaveAsync(document, cancellationToken);
            }
            return removed;
        } finally {
            _gate.Release();
        }
    }

    /// <summary>workspace/insertBefore：把工作区移到锚点之前（锚点省略则追加到末尾），返回完整顺序</summary>
    internal async Task<List<string>?> InsertBeforeAsync(string workspaceId, string? beforeWorkspaceId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            var moved = document.Items.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal));
            if (moved is null) {
                return null;
            }
            document.Items.Remove(moved);
            var anchor = beforeWorkspaceId is null
                ? -1
                : document.Items.FindIndex(item => string.Equals(item.WorkspaceId, beforeWorkspaceId, StringComparison.Ordinal));
            document.Items.Insert(anchor < 0 ? document.Items.Count : anchor, moved);
            await SaveAsync(document, cancellationToken);
            return [.. document.Items.Select(item => item.WorkspaceId)];
        } finally {
            _gate.Release();
        }
    }

    /// <summary>workspace/insertSessionBefore：把会话移到同工作区锚点之前，返回该工作区行</summary>
    internal async Task<DshWorkspaceItem?> InsertSessionBeforeAsync(string workspaceId, string sessionId, string? beforeSessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            return await MutateAsync(document, workspaceId, item => {
                item.SessionIds.RemoveAll(candidate => string.Equals(candidate, sessionId, StringComparison.Ordinal));
                var anchor = beforeSessionId is null
                    ? -1
                    : item.SessionIds.FindIndex(candidate => string.Equals(candidate, beforeSessionId, StringComparison.Ordinal));
                item.SessionIds.Insert(anchor < 0 ? item.SessionIds.Count : anchor, sessionId);
                return item;
            }, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    /// <summary>把会话登记到工作区（会话创建/恢复时调用）</summary>
    internal async Task<DshWorkspaceItem?> AttachSessionAsync(string workspaceId, string sessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            return await MutateAsync(document, workspaceId, item => {
                if (!item.SessionIds.Contains(sessionId, StringComparer.Ordinal)) {
                    item.SessionIds.Add(sessionId);
                }
                return item;
            }, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    /// <summary>按路径找已存在的工作区（会话创建时用于回填归属）</summary>
    internal async Task<DshWorkspaceItem?> FindByPathAsync(string path, CancellationToken cancellationToken = default) {
        var normalized = Normalize(path);
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            return document.Items.FirstOrDefault(item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase));
        } finally {
            _gate.Release();
        }
    }

    /// <summary>归档：加入归档集合并从置顶集合移除，返回完整归档集合</summary>
    internal async Task<List<string>> ArchiveAsync(string sessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            if (!document.ArchivedSessionIds.Contains(sessionId, StringComparer.Ordinal)) {
                document.ArchivedSessionIds.Add(sessionId);
            }
            document.PinnedSessionIds.RemoveAll(candidate => string.Equals(candidate, sessionId, StringComparison.Ordinal));
            await SaveAsync(document, cancellationToken);
            return [.. document.ArchivedSessionIds];
        } finally {
            _gate.Release();
        }
    }

    /// <summary>取消归档：移出归档集合，返回完整归档集合</summary>
    internal async Task<List<string>> UnarchiveAsync(string sessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            document.ArchivedSessionIds.RemoveAll(candidate => string.Equals(candidate, sessionId, StringComparison.Ordinal));
            await SaveAsync(document, cancellationToken);
            return [.. document.ArchivedSessionIds];
        } finally {
            _gate.Release();
        }
    }

    /// <summary>置顶：加入置顶集合，返回完整置顶集合</summary>
    internal async Task<List<string>> PinAsync(string sessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            if (!document.PinnedSessionIds.Contains(sessionId, StringComparer.Ordinal)) {
                document.PinnedSessionIds.Add(sessionId);
            }
            await SaveAsync(document, cancellationToken);
            return [.. document.PinnedSessionIds];
        } finally {
            _gate.Release();
        }
    }

    /// <summary>取消置顶：移出置顶集合，返回完整置顶集合</summary>
    internal async Task<List<string>> UnpinAsync(string sessionId, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var document = await LoadAsync(cancellationToken);
            document.PinnedSessionIds.RemoveAll(candidate => string.Equals(candidate, sessionId, StringComparison.Ordinal));
            await SaveAsync(document, cancellationToken);
            return [.. document.PinnedSessionIds];
        } finally {
            _gate.Release();
        }
    }

    /// <summary>对目标工作区行应用变更并落库（找不到返回 null）</summary>
    private async Task<DshWorkspaceItem?> MutateAsync(
        DshWorkspaceBaselineValue document,
        string workspaceId,
        Func<DshWorkspaceItem, DshWorkspaceItem> change,
        CancellationToken cancellationToken) {
        if (document.Items.FirstOrDefault(item => string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal)) is not { } target) {
            return null;
        }
        var updated = change(target);
        updated.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await SaveAsync(document, cancellationToken);
        return updated;
    }

    /// <summary>读取注册表（文件缺失或损坏返回空文档，不阻塞启动）</summary>
    private async Task<DshWorkspaceBaselineValue> LoadAsync(CancellationToken cancellationToken) {
        if (_document is not null) {
            return _document;
        }
        if (!_fs.FileExists(_filePath)) {
            return _document = new DshWorkspaceBaselineValue();
        }
        try {
            var json = await _fs.ReadAllTextAsync(_filePath, cancellationToken);
            return _document = RelaxedJsonSerializer.Deserialize(json, DshWorkspaceJsonContext.Default.DshWorkspaceBaselineValue)
                ?? new DshWorkspaceBaselineValue();
        } catch (Exception ex) when (ex is IOException or JsonException) {
            App.LogDiag($"[DSH-WEB] workspace registry unreadable: {ex.Message}");
            return _document = new DshWorkspaceBaselineValue();
        }
    }

    /// <summary>写回注册表（目录不存在则创建）</summary>
    private async Task SaveAsync(DshWorkspaceBaselineValue document, CancellationToken cancellationToken) {
        var directory = _fs.GetParentPath(_filePath);
        if (!string.IsNullOrEmpty(directory) && !_fs.DirectoryExists(directory)) {
            _fs.CreateDirectory(directory);
        }
        var json = RelaxedJsonSerializer.Serialize(document, DshWorkspaceJsonContext.Default);
        await _fs.WriteAllTextAsync(_filePath, json, cancellationToken);
    }

    /// <summary>规范化路径（去尾分隔符 + 全路径），保证同一目录只有一个工作区</summary>
    private string Normalize(string path) {
        var full = _fs.GetFullPath(path);
        var trimmed = full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        return trimmed.Length == 0 ? full : trimmed;
    }

    /// <summary>工作区显示名取目录名，根目录回退为完整路径</summary>
    private string TitleOf(string path) {
        var name = _fs.GetDirectoryName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

/// <summary>
/// 工作区注册表的持久化 JSON 上下文（AOT 源生成）。
/// 与 <c>GuiJsonContext</c> 同规格（camelCase / 缩进 / 宽容注释与尾逗号），
/// 独立存在是因为它承载的是内部（internal）协议 DTO，不能挂到公开上下文上。
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DshWorkspaceBaselineValue))]
internal sealed partial class DshWorkspaceJsonContext : JsonSerializerContext;
