namespace JoinCode.Cli;

// ─── Diff 相关 ───

/// <summary>
/// Diff 视图模式
/// </summary>
public enum DiffViewMode {
    /// <summary>
    /// 列表视图
    /// </summary>
    [EnumValue("list")]
    List,

    /// <summary>
    /// 详情视图
    /// </summary>
    [EnumValue("detail")]
    Detail,

    /// <summary>
    /// 统一格式视图
    /// </summary>
    [EnumValue("unified")]
    Unified,

    /// <summary>
    /// 分屏视图
    /// </summary>
    [EnumValue("split")]
    Split,

    /// <summary>
    /// 文件列表视图
    /// </summary>
    [EnumValue("fileList")]
    FileList
}

/// <summary>
/// Diff 来源基类 — CLI 简化版
/// </summary>
public abstract class DiffSource {
    /// <summary>
    /// 当前工作区 Diff 来源
    /// </summary>
    public sealed class Current : DiffSource { }

    /// <summary>
    /// 指定历史轮次的 Diff 来源
    /// </summary>
    public sealed class Turn : DiffSource {
        /// <summary>
        /// 轮次索引
        /// </summary>
        public int TurnIndex { get; }

        /// <summary>
        /// 该轮次的提示词预览，可选
        /// </summary>
        public string? PromptPreview { get; }

        /// <summary>
        /// 构造轮次 Diff 来源实例
        /// </summary>
        /// <param name="turnIndex">轮次索引</param>
        /// <param name="promptPreview">提示词预览，可选</param>
        public Turn(int turnIndex, string? promptPreview) {
            TurnIndex = turnIndex;
            PromptPreview = promptPreview;
        }
    }
}

/// <summary>
/// Diff 对话框状态 — record 支持 with 表达式
/// </summary>
public sealed record DiffDialogState {
    /// <summary>
    /// Diff 数据
    /// </summary>
    public required DiffData DiffData { get; init; }

    /// <summary>
    /// 当前 Diff 视图模式
    /// </summary>
    public required DiffViewMode ViewMode { get; init; }

    /// <summary>
    /// 当前选中项索引
    /// </summary>
    public int SelectedIndex { get; init; }

    /// <summary>
    /// 当前 Diff 来源索引
    /// </summary>
    public int SourceIndex { get; init; }

    /// <summary>
    /// 可选的 Diff 来源列表
    /// </summary>
    public IReadOnlyList<DiffSource> Sources { get; init; } = [];

    /// <summary>
    /// 滚动偏移量
    /// </summary>
    public int ScrollOffset { get; init; }
}

/// <summary>
/// Git Diff 服务 — CLI 简化版
/// </summary>
public sealed class GitDiffService {
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner? _gitRunner;

    /// <summary>
    /// 构造 Git Diff 服务实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="gitRunner">Git 命令执行器，可选，为 null 时返回空 Diff</param>
    public GitDiffService(IFileSystem fs, IGitCommandRunner? gitRunner = null) {
        _fs = fs;
        _gitRunner = gitRunner;
    }

    /// <summary>
    /// 异步获取当前工作区的 Git diff 数据
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析后的 Diff 数据，失败时返回空 Diff</returns>
    public async Task<DiffData> FetchDiffDataAsync(CancellationToken ct = default) {
        try {
            if (_gitRunner is null)
                return new DiffData(null, [], [], false);

            var result = await _gitRunner.ExecuteAsync("diff --stat", _fs.GetCurrentDirectory(), ct).ConfigureAwait(false);
            return ParseDiffStatOutput(result.Output);
        } catch {
            return new DiffData(null, [], [], false);
        }
    }

    private static DiffData ParseDiffStatOutput(string output) {
        var files = new List<DiffFileStats>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) {
            var parts = line.Split('|', 2);
            if (parts.Length == 2) {
                var path = parts[0].Trim();
                var stats = parts[1].Trim();
                var added = 0;
                var removed = 0;
                foreach (var c in stats) {
                    if (c == '+') added++;
                    else if (c == '-') removed++;
                }
                files.Add(new DiffFileStats(path, added, removed));
            }
        }

        return new DiffData(
            new DiffStats(files.Count, files.Sum(f => f.LinesAdded), files.Sum(f => f.LinesRemoved)),
            files,
            [],
            false);
    }
}

/// <summary>
/// Diff 对话框渲染器 — CLI 简化版
/// </summary>
public sealed class DiffDialogRenderer {
    /// <summary>
    /// 渲染 Diff 对话框状态为文本，包含统计信息和文件列表
    /// </summary>
    /// <param name="state">Diff 对话框状态</param>
    /// <returns>渲染后的文本</returns>
    public string Render(DiffDialogState state) {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Diff{AnsiStyleEnumConstants.Reset} ({state.ViewMode})");

        if (state.DiffData.Stats is not null) {
            sb.AppendLine($"  Files: {state.DiffData.Stats.FilesCount}, +{state.DiffData.Stats.LinesAdded}/-{state.DiffData.Stats.LinesRemoved}");
        }

        if (state.ViewMode == DiffViewMode.List && state.DiffData.Files.Count > 0) {
            sb.AppendLine();
            for (var i = 0; i < state.DiffData.Files.Count; i++) {
                var file = state.DiffData.Files[i];
                var marker = i == state.SelectedIndex ? ">" : " ";
                sb.AppendLine($"  {marker} {TerminalColors.Success}+{file.LinesAdded}{AnsiStyleEnumConstants.Reset} {TerminalColors.Error}-{file.LinesRemoved}{AnsiStyleEnumConstants.Reset} {file.Path}");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Diff 文件列表渲染器 — CLI 简化版
/// </summary>
public sealed class DiffFileListRenderer {
    /// <summary>
    /// 渲染变更文件列表为带增删行数标记的文本
    /// </summary>
    /// <param name="data">Diff 数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(DiffData data) {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Changed Files{AnsiStyleEnumConstants.Reset}");
        foreach (var file in data.Files) {
            sb.AppendLine($"  {TerminalColors.Success}+{file.LinesAdded}{AnsiStyleEnumConstants.Reset} {TerminalColors.Error}-{file.LinesRemoved}{AnsiStyleEnumConstants.Reset} {file.Path}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 计算滚动偏移量，CLI 简化版直接返回当前偏移
    /// </summary>
    /// <param name="selectedIndex">当前选中项索引</param>
    /// <param name="totalItems">总项数</param>
    /// <param name="currentOffset">当前滚动偏移</param>
    /// <returns>新的滚动偏移量</returns>
    public int ComputeScrollOffset(int selectedIndex, int totalItems, int currentOffset) {
        return currentOffset;
    }
}

/// <summary>
/// Diff 视图渲染器 — CLI 简化版
/// </summary>
public sealed class DiffViewRenderer {
    /// <summary>
    /// 将 Diff 行文本直接输出到终端
    /// </summary>
    /// <param name="diffLines">Diff 行文本</param>
    public void Render(string diffLines) {
        TerminalHelper.WriteLine(diffLines);
    }

    /// <summary>
    /// 根据 Diff 数据和视图模式渲染文件列表文本
    /// </summary>
    /// <param name="data">Diff 数据</param>
    /// <param name="mode">Diff 视图模式</param>
    /// <returns>渲染后的文本</returns>
    public static string Render(DiffData data, DiffViewMode mode) {
        var renderer = new DiffFileListRenderer();
        return renderer.Render(data);
    }
}