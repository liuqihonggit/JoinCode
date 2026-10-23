namespace JoinCode.Gui.WebHost;

/// <summary>
/// fileReferences/list 的宿主实现（TASK066 C6）：会话工作目录内的 @ 引用补全。
/// 语义对齐 dsh-file-reference-local — 带 / 的查询直接列该目录子项；裸词在整棵目录树上模糊排序；
/// 默认排除构建产物与依赖目录、隐藏项仅在显式输入 . 时可见、最多返回 20 条。
/// </summary>
internal sealed class DshFileReferenceService {
    /// <summary>单次补全返回上限（对齐 DSH 默认 20 条）</summary>
    internal const int DefaultMaxResults = 20;

    /// <summary>索引条目上限（对齐 DSH 默认 5 万条）</summary>
    internal const int DefaultMaxEntries = 50_000;

    /// <summary>索引缓存时长：避免每次击键都全树遍历</summary>
    internal static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(5);

    /// <summary>默认排除目录（版本库/依赖/构建产物，对齐 DSH 默认清单）</summary>
    private static readonly FrozenSet<string> ExcludedDirectories = new[] {
        ".git", "node_modules", "dist", "build", "out", "coverage", "target", ".next",
        ".nuxt", ".turbo", ".venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".gradle",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly DshSessionStore _sessions;
    private readonly IFileSystem _fileSystem;
    private readonly int _maxResults;
    private readonly int _maxEntries;
    private readonly TimeSpan _cacheTtl;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构造文件引用服务</summary>
    /// <param name="sessions">会话存储（解析 agentId → 工作目录）</param>
    /// <param name="fileSystem">文件系统抽象</param>
    /// <param name="maxResults">单次返回上限</param>
    /// <param name="maxEntries">索引条目上限</param>
    /// <param name="cacheTtl">索引缓存时长</param>
    internal DshFileReferenceService(
        DshSessionStore sessions,
        IFileSystem fileSystem,
        int maxResults = DefaultMaxResults,
        int maxEntries = DefaultMaxEntries,
        TimeSpan? cacheTtl = null) {
        _sessions = sessions;
        _fileSystem = fileSystem;
        _maxResults = maxResults > 0 ? maxResults : DefaultMaxResults;
        _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
    }

    /// <summary>按查询文本返回候选；会话不存在抛 <see cref="DshFileReferenceException"/></summary>
    internal async Task<IReadOnlyList<DshFileReferenceItem>> ListAsync(string sessionId, string query, CancellationToken cancellationToken) {
        var cwd = await _sessions.GetSessionCwdAsync(sessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(cwd)) {
            throw new DshFileReferenceException("session/not-found",
                $"fileReferences/list 找不到会话 \"{sessionId}\" 的工作目录；请从侧栏选择一个已存在的会话（引擎会话目录 ~/.jcc/sessions）。");
        }
        var root = DshPathText.Normalize(cwd);
        if (!_fileSystem.DirectoryExists(root)) {
            return [];
        }
        var normalizedQuery = query.Replace('\\', '/');
        return normalizedQuery.Length == 0 || normalizedQuery.Contains('/')
            ? ListDirectory(root, normalizedQuery, cancellationToken)
            : Rank(VisibleForGlobalQuery(GetIndex(root, cancellationToken), normalizedQuery), normalizedQuery);
    }

    /// <summary>带斜杠/空查询：直接列显示目录的直接子项</summary>
    private IReadOnlyList<DshFileReferenceItem> ListDirectory(string root, string query, CancellationToken cancellationToken) {
        var slash = query.LastIndexOf('/');
        var displayDirectory = slash < 0 ? "" : query[..(slash + 1)];
        var fragment = slash < 0 ? "" : query[(slash + 1)..];
        if (displayDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(ExcludedDirectories.Contains)) {
            return [];
        }
        var resolved = DshPathText.ResolveWithin(root, displayDirectory);
        if (resolved is null || !_fileSystem.DirectoryExists(resolved)) {
            return [];
        }

        var candidates = new List<DshFileReferenceItem>();
        foreach (var directory in SafeEntries(() => _fileSystem.GetDirectories(resolved, "*", SearchOption.TopDirectoryOnly))) {
            var name = DshPathText.DisplayName(directory);
            if ((name.StartsWith('.') && !fragment.StartsWith('.')) || ExcludedDirectories.Contains(name)) {
                continue;
            }
            candidates.Add(new DshFileReferenceItem { Path = displayDirectory + name, Kind = "directory" });
        }
        foreach (var file in SafeEntries(() => _fileSystem.GetFiles(resolved, "*", SearchOption.TopDirectoryOnly))) {
            var name = DshPathText.DisplayName(file);
            if (name.StartsWith('.') && !fragment.StartsWith('.')) {
                continue;
            }
            candidates.Add(new DshFileReferenceItem { Path = displayDirectory + name, Kind = "file" });
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Rank(candidates, fragment);
    }

    /// <summary>模糊索引（按工作目录缓存，TTL 内复用）</summary>
    private IReadOnlyList<DshFileReferenceItem> GetIndex(string root, CancellationToken cancellationToken) {
        lock (_gate) {
            if (_cache.TryGetValue(root, out var cached) && DateTime.UtcNow - cached.BuiltAt < _cacheTtl) {
                return cached.Items;
            }
        }
        var items = ScanIndex(root, cancellationToken);
        lock (_gate) {
            _cache[root] = new CacheEntry(DateTime.UtcNow, items);
        }
        return items;
    }

    /// <summary>广度优先扫描工作目录（跳过排除目录与重解析点，条目数封顶）</summary>
    private List<DshFileReferenceItem> ScanIndex(string root, CancellationToken cancellationToken) {
        var indexed = new List<DshFileReferenceItem>();
        var queue = new Queue<(string Absolute, string Relative)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((root, ""));
        visited.Add(root);
        while (queue.Count > 0 && indexed.Count < _maxEntries) {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, relative) = queue.Dequeue();
            var children = SafeEntries(() => _fileSystem.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                .Select(path => (Name: DshPathText.DisplayName(path), Path: path, IsDirectory: true))
                .Concat(SafeEntries(() => _fileSystem.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
                    .Select(path => (Name: DshPathText.DisplayName(path), Path: path, IsDirectory: false)))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal);
            foreach (var child in children) {
                if (indexed.Count >= _maxEntries) {
                    break;
                }
                if (child.IsDirectory) {
                    if (ExcludedDirectories.Contains(child.Name) || IsReparsePoint(child.Path) || !visited.Add(child.Path)) {
                        continue;
                    }
                    var childRelative = relative.Length == 0 ? child.Name : $"{relative}/{child.Name}";
                    indexed.Add(new DshFileReferenceItem { Path = childRelative, Kind = "directory" });
                    queue.Enqueue((child.Path, childRelative));
                } else {
                    indexed.Add(new DshFileReferenceItem {
                        Path = relative.Length == 0 ? child.Name : $"{relative}/{child.Name}",
                        Kind = "file",
                    });
                }
            }
        }
        return indexed;
    }

    /// <summary>全局查询的隐藏项过滤：查询显式输入 . 时才返回隐藏路径</summary>
    private static IReadOnlyList<DshFileReferenceItem> VisibleForGlobalQuery(
        IReadOnlyList<DshFileReferenceItem> items,
        string query) =>
        query.StartsWith('.') || query.Contains("/.")
            ? items
            : items.Where(item => !item.Path.Split('/').Any(segment => segment.StartsWith('.'))).ToList();

    /// <summary>按 DSH 规则打分排序：名称相等/前缀/包含/路径包含/子序列，目录小幅优先</summary>
    private IReadOnlyList<DshFileReferenceItem> Rank(IEnumerable<DshFileReferenceItem> candidates, string query) {
        var needle = query.ToLowerInvariant();
        return candidates
            .Select(item => (Item: item, Score: ScoreCandidate(item, needle)))
            .Where(entry => entry.Score is not null)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.Kind == "directory" ? 0 : 1)
            .ThenBy(entry => query.Length == 0 ? 0 : entry.Item.Path.Length)
            .ThenBy(entry => entry.Item.Path, StringComparer.Ordinal)
            .Take(_maxResults)
            .Select(entry => entry.Item)
            .ToList();
    }

    /// <summary>单条候选评分；不匹配返回 null</summary>
    private static int? ScoreCandidate(DshFileReferenceItem item, string needle) {
        if (needle.Length == 0) {
            return 0;
        }
        var path = item.Path.ToLowerInvariant();
        var slash = path.LastIndexOf('/');
        var name = path[(slash + 1)..];
        var bonus = item.Kind == "directory" ? 25 : 0;
        if (name == needle) {
            return 1000 + bonus;
        }
        if (name.StartsWith(needle, StringComparison.Ordinal)) {
            return 900 + bonus;
        }
        if (name.Contains(needle, StringComparison.Ordinal)) {
            return 700 + bonus;
        }
        if (path.Contains(needle, StringComparison.Ordinal)) {
            return 500 + bonus;
        }
        return SubsequenceScore(path, needle) is { } subsequence ? 300 + subsequence + bonus : null;
    }

    /// <summary>子序列匹配分（间隙越小分越高）</summary>
    private static int? SubsequenceScore(string target, string needle) {
        var cursor = 0;
        var gap = 0;
        foreach (var character in needle) {
            var found = target.IndexOf(character, cursor);
            if (found < 0) {
                return null;
            }
            gap += found - cursor;
            cursor = found + 1;
        }
        return Math.Max(0, 100 - gap);
    }

    /// <summary>是否重解析点（避免符号链接环导致索引死循环）</summary>
    private bool IsReparsePoint(string directory) {
        try {
            return (_fileSystem.GetFileAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) {
            return false;
        }
    }

    /// <summary>目录枚举失败按空处理（权限不足的目录不阻断补全）</summary>
    private static IEnumerable<string> SafeEntries(Func<IEnumerable<string>> enumerate) {
        try {
            return enumerate();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
            return [];
        }
    }

    /// <summary>索引缓存条目</summary>
    private sealed record CacheEntry(DateTime BuiltAt, IReadOnlyList<DshFileReferenceItem> Items);
}
