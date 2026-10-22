namespace NativeTui.Core.Repl;

/// <summary>@ 文件补全建议项 — 工作区相对路径（/ 分隔跨平台统一形态）。</summary>
/// <param name="RelativePath">相对工作区根的路径（/ 分隔 — 展示与 @ 引用共用）。</param>
public sealed record AtFileSuggestion(string RelativePath);

/// <summary>
/// @ 文件补全数据层 — 对齐基准 useTypeahead.tsx fileSuggestions 分支（纯数据层，无 UI）：
/// 触发键位/替换接线属 ReplShell/InputBuffer 域（未决问题）。工作区文件枚举
///（遍历层剪枝跳过 .git/node_modules 等忽略目录）+ 大仓库截断守卫 + 大小写不敏感包含过滤
/// + 路径短优先排序 + 上限 20。首次建议时建立内存索引（缓存直到 Invalidate —
/// 基准后台缓存刷新的同步简化，宿主接线可在目录变更后调用 Invalidate 重建）。
/// </summary>
public sealed class AtFileSuggester
{
    /// <summary>建议条数上限（基准 typeahead file suggestions 上限 20）。</summary>
    public const int MaxSuggestions = 20;

    /// <summary>枚举文件数截断守卫（大仓库防卡死 — 基准 git ls-files 5s 超时的同步等价）。</summary>
    public const int MaxIndexedFiles = 50_000;

    /// <summary>忽略目录名（不踏入索引 — 版本库/依赖产物/构建产物/临时归置目录）。</summary>
    private static readonly string[] IgnoredDirectoryNames =
        [".git", "node_modules", "bin", "obj", "artifacts", ".xxx", ".vs", ".idea", "dist"];

    private readonly string _rootPath;
    private readonly StringComparison _pathComparison;
    private List<string>? _indexedFiles;

    /// <summary>创建补全器 — rootPath 为工作区根目录；路径匹配大小写策略按平台（Windows 不敏感）。</summary>
    public AtFileSuggester(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>建议清单 — prefix 大小写不敏感包含匹配（空前缀 = 全部）；路径短优先 + 字母序，上限 MaxSuggestions。</summary>
    public IReadOnlyList<AtFileSuggestion> Suggest(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return EnsureIndexed()
            .Where(path => prefix.Length == 0 || path.Contains(prefix, _pathComparison))
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(path => new AtFileSuggestion(path))
            .ToList();
    }

    /// <summary>清空索引（下次建议重建 — 会话恢复/目录结构变更后由接线方消费）。</summary>
    public void Invalidate() => _indexedFiles = null;

    /// <summary>确保索引就绪（懒加载 — 首次建议触发枚举，之后缓存复用；目录不可访问静默跳过）。</summary>
    private List<string> EnsureIndexed()
    {
        if (_indexedFiles is not null)
        {
            return _indexedFiles;
        }

        var ignored = IgnoredDirectoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _indexedFiles = EnumerateFilesSkippingIgnored(ignored)
            .Take(MaxIndexedFiles)
            .Select(ToRelativePath)
            .ToList();
        _indexedFiles.Sort(StringComparer.Ordinal);
        return _indexedFiles;
    }

    /// <summary>广度优先枚举 — 忽略目录整棵跳过（剪枝在遍历层非过滤层 — .git/node_modules 不踏入）；
    /// 目录消失/拒绝访问静默跳过（枚举竞态容忍）。</summary>
    private IEnumerable<string> EnumerateFilesSkippingIgnored(HashSet<string> ignored)
    {
        var pending = new Queue<string>();
        pending.Enqueue(_rootPath);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(directory);
                directories = Directory.EnumerateDirectories(directory);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var subdirectory in directories.Where(name => !ignored.Contains(Path.GetFileName(name))))
            {
                pending.Enqueue(subdirectory);
            }
        }
    }

    /// <summary>绝对路径 → 相对路径（/ 分隔统一展示形态）。</summary>
    private string ToRelativePath(string absolutePath) =>
        Path.GetRelativePath(_rootPath, absolutePath).Replace('\\', '/');
}
