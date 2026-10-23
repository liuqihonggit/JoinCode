namespace JoinCode.Gui.WebHost;

/// <summary>
/// directoryPicker/list|createDirectory 的宿主实现（TASK066 C6）：浏览模式对齐
/// dsh-host-directory-picker-browse — 只返回可进入的子目录、隐藏项打标保留、面包屑含根、
/// 默认 1000 条截断；创建只接受单段目录名，父目录必须存在。
/// </summary>
internal sealed class DshDirectoryBrowser {
    /// <summary>单次列表条目上限（对齐 DSH browse 后端默认值）</summary>
    internal const int DefaultMaxEntries = 1000;

    private readonly IFileSystem _fileSystem;
    private readonly Func<string> _homeDirectory;
    private readonly int _maxEntries;

    /// <summary>构造浏览器</summary>
    /// <param name="fileSystem">文件系统抽象</param>
    /// <param name="homeDirectory">主目录提供器（默认取用户目录；测试可注入）</param>
    /// <param name="maxEntries">单次列表目录条目上限</param>
    internal DshDirectoryBrowser(IFileSystem fileSystem, Func<string>? homeDirectory = null, int maxEntries = DefaultMaxEntries) {
        _fileSystem = fileSystem;
        _homeDirectory = homeDirectory ?? DefaultHome;
        _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
    }

    /// <summary>列出目录（path 为空从主目录开始）；失败抛 <see cref="DshDirectoryBrowserException"/></summary>
    internal DshDirectoryListing List(string? path) {
        var home = DshPathText.Normalize(_homeDirectory());
        var target = string.IsNullOrWhiteSpace(path) ? home : DshPathText.Normalize(path);
        if (!DshPathText.IsAbsolute(target)) {
            throw new DshDirectoryBrowserException("directory-unreadable",
                $"directoryPicker/list 的 path 必须是绝对路径（例如 \"{home}\"）；收到 \"{path}\"。省略 path 表示从主目录开始浏览。");
        }
        if (!_fileSystem.DirectoryExists(target)) {
            throw new DshDirectoryBrowserException("directory-unreadable",
                $"directoryPicker/list 无法读取目录 \"{target}\"：目录不存在或不可访问。请从上级目录重新进入，或省略 path 从主目录 \"{home}\" 开始。");
        }

        var directories = SafeSubdirectories(target)
            .Select(entry => new DshDirectoryEntry {
                Name = DshPathText.DisplayName(entry),
                Path = entry,
                Hidden = DshPathText.DisplayName(entry).StartsWith('.'),
            })
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        var truncated = directories.Count > _maxEntries;
        if (truncated) {
            directories = directories.Take(_maxEntries).ToList();
        }
        return new DshDirectoryListing {
            Path = target,
            Home = home,
            Crumbs = DshPathText.Ancestors(target)
                .Select(ancestor => new DshDirectoryCrumb {
                    Name = DshPathText.DisplayName(ancestor),
                    Path = ancestor,
                    Hidden = false,
                })
                .ToList(),
            Entries = directories,
            Truncated = truncated,
        };
    }

    /// <summary>在父目录下创建单段子目录，返回新目录绝对路径；失败抛 <see cref="DshDirectoryBrowserException"/></summary>
    internal string CreateDirectory(string path, string name) {
        var parent = DshPathText.Normalize(path);
        if (!DshPathText.IsAbsolute(parent)) {
            throw new DshDirectoryBrowserException("directory-create-failed",
                $"directoryPicker/createDirectory 的 path 必须是绝对父目录（例如 \"{DshPathText.Normalize(_homeDirectory())}\"）；收到 \"{path}\"。");
        }
        if (!_fileSystem.DirectoryExists(parent)) {
            throw new DshDirectoryBrowserException("directory-create-failed",
                $"directoryPicker/createDirectory 的父目录不存在：\"{parent}\"。请先刷新目录列表再新建。");
        }
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\')) {
            throw new DshDirectoryBrowserException("directory-create-failed",
                $"directoryPicker/createDirectory 的 name 必须是单段目录名（不能为空、不能是 . 或 ..、不能包含路径分隔符）；收到 \"{name}\"。");
        }
        var target = DshPathText.Join(parent, name);
        if (_fileSystem.DirectoryExists(target) || _fileSystem.FileExists(target)) {
            throw new DshDirectoryBrowserException("directory-exists",
                $"目录 \"{target}\" 已存在。请直接进入该目录，或换一个名称后重试。");
        }
        try {
            _fileSystem.CreateDirectory(target);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            throw new DshDirectoryBrowserException("directory-create-failed",
                $"创建目录 \"{target}\" 失败：{ex.Message}。请确认父目录可写后重试。");
        }
        return target;
    }

    /// <summary>枚举直接子目录（异常目录按空处理，前端可继续浏览其它位置）</summary>
    private List<string> SafeSubdirectories(string path) {
        try {
            return _fileSystem.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Select(DshPathText.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) {
            GuiDiag.Log($"[DSH-WEB] directoryPicker/list 读取失败 path={path}: {ex.Message}");
            return [];
        }
    }

    /// <summary>默认主目录（用户目录缺失时退回当前目录）</summary>
    private static string DefaultHome() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? Environment.CurrentDirectory : home;
    }
}
