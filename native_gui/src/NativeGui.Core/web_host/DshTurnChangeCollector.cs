namespace JoinCode.Gui.WebHost;

/// <summary>
/// 一轮对话的文件改动收集器（TASK066 C7）：每次改动型工具调用开始时快照目标文件，
/// 轮次收尾时把「轮次起点内容 → 轮次终点内容」做对比，生成 deliverables 插件消费的
/// summary/diff。快照只取本轮第一次触碰前的状态，多次编辑合并为一次净改动。
/// </summary>
internal sealed class DshTurnChangeCollector {
    /// <summary>超过该大小的文件只报 oversized，不做逐行对比（前端显示"过大"）</summary>
    private const int OversizedBytes = 1024 * 1024;

    /// <summary>二进制探测窗口（出现 NUL 即视为二进制）</summary>
    private const int BinaryProbeBytes = 8192;

    /// <summary>带单个 <c>file_path</c> 参数的改动型工具</summary>
    private static readonly FrozenSet<string> SinglePathTools = new[] {
        "write",
        "edit",
        "file_edit_regex",
        "file_insert_lines",
        "file_delete_lines",
        "file_delete",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IFileSystem _fileSystem;
    private readonly string _cwd;
    private readonly Dictionary<string, FileSnapshot> _before = new(StringComparer.Ordinal);

    /// <summary>构造收集器</summary>
    /// <param name="fileSystem">宿主文件系统</param>
    /// <param name="cwd">会话工作目录（相对路径按它解析；空则取宿主当前目录）</param>
    internal DshTurnChangeCollector(IFileSystem fileSystem, string? cwd) {
        _fileSystem = fileSystem;
        _cwd = string.IsNullOrWhiteSpace(cwd) ? fileSystem.GetCurrentDirectory() : cwd;
    }

    /// <summary>
    /// 在工具调用开始时记录目标文件的轮次起点快照（同一文件只记第一次）。
    /// 非改动型工具、参数无法解析、dry_run 补丁都不记录。
    /// </summary>
    /// <param name="toolName">工具名</param>
    /// <param name="arguments">工具参数原始 JSON</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task ObserveAsync(string toolName, string arguments, CancellationToken cancellationToken = default) {
        foreach (var path in TargetPaths(toolName, arguments)) {
            var resolved = Resolve(path);
            if (_before.ContainsKey(resolved)) {
                continue;
            }
            _before[resolved] = await SnapshotAsync(resolved, cancellationToken);
        }
    }

    /// <summary>
    /// 轮次收尾对比：读终点快照，跳过未变化/不可读文件，按绝对路径排序保证 summary 与 diff 下标稳定。
    /// </summary>
    /// <param name="turn">轮次号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>改动摘要；没有净改动时返回 null（宿主不发 workspace/changes）</returns>
    internal async Task<DshChangesSummary?> CollectAsync(int turn, CancellationToken cancellationToken = default) {
        var files = new List<DshChangedFile>();
        var diffs = new List<DshChangesDiff>();
        foreach (var (path, before) in _before.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            var after = await SnapshotAsync(path, cancellationToken);
            if (before.SameAs(after)) {
                continue;
            }
            var display = Display(path);
            var (file, diff) = Compare(path, display, before, after);
            files.Add(file);
            diffs.Add(diff);
        }
        if (files.Count == 0) {
            return null;
        }
        return new DshChangesSummary {
            Turn = turn,
            Files = files,
            Diffs = diffs,
            Total = files.Count,
            Added = files.Sum(file => file.Added),
            Deleted = files.Sum(file => file.Deleted),
        };
    }

    /// <summary>从工具参数提取目标路径（改动型工具白名单；apply_patch 取 +++ 行）</summary>
    private static IEnumerable<string> TargetPaths(string toolName, string arguments) {
        if (string.IsNullOrWhiteSpace(arguments)) {
            return [];
        }
        try {
            using var document = JsonDocument.Parse(arguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return [];
            }
            if (string.Equals(toolName, "apply_patch", StringComparison.Ordinal)) {
                return IsDryRun(root) ? [] : PatchTargets(root);
            }
            if (SinglePathTools.Contains(toolName)) {
                return ReadString(root, "file_path") is { } single ? [single] : [];
            }
            if (string.Equals(toolName, "file_batch_edit", StringComparison.Ordinal)
                && root.TryGetProperty("file_paths", out var paths)
                && paths.ValueKind == JsonValueKind.Array) {
                return paths.EnumerateArray()
                    .Where(path => path.ValueKind == JsonValueKind.String && path.GetString() is { Length: > 0 })
                    .Select(path => path.GetString() ?? "")
                    .ToArray();
            }
            return [];
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>apply_patch 的 dry_run 标记</summary>
    private static bool IsDryRun(JsonElement root) =>
        root.TryGetProperty("dry_run", out var dryRun) && dryRun.ValueKind == JsonValueKind.True;

    /// <summary>从 unified diff 的 <c>+++ b/path</c> 行提取目标文件（与引擎写盘防御链同源规则）</summary>
    private static string[] PatchTargets(JsonElement root) {
        if (ReadString(root, "patch") is not { } patch) {
            return [];
        }
        var targets = new List<string>();
        foreach (var line in patch.Split('\n')) {
            if (!line.StartsWith("+++", StringComparison.Ordinal) || line.Length < 4) {
                continue;
            }
            var path = line[4..].Trim();
            if (path.StartsWith("b/", StringComparison.Ordinal)) {
                path = path[2..];
            }
            if (path.Length > 0 && !string.Equals(path, "/dev/null", StringComparison.Ordinal)) {
                targets.Add(path);
            }
        }
        return [.. targets];
    }

    /// <summary>读对象里的字符串字段（缺失/非字符串返回 null）</summary>
    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>相对路径按会话 cwd 解析；完全限定路径规范化（反斜杠统一为正斜杠）</summary>
    private string Resolve(string path) {
        var combined = Path.IsPathRooted(path) ? path : Path.Combine(_cwd, path);
        if (Path.IsPathFullyQualified(combined)) {
            try {
                combined = Path.GetFullPath(combined);
            } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
                // 非法路径保持原样：后续存在性检查会把它当作不可读快照跳过
                GuiDiag.Log($"[DSH-WEB] changes path normalize failed path={combined}: {ex.Message}");
            }
        }
        return combined.Replace('\\', '/');
    }

    /// <summary>展示路径：cwd 内用相对路径，cwd 外用绝对路径（均为正斜杠）</summary>
    private string Display(string path) {
        var cwd = _cwd.Replace('\\', '/').TrimEnd('/');
        var normalized = path.Replace('\\', '/');
        return cwd.Length > 0 && normalized.StartsWith(cwd + "/", StringComparison.Ordinal)
            ? normalized[(cwd.Length + 1)..]
            : normalized;
    }

    /// <summary>读文件快照（缺失/不可读/超大/二进制分别建模，供对比阶段分流）</summary>
    private async Task<FileSnapshot> SnapshotAsync(string path, CancellationToken cancellationToken) {
        if (!_fileSystem.FileExists(path)) {
            return FileSnapshot.Missing;
        }
        try {
            var length = _fileSystem.GetFileLength(path);
            (long Length, DateTime LastWriteUtc) stamp = (length, _fileSystem.GetLastWriteTimeUtc(path));
            if (length > OversizedBytes) {
                return FileSnapshot.OversizedOf(stamp);
            }
            var bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
            var text = IsBinary(bytes) ? null : Encoding.UTF8.GetString(bytes);
            return new FileSnapshot(true, false, false, bytes, text, stamp.Length, stamp.LastWriteUtc);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return FileSnapshot.UnreadableOf();
        }
    }

    /// <summary>二进制探测：前 8 KiB 出现 NUL 即视为二进制</summary>
    private static bool IsBinary(byte[] bytes) =>
        bytes.AsSpan(0, Math.Min(bytes.Length, BinaryProbeBytes)).IndexOf((byte)0) >= 0;

    /// <summary>逐行对比；文本不同但生成器无结果（超时）时退化为整文件替换（coarse）</summary>
    private static (DshChangedFile File, DshChangesDiff Diff) Compare(
        string path,
        string display,
        FileSnapshot before,
        FileSnapshot after) {
        if (before.Oversized || after.Oversized) {
            return (
                new DshChangedFile { Path = path, Display = display, Oversized = true },
                new DshChangesDiff { Kind = "oversized", Path = path, Display = display });
        }
        // 缺失快照的 Text 也是 null（新建/删除），只有「存在但无文本」才是二进制
        if ((before.Exists && before.Text is null) || (after.Exists && after.Text is null)) {
            return (
                new DshChangedFile { Path = path, Display = display, Binary = true },
                new DshChangesDiff { Kind = "binary", Path = path, Display = display });
        }
        var oldText = before.Exists ? Normalize(before.Text) : "";
        var newText = after.Exists ? Normalize(after.Text) : "";
        var generated = StructuredPatchGenerator.Generate(path, oldText, newText);
        var coarse = false;
        List<DshChangesHunk> hunks;
        int added;
        int deleted;
        if (generated.Length > 0) {
            hunks = ToHunks(generated);
            (added, deleted) = StructuredPatchGenerator.CountLinesChanged(generated);
        } else if (!string.Equals(oldText, newText, StringComparison.Ordinal)) {
            coarse = true;
            hunks = WholeFileHunks(oldText, newText);
            added = newText.Length == 0 ? 0 : newText.Split('\n').Length;
            deleted = oldText.Length == 0 ? 0 : oldText.Split('\n').Length;
        } else {
            hunks = [];
            added = 0;
            deleted = 0;
        }
        return (
            new DshChangedFile { Path = path, Display = display, Added = added, Deleted = deleted },
            new DshChangesDiff {
                Kind = "text",
                Path = path,
                Display = display,
                Before = before.Exists,
                After = after.Exists,
                Coarse = coarse,
                Hunks = hunks,
            });
    }

    /// <summary>统一行尾并去掉一个结尾换行（避免 diff 多出一行空行）</summary>
    private static string Normalize(string text) {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.EndsWith('\n') ? normalized[..^1] : normalized;
    }

    /// <summary>引擎 hunk → 协议 hunk（行内容补 '+' / '-' / ' ' 前缀）</summary>
    private static List<DshChangesHunk> ToHunks(StructuredPatchHunk[] hunks) =>
        [.. hunks.Select(hunk => new DshChangesHunk {
            OldStart = hunk.OldStart,
            OldLines = hunk.OldLines,
            NewStart = hunk.NewStart,
            NewLines = hunk.NewLines,
            Lines = [.. hunk.Lines.Select(line => Prefix(line.Type) + line.Content)],
        })];

    /// <summary>整文件替换块（coarse 兜底）</summary>
    private static List<DshChangesHunk> WholeFileHunks(string oldText, string newText) {
        var oldLines = oldText.Length == 0 ? [] : oldText.Split('\n');
        var newLines = newText.Length == 0 ? [] : newText.Split('\n');
        return [
            new DshChangesHunk {
                OldStart = oldLines.Length > 0 ? 1 : 0,
                OldLines = oldLines.Length,
                NewStart = newLines.Length > 0 ? 1 : 0,
                NewLines = newLines.Length,
                Lines = [.. oldLines.Select(line => "-" + line), .. newLines.Select(line => "+" + line)],
            },
        ];
    }

    /// <summary>行前缀（与前端 hunkRows 的 '+' / '-' / ' ' 约定一致）</summary>
    private static char Prefix(PatchLineType type) => type switch {
        PatchLineType.Added => '+',
        PatchLineType.Removed => '-',
        _ => ' ',
    };

    /// <summary>文件快照（Exists=false 表示缺失；Unreadable 用于跳过无法读取的路径）</summary>
    private readonly record struct FileSnapshot(
        bool Exists,
        bool Unreadable,
        bool Oversized,
        byte[] Bytes,
        string? Text,
        long Length,
        DateTime LastWriteUtc) {
        /// <summary>文件不存在</summary>
        internal static FileSnapshot Missing => new(false, false, false, [], null, 0, default);

        /// <summary>存在但读不了（权限/IO）——对比阶段跳过，不虚报改动</summary>
        internal static FileSnapshot UnreadableOf() => new(false, true, false, [], null, 0, default);

        /// <summary>超过对比上限（按长度 + 修改时间判变化）</summary>
        internal static FileSnapshot OversizedOf((long Length, DateTime LastWriteUtc) stamp) =>
            new(true, false, true, [], null, stamp.Length, stamp.LastWriteUtc);

        /// <summary>是否与另一快照等价（缺失相等；超大按长度 + mtime；其余按字节）</summary>
        internal bool SameAs(FileSnapshot other) {
            if (Unreadable || other.Unreadable) {
                return true;
            }
            if (Exists != other.Exists) {
                return false;
            }
            if (!Exists) {
                return true;
            }
            if (Oversized || other.Oversized) {
                return Length == other.Length && LastWriteUtc == other.LastWriteUtc;
            }
            return Bytes.AsSpan().SequenceEqual(other.Bytes);
        }
    }
}
