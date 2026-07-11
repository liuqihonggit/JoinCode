namespace JoinCode.Cli;

/// <summary>
/// CLI 交互组件 — TUI 组件的 CLI 简化替代
/// </summary>

// ─── Selector ───

/// <summary>
/// 交互式选择器 — CLI 简化版，使用数字选择替代 TUI 上下键
/// </summary>
public sealed class Selector<T>
{
    private readonly string _title;
    private readonly (T Item, string DisplayText, string SearchKey)[] _items;

    public Selector(string title, (T Item, string DisplayText, string SearchKey)[] items)
    {
        _title = title;
        _items = items;
    }

    public Selector(string title, T[] items, Func<T, string> displaySelector, Func<T, string>? searchSelector = null, bool enableSearch = false)
    {
        _title = title;
        _items = items.Select(i => (i, displaySelector(i), searchSelector?.Invoke(i) ?? displaySelector(i))).ToArray();
    }

    public async Task<SelectorResult<T>> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{_title}{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        for (var i = 0; i < _items.Length; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} {_items[i].DisplayText}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteRaw($"请输入选择 (1-{_items.Length}, Esc 取消): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return new SelectorResult<T> { Cancelled = true };
        }

        try
        {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new SelectorResult<T> { Cancelled = true };
            }

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= _items.Length)
            {
                return new SelectorResult<T> { Selected = _items[index - 1].Item, Cancelled = false };
            }

            return new SelectorResult<T> { Cancelled = true };
        }
        catch
        {
            return new SelectorResult<T> { Cancelled = true };
        }
    }
}

/// <summary>
/// 选择器结果
/// </summary>
public sealed class SelectorResult<T>
{
    public T Selected { get; init; } = default!;
    public required bool Cancelled { get; init; }
}

// ─── Dialog ───

/// <summary>
/// 对话框 — CLI 简化版
/// </summary>
public sealed class Dialog
{
    private readonly string _title;
    private readonly string _content;
    private readonly string[] _buttons;

    public Dialog(string title, string content, string[] buttons)
    {
        _title = title;
        _content = content;
        _buttons = buttons;
    }

    public async Task<DialogResult> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{_title}{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(_content);
        TerminalHelper.NewLine();

        for (var i = 0; i < _buttons.Length; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} {_buttons[i]}");
        }

        TerminalHelper.NewLine();

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }

        try
        {
            TerminalHelper.WriteRaw($"请选择 (1-{_buttons.Length}, Esc 取消): ");
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new DialogResult { Cancelled = true, SelectedIndex = -1 };
            }

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= _buttons.Length)
            {
                return new DialogResult { Cancelled = false, SelectedIndex = index - 1 };
            }

            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }
        catch
        {
            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }
    }

    public static async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteRaw($"{message} (y/N): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return false;

        try
        {
            var response = TerminalHelper.ReadLine();
            return response?.ToLowerInvariant() == "y";
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> PromptAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteRaw($"{message}: ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return null;

        try
        {
            return TerminalHelper.ReadLine();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 对话框结果
/// </summary>
public sealed class DialogResult
{
    public required bool Cancelled { get; init; }
    public required int SelectedIndex { get; init; }
}

// ─── Confirmation ───

/// <summary>
/// 确认对话框 — CLI 简化版
/// </summary>
public static class Confirmation
{
    public static Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        return Dialog.ConfirmAsync(message, ct);
    }

    public static Task<bool> ShowAsync(string message, CancellationToken ct = default)
    {
        return Dialog.ConfirmAsync(message, ct);
    }
}

// ─── TabPanel ───

/// <summary>
/// Tab 面板 — CLI 简化版，顺序显示所有 Tab 内容
/// </summary>
public sealed class TabPanel
{
    private readonly string[] _tabNames;
    private readonly Func<int, string> _contentProvider;

    public TabPanel(string[] tabNames, Func<int, string> contentProvider)
    {
        _tabNames = tabNames;
        _contentProvider = contentProvider;
    }

    public async Task ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        for (var i = 0; i < _tabNames.Length; i++)
        {
            if (i > 0)
            {
                TerminalHelper.WriteLine();
                TerminalHelper.WriteLine($"{TerminalColors.Divider}{new string('─', 40)}{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine();
            }

            TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}[{_tabNames[i]}]{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine();

            var content = _contentProvider(i);
            if (!string.IsNullOrWhiteSpace(content))
            {
                TerminalHelper.WriteLine(content);
            }
        }
    }
}

// ─── PaginatedList ───

/// <summary>
/// 分页列表 — CLI 简化版
/// </summary>
public sealed class PaginatedList<T>
{
    private readonly string _title;
    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, string> _displaySelector;
    private readonly int _pageSize;

    public PaginatedList(string title, IReadOnlyList<T> items, Func<T, string> displaySelector, int pageSize = 20)
    {
        _title = title;
        _items = items;
        _displaySelector = displaySelector;
        _pageSize = pageSize;
    }

    public async Task ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{_title}{AnsiStyleConstants.Reset} ({_items.Count} 项)");
        TerminalHelper.NewLine();

        var displayCount = Math.Min(_items.Count, _pageSize);
        for (var i = 0; i < displayCount; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} {_displaySelector(_items[i])}");
        }

        if (_items.Count > _pageSize)
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"{TerminalColors.Muted}  ... 还有 {_items.Count - _pageSize} 项未显示{AnsiStyleConstants.Reset}");
        }
    }
}

// ─── StepFlow / Step ───

/// <summary>
/// 步骤流程 — CLI 简化版
/// </summary>
public sealed class StepFlow
{
    private readonly Step[] _steps;
    private readonly string? _title;

    public StepFlow(Step[] steps)
    {
        _steps = steps;
    }

    public StepFlow(string title, Step[] steps)
    {
        _title = title;
        _steps = steps;
    }

    public async Task<int> ShowAsync(CancellationToken ct = default)
    {
        if (_title is not null)
        {
            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{_title}{AnsiStyleConstants.Reset}");
            TerminalHelper.NewLine();
        }

        for (var i = 0; i < _steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = _steps[i];
            TerminalHelper.WriteLine($"{TerminalColors.Primary}{AnsiStyleConstants.Bold}步骤 {i + 1}/{_steps.Length}{AnsiStyleConstants.Reset}: {step.Title}");

            if (!string.IsNullOrEmpty(step.Description))
            {
                TerminalHelper.WriteLine(step.Description);
            }

            if (step.Action is not null)
            {
                await step.Action(ct).ConfigureAwait(false);
            }
        }

        return _steps.Length;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await ShowAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 步骤定义
/// </summary>
public sealed class Step
{
    public string Title { get; init; }
    public string? Description { get; init; }
    public Func<CancellationToken, Task>? Action { get; init; }

    public Step(string title, string? description = null)
    {
        Title = title;
        Description = description;
    }
}

// ─── ProviderPicker ───

/// <summary>
/// 供应商选择器 — CLI 简化版
/// </summary>
public sealed class ProviderPicker
{
    public static string? Show(string defaultProvider, string title, string hint)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}{title}{AnsiStyleConstants.Reset}");
        if (!string.IsNullOrEmpty(hint))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{hint}{AnsiStyleConstants.Reset}");
        }
        TerminalHelper.NewLine();

        var providers = ProviderDefinitionRegistry.RegisteredProviders
            .Select(p => ProviderDefinitionRegistry.TryGet(p))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        for (var i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            var marker = p.ProviderName == defaultProvider ? " (默认)" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} {p.DisplayName}{marker}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteRaw($"请选择供应商 (1-{providers.Count}, 直接回车使用默认): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return defaultProvider;

        try
        {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return defaultProvider;

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= providers.Count)
            {
                return providers[index - 1].ProviderName;
            }

            return defaultProvider;
        }
        catch
        {
            return defaultProvider;
        }
    }
}

// ─── ContextData / ContextCategory / ContextVisualizer ───

/// <summary>
/// 上下文数据
/// </summary>
public sealed class ContextData
{
    public string Model { get; set; } = string.Empty;
    public int TotalTokens { get; set; }
    public int MaxTokens { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public double Percentage { get; set; }
    public List<ContextCategory> Categories { get; set; } = [];
}

/// <summary>
/// 上下文类别
/// </summary>
public sealed class ContextCategory
{
    public string Name { get; }
    public int TokenCount { get; }
    public int SortOrder { get; }

    public ContextCategory(string name, int tokenCount, int sortOrder = 0)
    {
        Name = name;
        TokenCount = tokenCount;
        SortOrder = sortOrder;
    }
}

/// <summary>
/// 上下文可视化器 — CLI 简化版
/// </summary>
public sealed class ContextVisualizer
{
    public string Render(ContextData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}Context Window{AnsiStyleConstants.Reset}");
        sb.AppendLine($"  Model: {data.Model}");
        sb.AppendLine($"  Tokens: {data.TotalTokens:N0} / {data.MaxTokens:N0}");

        if (data.Categories.Count > 0)
        {
            sb.AppendLine();
            foreach (var cat in data.Categories.OrderBy(c => c.SortOrder))
            {
                var percentage = data.MaxTokens > 0 ? (double)cat.TokenCount / data.MaxTokens * 100 : 0;
                var bar = new string('█', (int)Math.Max(1, percentage / 5));
                sb.AppendLine($"  {TerminalColors.Primary}{cat.Name,-12}{AnsiStyleConstants.Reset} {bar} {cat.TokenCount:N0} ({percentage:F1}%)");
            }
        }

        return sb.ToString();
    }

    public static string Render(IReadOnlyList<ContextData> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}Context Window{AnsiStyleConstants.Reset}");
        sb.AppendLine();

        foreach (var d in data.OrderByDescending(d => d.TokenCount))
        {
            var bar = new string('█', (int)Math.Max(1, d.Percentage / 5));
            sb.AppendLine($"  {TerminalColors.Primary}{d.Category,-12}{AnsiStyleConstants.Reset} {bar} {d.TokenCount:N0} ({d.Percentage:F1}%)");
        }

        return sb.ToString();
    }
}

// ─── FeedbackState / FeedbackRenderer / FeedbackStep / FeedbackRedactor ───

/// <summary>
/// 反馈状态
/// </summary>
public sealed class FeedbackState
{
    public FeedbackStep Step { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
}

/// <summary>
/// 反馈步骤
/// </summary>
public enum FeedbackStep
{
    UserInput,
    Rating,
    Comment,
    Confirm,
    Done
}

/// <summary>
/// 反馈渲染器 — CLI 简化版
/// </summary>
public sealed class FeedbackRenderer
{
    public string Render(FeedbackState state)
    {
        var sb = new StringBuilder();

        switch (state.Step)
        {
            case FeedbackStep.UserInput:
                sb.AppendLine($"{AnsiStyleConstants.Bold}反馈{AnsiStyleConstants.Reset}");
                sb.AppendLine("请输入您的反馈内容:");
                break;
            case FeedbackStep.Done:
                if (state.IsSuccess)
                {
                    sb.AppendLine($"{TerminalColors.Success}反馈已提交{AnsiStyleConstants.Reset}");
                    if (!string.IsNullOrEmpty(state.Description))
                    {
                        sb.AppendLine($"  内容: {state.Description}");
                    }
                }
                else
                {
                    sb.AppendLine($"{TerminalColors.Error}反馈提交失败{AnsiStyleConstants.Reset}");
                }
                break;
            default:
                sb.AppendLine($"反馈步骤: {state.Step}");
                break;
        }

        return sb.ToString();
    }

    public static void ShowRating(int rating)
    {
        var stars = new string('★', rating) + new string('☆', 5 - rating);
        TerminalHelper.WriteLine($"  评分: {TerminalColors.Warning}{stars}{AnsiStyleConstants.Reset}");
    }

    public static void ShowComment(string comment)
    {
        if (!string.IsNullOrWhiteSpace(comment))
        {
            TerminalHelper.WriteLine($"  评论: {comment}");
        }
    }
}

/// <summary>
/// 反馈脱敏器
/// </summary>
public static class FeedbackRedactor
{
    public static string Redact(string text)
    {
        return text;
    }
}

// ─── BridgeConnectionState / BridgeStatusIndicator ───

/// <summary>
/// Bridge 连接状态
/// </summary>
public enum BridgeConnectionState
{
    Idle,
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Bridge 状态指示器 — CLI 简化版
/// </summary>
public static class BridgeStatusIndicator
{
    public static string Render(BridgeConnectionState state) => GetStatusText(state);

    public static string GetStatusText(BridgeConnectionState state) => state switch
    {
        BridgeConnectionState.Connected => $"{TerminalColors.Success}● 已连接{AnsiStyleConstants.Reset}",
        BridgeConnectionState.Connecting => $"{TerminalColors.Warning}● 连接中...{AnsiStyleConstants.Reset}",
        BridgeConnectionState.Disconnected => $"{TerminalColors.Muted}○ 未连接{AnsiStyleConstants.Reset}",
        BridgeConnectionState.Error => $"{TerminalColors.Error}● 错误{AnsiStyleConstants.Reset}",
        _ => $"{TerminalColors.Muted}○ 未知{AnsiStyleConstants.Reset}"
    };
}

// ─── Diff 相关 ───

/// <summary>
/// Diff 视图模式
/// </summary>
public enum DiffViewMode
{
    List,
    Detail,
    Unified,
    Split,
    FileList
}

/// <summary>
/// Diff 来源基类 — CLI 简化版
/// </summary>
public abstract class DiffSource
{
    public sealed class Current : DiffSource { }

    public sealed class Turn : DiffSource
    {
        public int TurnIndex { get; }
        public string? PromptPreview { get; }

        public Turn(int turnIndex, string? promptPreview)
        {
            TurnIndex = turnIndex;
            PromptPreview = promptPreview;
        }
    }
}

/// <summary>
/// Diff 对话框状态 — record 支持 with 表达式
/// </summary>
public sealed record DiffDialogState
{
    public required DiffData DiffData { get; init; }
    public required DiffViewMode ViewMode { get; init; }
    public int SelectedIndex { get; init; }
    public int SourceIndex { get; init; }
    public IReadOnlyList<DiffSource> Sources { get; init; } = [];
    public int ScrollOffset { get; init; }
}

/// <summary>
/// Git Diff 服务 — CLI 简化版
/// </summary>
public sealed class GitDiffService
{
    private readonly IFileSystem _fs;
    private readonly IProcessService? _processService;

    public GitDiffService(IFileSystem fs, IProcessService? processService = null)
    {
        _fs = fs;
        _processService = processService;
    }

    public async Task<DiffData> FetchDiffDataAsync(CancellationToken ct = default)
    {
        try
        {
            if (_processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = "git",
                    Arguments = "diff --stat",
                    WorkingDirectory = _fs.GetCurrentDirectory()
                };

                var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);
                return ParseDiffStatOutput(result.StandardOutput);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --stat",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _fs.GetCurrentDirectory()
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            _ = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return ParseDiffStatOutput(output);
        }
        catch
        {
            return new DiffData(null, [], [], false);
        }
    }

    private static DiffData ParseDiffStatOutput(string output)
    {
        var files = new List<DiffFileStats>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('|', 2);
            if (parts.Length == 2)
            {
                var path = parts[0].Trim();
                var stats = parts[1].Trim();
                var added = 0;
                var removed = 0;
                foreach (var c in stats)
                {
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
public sealed class DiffDialogRenderer
{
    public string Render(DiffDialogState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}Diff{AnsiStyleConstants.Reset} ({state.ViewMode})");

        if (state.DiffData.Stats is not null)
        {
            sb.AppendLine($"  Files: {state.DiffData.Stats.FilesCount}, +{state.DiffData.Stats.LinesAdded}/-{state.DiffData.Stats.LinesRemoved}");
        }

        if (state.ViewMode == DiffViewMode.List && state.DiffData.Files.Count > 0)
        {
            sb.AppendLine();
            for (var i = 0; i < state.DiffData.Files.Count; i++)
            {
                var file = state.DiffData.Files[i];
                var marker = i == state.SelectedIndex ? ">" : " ";
                sb.AppendLine($"  {marker} {TerminalColors.Success}+{file.LinesAdded}{AnsiStyleConstants.Reset} {TerminalColors.Error}-{file.LinesRemoved}{AnsiStyleConstants.Reset} {file.Path}");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Diff 文件列表渲染器 — CLI 简化版
/// </summary>
public sealed class DiffFileListRenderer
{
    public string Render(DiffData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}Changed Files{AnsiStyleConstants.Reset}");
        foreach (var file in data.Files)
        {
            sb.AppendLine($"  {TerminalColors.Success}+{file.LinesAdded}{AnsiStyleConstants.Reset} {TerminalColors.Error}-{file.LinesRemoved}{AnsiStyleConstants.Reset} {file.Path}");
        }
        return sb.ToString();
    }

    public int ComputeScrollOffset(int selectedIndex, int totalItems, int currentOffset)
    {
        return currentOffset;
    }
}

/// <summary>
/// Diff 视图渲染器 — CLI 简化版
/// </summary>
public sealed class DiffViewRenderer
{
    public void Render(string diffLines)
    {
        TerminalHelper.WriteLine(diffLines);
    }

    public static string Render(DiffData data, DiffViewMode mode)
    {
        var renderer = new DiffFileListRenderer();
        return renderer.Render(data);
    }
}

// ─── UsageBar ───

/// <summary>
/// 使用量进度条 — CLI 简化版
/// </summary>
public sealed class UsageBar
{
    private readonly double _percentage;
    private readonly int _width;
    private readonly string _filledColor;
    private readonly string _emptyColor;

    public UsageBar(double percentage, int width = 20, string? filledColor = null, string? emptyColor = null)
    {
        _percentage = percentage;
        _width = width;
        _filledColor = filledColor ?? TerminalColors.Primary;
        _emptyColor = emptyColor ?? TerminalColors.Muted;
    }

    public string Render()
    {
        var filled = (int)Math.Round(_percentage * _width);
        if (filled < 0) filled = 0;
        if (filled > _width) filled = _width;
        var bar = $"{_filledColor}{new string('█', filled)}{_emptyColor}{new string('░', _width - filled)}{AnsiStyleConstants.Reset}";
        return bar;
    }

    public static string Render(double percentage, int width = 20)
    {
        var filled = (int)Math.Round(percentage / 100 * width);
        if (filled < 0) filled = 0;
        if (filled > width) filled = width;
        var bar = new string('█', filled) + new string('░', width - filled);
        var color = percentage > 80 ? TerminalColors.Warning : TerminalColors.Primary;
        return $"{color}{bar}{AnsiStyleConstants.Reset} {percentage:F1}%";
    }
}

// ─── CompactSummaryData / CompactSummaryRenderer ───

/// <summary>
/// 压缩摘要数据
/// </summary>
public sealed class CompactSummaryData
{
    public int MessagesBefore { get; init; }
    public int MessagesAfter { get; init; }
    public int TokensSaved { get; init; }
    public int MessagesSummarized { get; init; }
    public CompactDirection Direction { get; init; }
    public int OriginalTokens { get; init; }
    public int CompressedTokens { get; init; }
}

/// <summary>
/// 压缩摘要渲染器 — CLI 简化版
/// </summary>
public sealed class CompactSummaryRenderer
{
    public string Render(CompactSummaryData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{TerminalColors.Primary}上下文已压缩{AnsiStyleConstants.Reset}");
        sb.AppendLine($"  消息: {data.MessagesSummarized} 条已摘要");
        sb.AppendLine($"  Token: {data.OriginalTokens:N0} → {data.CompressedTokens:N0} (节省 {data.OriginalTokens - data.CompressedTokens:N0})");
        return sb.ToString();
    }

    public static string RenderStatic(CompactSummaryData data)
    {
        return $"上下文已压缩: {data.MessagesBefore} → {data.MessagesAfter} 消息, 节省 {data.TokensSaved:N0} tokens";
    }
}

// ─── ModelPicker ───

/// <summary>
/// 模型选择器 — CLI 简化版
/// </summary>
public sealed class ModelPicker
{
    public string Render(ModelEntry[] models, int selectedIndex, string currentModelId, string providerName, EffortLevel effortLevel, bool isFastModeActive)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleConstants.Bold}模型选择 ({providerName}){AnsiStyleConstants.Reset}");
        sb.AppendLine();

        for (var i = 0; i < models.Length; i++)
        {
            var model = models[i];
            var marker = model.Id.Equals(currentModelId, StringComparison.OrdinalIgnoreCase) ? " *" : "";
            var selector = i == selectedIndex ? ">" : " ";
            sb.AppendLine($"  {selector} {model.DisplayName}{marker}");
        }

        sb.AppendLine();
        sb.AppendLine($"  Effort: {effortLevel.ToValue()} | Fast mode: {(isFastModeActive ? "ON" : "OFF")}");
        sb.AppendLine($"{TerminalColors.Muted}  ↑↓ 选择 | ←→ Effort | Enter 确认 | Esc 取消{AnsiStyleConstants.Reset}");

        return sb.ToString();
    }

    public static EffortLevel CycleEffort(EffortLevel current, bool forward)
    {
        var values = new[] { EffortLevel.Low, EffortLevel.Medium, EffortLevel.High, EffortLevel.Max };
        var idx = Array.IndexOf(values, current);
        if (idx < 0) idx = 1; // default to Medium

        if (forward)
        {
            idx = idx < values.Length - 1 ? idx + 1 : 0;
        }
        else
        {
            idx = idx > 0 ? idx - 1 : values.Length - 1;
        }

        return values[idx];
    }

    public static async Task<string?> ShowAsync(string currentModel, IModelCatalog catalog, string provider, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        var models = catalog.GetModelsForProvider(provider);
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleConstants.Bold}选择模型{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        for (var i = 0; i < models.Length; i++)
        {
            var marker = models[i].Id == currentModel ? " *" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} {models[i].DisplayName}{marker}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteRaw($"请选择 (1-{models.Length}, 回车保持当前): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return currentModel;

        try
        {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return currentModel;

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= models.Length)
            {
                return models[index - 1].Id;
            }

            return currentModel;
        }
        catch
        {
            return currentModel;
        }
    }
}

// ─── AnsiEscape ───

/// <summary>
/// ANSI 转义序列辅助 — CLI 简化版
/// </summary>
public static class AnsiEscape
{
    public static string CursorUp(int count) => count > 0 ? $"\x1b[{count}A" : "";
    public static string CursorDown(int count) => count > 0 ? $"\x1b[{count}B" : "";
    public static string CursorLeft(int count) => count > 0 ? $"\x1b[{count}D" : "";
    public static string CursorRight(int count) => count > 0 ? $"\x1b[{count}C" : "";
    public static string ClearScreenFromCursor => "\x1b[J";
    public static string ClearLine => "\x1b[2K";
}

// ─── TerminalCharts ───

/// <summary>
/// 终端图表 — CLI 简化版
/// </summary>
public static class TerminalCharts
{
    public static string ActivityHeatmap(IReadOnlyList<DailyActivity> activities, string? title = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine($"{AnsiStyleConstants.Bold}{title}{AnsiStyleConstants.Reset}");
            sb.AppendLine();
        }

        if (activities.Count == 0) return sb.ToString();

        var maxCount = activities.Max(a => a.MessageCount);
        if (maxCount == 0) maxCount = 1;

        var blocks = new[] { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        sb.Append("  ");
        foreach (var day in activities)
        {
            var idx = (int)Math.Round((double)day.MessageCount / maxCount * (blocks.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= blocks.Length) idx = blocks.Length - 1;
            sb.Append($"{TerminalColors.Primary}{blocks[idx]}{AnsiStyleConstants.Reset}");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    public static string FunFactoid(long totalTokens, int daysActive, double totalHours)
    {
        if (totalTokens < 1000) return "";
        var tokensInMillions = totalTokens / 1_000_000.0;
        if (tokensInMillions > 1)
        {
            return $"That's about {tokensInMillions:F1}M tokens — equivalent to reading ~{tokensInMillions * 0.5:F0} books!";
        }
        return "";
    }
}
