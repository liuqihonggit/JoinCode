namespace JoinCode.Cli;

/// <summary>
/// Turn Diff 服务 — 对齐 TS useTurnDiffs
/// 从消息历史中提取每个 AI 对话轮次的文件编辑 diff
/// </summary>
public sealed class TurnDiffService : ITurnDiffProvider
{
    private readonly List<TurnToolCall> _toolCalls = [];
    private int _currentTurnIndex;
    private string? _currentUserPrompt;
    private DateTimeOffset _currentTimestamp;
    private readonly IClockService _clock = SystemClockService.Instance;

    public TurnDiffService()
    {
        _currentTimestamp = _clock.GetUtcNowOffset();
    }

    // 当前 Turn 的累积文件编辑
    private readonly Dictionary<string, TurnFileDiff> _currentFiles = [];

    // 已完成的 Turn 列表（倒序）
    private readonly List<TurnDiff> _completedTurns = [];

    /// <inheritdoc/>
    public void RecordUserPrompt(string prompt)
    {
        FlushCurrentTurn();

        _currentTurnIndex++;
        _currentUserPrompt = prompt;
        _currentTimestamp = _clock.GetUtcNowOffset();
    }

    /// <inheritdoc/>
    public void RecordFileEdit(string filePath, string? result, bool isNewFile = false)
    {
        var hunks = ParseHunksFromResult(result);
        RecordFileEditInternal(filePath, hunks, isNewFile, result);
    }

    /// <summary>
    /// 从 StructuredPatchHunk[] 记录文件编辑 — 对齐 TS 从 toolUseResult.structuredPatch 直接读取
    /// </summary>
    public void RecordFileEditWithPatch(string filePath, StructuredPatchHunk[] patch, bool isNewFile = false)
    {
        var hunks = ConvertPatchHunks(patch);
        RecordFileEditInternal(filePath, hunks, isNewFile);
    }

    private void RecordFileEditInternal(string filePath, StructuredHunk[] hunks, bool isNewFile, string? result = null)
    {
        var (added, removed) = CountHunkLines(hunks);

        if (_currentFiles.TryGetValue(filePath, out var existing))
        {
            var mergedHunks = new List<StructuredHunk>(existing.Hunks);
            mergedHunks.AddRange(hunks);
            _currentFiles[filePath] = existing with
            {
                Hunks = mergedHunks.ToArray(),
                LinesAdded = existing.LinesAdded + added,
                LinesRemoved = existing.LinesRemoved + removed,
                IsNewFile = existing.IsNewFile || isNewFile
            };
        }
        else
        {
            _currentFiles[filePath] = new TurnFileDiff(filePath, hunks, isNewFile, added, removed);
        }

        _toolCalls.Add(new TurnToolCall
        {
            IsUserPrompt = false,
            IsFileEdit = true,
            FilePath = filePath,
            Result = result,
            IsNewFile = isNewFile
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<TurnDiffSnapshot> GetTurnDiffs()
    {
        var allTurns = new List<TurnDiff>(_completedTurns);

        if (_currentFiles.Count > 0)
        {
            allTurns.Add(BuildTurnDiff(_currentTurnIndex, _currentUserPrompt, _currentTimestamp, _currentFiles));
        }

        allTurns.Reverse();

        return allTurns.Select(t => new TurnDiffSnapshot
        {
            TurnIndex = t.TurnIndex,
            UserPromptPreview = t.UserPromptPreview,
            Timestamp = t.Timestamp,
            FilesCount = t.Stats.FilesCount,
            LinesAdded = t.Stats.LinesAdded,
            LinesRemoved = t.Stats.LinesRemoved
        }).ToArray();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _toolCalls.Clear();
        _completedTurns.Clear();
        _currentFiles.Clear();
        _currentTurnIndex = 0;
        _currentUserPrompt = null;
        _currentTimestamp = _clock.GetUtcNowOffset();
    }

    /// <summary>
    /// 获取完整的 Turn Diff 列表（包含文件详情）— 对齐 TS getFullTurnDiffs
    /// </summary>
    public IReadOnlyList<TurnDiff> GetFullTurnDiffs()
    {
        FlushCurrentTurn();

        var allTurns = new List<TurnDiff>(_completedTurns);
        allTurns.Reverse();
        return allTurns;
    }

    /// <summary>
    /// 将 TurnDiff 转换为 DiffData — 对齐 TS turnDiffToDiffData
    /// </summary>
    public DiffData TurnDiffToDiffData(TurnDiff turnDiff)
    {
        var files = new List<DiffFileStats>();
        var hunks = new Dictionary<string, StructuredHunk[]>();

        foreach (var (filePath, fileDiff) in turnDiff.Files)
        {
            files.Add(new DiffFileStats(filePath, fileDiff.LinesAdded, fileDiff.LinesRemoved, IsNewFile: fileDiff.IsNewFile));
            hunks[filePath] = fileDiff.Hunks;
        }

        return new DiffData(turnDiff.Stats, files, hunks, false);
    }

    private void FlushCurrentTurn()
    {
        if (_currentFiles.Count > 0)
        {
            _completedTurns.Add(BuildTurnDiff(_currentTurnIndex, _currentUserPrompt, _currentTimestamp, _currentFiles));
            _currentFiles.Clear();
        }
    }

    private static TurnDiff BuildTurnDiff(int turnIndex, string? userPrompt, DateTimeOffset timestamp, Dictionary<string, TurnFileDiff> files)
    {
        var totalAdded = files.Values.Sum(f => f.LinesAdded);
        var totalRemoved = files.Values.Sum(f => f.LinesRemoved);

        return new TurnDiff(
            turnIndex,
            userPrompt,
            timestamp,
            new Dictionary<string, TurnFileDiff>(files),
            new DiffStats(files.Count, totalAdded, totalRemoved));
    }

    private static StructuredHunk[] ParseHunksFromResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return [];

        return DiffParser.ParseStructured(result)
            .SelectMany(kvp => kvp.Value)
            .ToArray();
    }

    private static StructuredHunk[] ConvertPatchHunks(StructuredPatchHunk[] patch)
    {
        if (patch.Length == 0) return [];

        var result = new StructuredHunk[patch.Length];
        for (var i = 0; i < patch.Length; i++)
        {
            var p = patch[i];
            var lines = new DiffLine[p.Lines.Length];
            for (var j = 0; j < p.Lines.Length; j++)
            {
                var pl = p.Lines[j];
                lines[j] = new DiffLine(
                    pl.Type,
                    pl.Content,
                    pl.OldLineNumber,
                    pl.NewLineNumber);
            }

            result[i] = new StructuredHunk(
                p.OldStart, p.OldLines,
                p.NewStart, p.NewLines,
                p.Header,
                lines);
        }

        return result;
    }

    private static (int Added, int Removed) CountHunkLines(StructuredHunk[] hunks)
    {
        var added = 0;
        var removed = 0;
        foreach (var hunk in hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.Type == PatchLineType.Added) added++;
                else if (line.Type == PatchLineType.Removed) removed++;
            }
        }
        return (added, removed);
    }
}

/// <summary>
/// Turn 工具调用记录 — 用于传递给 TurnDiffService
/// </summary>
public sealed record TurnToolCall
{
    /// <summary>
    /// 是否为用户提示（标记新 Turn 的开始）
    /// </summary>
    public required bool IsUserPrompt { get; init; }

    /// <summary>
    /// 用户提示文本（仅 IsUserPrompt=true 时有效）
    /// </summary>
    public string? UserPrompt { get; init; }

    /// <summary>
    /// 是否为文件编辑工具调用
    /// </summary>
    public required bool IsFileEdit { get; init; }

    /// <summary>
    /// 文件路径
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// 工具结果（diff 输出）
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// 是否为新文件
    /// </summary>
    public bool IsNewFile { get; init; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
