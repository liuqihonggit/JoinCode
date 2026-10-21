namespace JoinCode.Gui.WebHost;

/// <summary>
/// 工作区文件树服务（TASK058 P2-B1）：DSH 前端 sidebar-files 的 list/read/readBytes/stat 四一元方法后端。
/// 根目录 = 会话 cwd（SessionInfo.ProjectPath）；路径解析后必须落在根内（防 ../ 穿越）；
/// version 用「size-mtimeTicks」合成（前端仅透传比较失效，无需内容哈希）；changes 流属深化项。
/// </summary>
internal sealed class DshWorkspaceFilesService(DshSessionStore sessions, IFileSystem fs) {
    /// <summary>列出目录条目（目录在前、名称不区分大小写排序），截断阈值 500</summary>
    internal async Task<DshRpcResult> ListAsync(string sessionId, string path, CancellationToken cancellationToken) {
        var (error, absolutePath) = await ResolveRootedAsync(sessionId, path, cancellationToken);
        if (error is not null || absolutePath is null) {
            return error ?? AsInternalError();
        }
        if (!fs.DirectoryExists(absolutePath)) {
            return DshRpcResults.Fail("workspaceFiles/not-found", $"目录不存在：{path}；在文件树里刷新后重试");
        }
        var directories = fs.GetDirectories(absolutePath, "*", SearchOption.TopDirectoryOnly)
            .Select(name => Path.GetFileName(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        var files = fs.GetFiles(absolutePath, "*", SearchOption.TopDirectoryOnly)
            .Select(fullPath => new { Name = Path.GetFileName(fullPath), Size = TryGetLength(fullPath) })
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        var entries = new List<DshWorkspaceFileEntry>();
        foreach (var name in directories) {
            entries.Add(new DshWorkspaceFileEntry { Name = name, Type = "directory" });
        }
        foreach (var file in files) {
            entries.Add(new DshWorkspaceFileEntry { Name = file.Name, Type = "file", Size = file.Size });
        }
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshWorkspaceFilesListResult { Path = path, Entries = entries, Truncated = entries.Count > 500 },
            DshJsonContext.Default.DshWorkspaceFilesListResult));
    }

    /// <summary>读文本（v1 全量读 + 行数统计 + eof；range 分页属深化项，offset 恒 0）</summary>
    internal async Task<DshRpcResult> ReadAsync(string sessionId, string path, CancellationToken cancellationToken) {
        var (error, absolutePath) = await ResolveRootedAsync(sessionId, path, cancellationToken);
        if (error is not null || absolutePath is null) {
            return error ?? AsInternalError();
        }
        if (!fs.FileExists(absolutePath)) {
            return DshRpcResults.Fail("workspaceFiles/not-found", $"文件不存在：{path}；在文件树里刷新后重试");
        }
        var text = await fs.ReadAllTextAsync(absolutePath, cancellationToken);
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshWorkspaceFilesReadResult {
                Offset = 0, Text = text, Lines = 1 + text.Count('\n'), Eof = true,
                AbsolutePath = absolutePath, Version = VersionFor(absolutePath),
            },
            DshJsonContext.Default.DshWorkspaceFilesReadResult));
    }

    /// <summary>读字节（base64 编码返回；v1 全量读，大文件分页属深化项）</summary>
    internal async Task<DshRpcResult> ReadBytesAsync(string sessionId, string path, CancellationToken cancellationToken) {
        var (error, absolutePath) = await ResolveRootedAsync(sessionId, path, cancellationToken);
        if (error is not null || absolutePath is null) {
            return error ?? AsInternalError();
        }
        if (!fs.FileExists(absolutePath)) {
            return DshRpcResults.Fail("workspaceFiles/not-found", $"文件不存在：{path}；在文件树里刷新后重试");
        }
        var bytes = await fs.ReadAllBytesAsync(absolutePath, cancellationToken);
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshWorkspaceFilesReadBytesResult {
                Offset = 0, Data = Convert.ToBase64String(bytes), Eof = true,
                AbsolutePath = absolutePath, Version = VersionFor(absolutePath),
            },
            DshJsonContext.Default.DshWorkspaceFilesReadBytesResult));
    }

    /// <summary>文件元数据（绝对路径 + version）</summary>
    internal async Task<DshRpcResult> StatAsync(string sessionId, string path, CancellationToken cancellationToken) {
        var (error, absolutePath) = await ResolveRootedAsync(sessionId, path, cancellationToken);
        if (error is not null || absolutePath is null) {
            return error ?? AsInternalError();
        }
        if (!fs.FileExists(absolutePath)) {
            return DshRpcResults.Fail("workspaceFiles/not-found", $"文件不存在：{path}；在文件树里刷新后重试");
        }
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshWorkspaceFilesStatResult { AbsolutePath = absolutePath, Version = VersionFor(absolutePath) },
            DshJsonContext.Default.DshWorkspaceFilesStatResult));
    }

    /// <summary>解析「会话 cwd + 相对路径」为根内绝对路径；穿越/无 cwd 返回引导错误（非异常）</summary>
    private async Task<(DshRpcResult? Error, string? AbsolutePath)> ResolveRootedAsync(string sessionId, string path, CancellationToken cancellationToken) {
        var cwd = await sessions.GetSessionCwdAsync(sessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(cwd)) {
            return (DshRpcResults.Fail("workspaceFiles/rejected",
                $"会话 {sessionId} 无工作目录（meta.json 缺 ProjectPath）；用 workspace/create 建会话或重开会话后重试"), null);
        }
        // 手工段解析（弹栈式防穿越）：不用 Path.GetFullPath —— 它会给「/x/y」补盘符，
        // 与 InMemoryFileSystem 的字面量路径键不匹配；弹栈法同时杜绝「根前缀碰撞」误判
        var root = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = new List<string>();
        foreach (var part in path.Split('/', '\\')) {
            if (part is "" or ".") {
                continue;
            }
            if (part == "..") {
                if (segments.Count == 0) {
                    return (DshRpcResults.Fail("workspaceFiles/rejected",
                        $"路径 {path} 解析到工作区外（根={root}）；请使用工作区内的相对路径"), null);
                }
                segments.RemoveAt(segments.Count - 1);
            } else {
                segments.Add(part);
            }
        }
        var candidate = segments.Count == 0
            ? root
            : root + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, segments);
        return (null, candidate);
    }

    /// <summary>version 合成：size-mtimeTicks（前端透传比较失效即可，无需内容哈希）</summary>
    private string VersionFor(string absolutePath) {
        var length = TryGetLength(absolutePath) ?? 0;
        var mtime = fs.GetLastWriteTimeUtc(absolutePath).Ticks;
        return $"{length}-{mtime}";
    }

    private long? TryGetLength(string absolutePath) {
        try {
            return new FileInfo(absolutePath).Length;
        } catch (Exception) {
            return null;
        }
    }

    private static DshRpcResult AsInternalError() => DshRpcResults.Fail(
        "workspaceFiles/rejected", "路径解析失败（内部错误）；请刷新文件树后重试");
}
