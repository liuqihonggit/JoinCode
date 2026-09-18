namespace JoinCode.Cli;

// ─── Selector ───

/// <summary>
/// 交互式选择器 — CLI 简化版，使用数字选择替代 TUI 上下键
/// </summary>
public sealed class Selector<T>
{
    private readonly string _title;
    private readonly (T Item, string DisplayText, string SearchKey)[] _items;

    /// <summary>
    /// 构造选择器实例，使用预格式化的项元组数组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">项元组数组，每项包含原始对象、显示文本、搜索键</param>
    public Selector(string title, (T Item, string DisplayText, string SearchKey)[] items)
    {
        _title = title;
        _items = items;
    }

    /// <summary>
    /// 构造选择器实例，使用显示选择器和可选搜索选择器从原始项数组生成项元组
    /// </summary>
    /// <param name="title">选择器标题</param>
    /// <param name="items">原始项数组</param>
    /// <param name="displaySelector">将项转换为显示文本的委托</param>
    /// <param name="searchSelector">将项转换为搜索键的可选委托，缺省时使用显示文本</param>
    /// <param name="enableSearch">是否启用搜索功能（保留参数，CLI 简化版未实现）</param>
    public Selector(string title, T[] items, Func<T, string> displaySelector, Func<T, string>? searchSelector = null, bool enableSearch = false)
    {
        _title = title;
        _items = items.Select(i => (i, displaySelector(i), searchSelector?.Invoke(i) ?? displaySelector(i))).ToArray();
    }

    /// <summary>
    /// 异步显示选择器并等待用户输入
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>选择结果，包含选中项或取消标志</returns>
    public async Task<SelectorResult<T>> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteLineReal($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLineReal();

        for (var i = 0; i < _items.Length; i++)
        {
            TerminalHelper.WriteLineReal($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {_items[i].DisplayText}");
        }

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"请输入选择 (1-{_items.Length}, Esc 取消): ");

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
    /// <summary>
    /// 选中的项，取消时为默认值
    /// </summary>
    public T Selected { get; init; } = default!;

    /// <summary>
    /// 是否已取消选择
    /// </summary>
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

    /// <summary>
    /// 构造对话框实例
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="content">对话框正文内容</param>
    /// <param name="buttons">按钮标签数组</param>
    public Dialog(string title, string content, string[] buttons)
    {
        _title = title;
        _content = content;
        _buttons = buttons;
    }

    /// <summary>
    /// 异步显示对话框并等待用户选择按钮
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>对话框结果，包含是否取消和选中按钮索引</returns>
    public async Task<DialogResult> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(_content);
        TerminalHelper.NewLine();

        for (var i = 0; i < _buttons.Length; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {_buttons[i]}");
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

    /// <summary>
    /// 异步显示确认提示并等待用户输入 y/N
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户输入 y 返回 true，否则返回 false</returns>
    public static async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine($"{message} (y/N): ");
            return false;
        }

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"{message} (y/N): ");

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

    /// <summary>
    /// 异步显示输入提示并等待用户输入文本
    /// </summary>
    /// <param name="message">输入提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户输入的文本，取消或非交互环境返回 null</returns>
    public static async Task<string?> PromptAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"{message}: ");

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
    /// <summary>
    /// 是否已取消对话框
    /// </summary>
    public required bool Cancelled { get; init; }

    /// <summary>
    /// 选中按钮的从零开始的索引，取消时为 -1
    /// </summary>
    public required int SelectedIndex { get; init; }
}

// ─── Confirmation ───

/// <summary>
/// 确认对话框 — CLI 简化版
/// </summary>
public static class Confirmation
{
    /// <summary>
    /// 异步显示确认提示，委托给 Dialog.ConfirmAsync
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户确认返回 true，否则返回 false</returns>
    public static Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        return Dialog.ConfirmAsync(message, ct);
    }

    /// <summary>
    /// 异步显示确认提示，语义同 ConfirmAsync
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户确认返回 true，否则返回 false</returns>
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

    /// <summary>
    /// 构造 Tab 面板实例
    /// </summary>
    /// <param name="tabNames">Tab 名称数组</param>
    /// <param name="contentProvider">根据 Tab 索引返回该 Tab 内容的委托</param>
    public TabPanel(string[] tabNames, Func<int, string> contentProvider)
    {
        _tabNames = tabNames;
        _contentProvider = contentProvider;
    }

    /// <summary>
    /// 异步显示 Tab 面板，顺序输出每个 Tab 的名称和内容
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        for (var i = 0; i < _tabNames.Length; i++)
        {
            if (i > 0)
            {
                TerminalHelper.WriteLine();
                TerminalHelper.WriteLine($"{TerminalColors.Divider}{new string('─', 40)}{AnsiStyleEnumConstants.Reset}");
                TerminalHelper.WriteLine();
            }

            TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}[{_tabNames[i]}]{AnsiStyleEnumConstants.Reset}");
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

    /// <summary>
    /// 构造分页列表实例
    /// </summary>
    /// <param name="title">列表标题</param>
    /// <param name="items">项只读列表</param>
    /// <param name="displaySelector">将项转换为显示文本的委托</param>
    /// <param name="pageSize">每页显示项数，默认 20</param>
    public PaginatedList(string title, IReadOnlyList<T> items, Func<T, string> displaySelector, int pageSize = 20)
    {
        _title = title;
        _items = items;
        _displaySelector = displaySelector;
        _pageSize = pageSize;
    }

    /// <summary>
    /// 异步显示分页列表，超出每页大小的项以省略形式提示
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset} ({_items.Count} 项)");
        TerminalHelper.NewLine();

        var displayCount = Math.Min(_items.Count, _pageSize);
        for (var i = 0; i < displayCount; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {_displaySelector(_items[i])}");
        }

        if (_items.Count > _pageSize)
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"{TerminalColors.Muted}  ... 还有 {_items.Count - _pageSize} 项未显示{AnsiStyleEnumConstants.Reset}");
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

    /// <summary>
    /// 构造步骤流程实例，不带标题
    /// </summary>
    /// <param name="steps">步骤数组</param>
    public StepFlow(Step[] steps)
    {
        _steps = steps;
    }

    /// <summary>
    /// 构造步骤流程实例，带标题
    /// </summary>
    /// <param name="title">流程标题</param>
    /// <param name="steps">步骤数组</param>
    public StepFlow(string title, Step[] steps)
    {
        _title = title;
        _steps = steps;
    }

    /// <summary>
    /// 异步显示并执行所有步骤，依次输出标题与描述并执行动作
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已执行的步骤总数</returns>
    public async Task<int> ShowAsync(CancellationToken ct = default)
    {
        if (_title is not null)
        {
            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.NewLine();
        }

        for (var i = 0; i < _steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = _steps[i];
            TerminalHelper.WriteLine($"{TerminalColors.Primary}{AnsiStyleEnumConstants.Bold}步骤 {i + 1}/{_steps.Length}{AnsiStyleEnumConstants.Reset}: {step.Title}");

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

    /// <summary>
    /// 异步运行整个步骤流程，等价于 ShowAsync 但不返回步骤数
    /// </summary>
    /// <param name="ct">取消令牌</param>
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
    /// <summary>
    /// 步骤标题
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// 步骤描述，可选
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 步骤执行动作的异步委托，可选
    /// </summary>
    public Func<CancellationToken, Task>? Action { get; init; }

    /// <summary>
    /// 构造步骤实例
    /// </summary>
    /// <param name="title">步骤标题</param>
    /// <param name="description">步骤描述，可选</param>
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
    /// <summary>
    /// 显示供应商选择列表并等待用户输入
    /// </summary>
    /// <param name="defaultProvider">默认供应商名称，回车时使用</param>
    /// <param name="title">选择标题</param>
    /// <param name="hint">提示文本</param>
    /// <param name="registry">供应商定义注册表</param>
    /// <returns>选中的供应商名称，失败时返回默认供应商</returns>
    public static string? Show(string defaultProvider, string title, string hint, IProviderDefinitionRegistry registry)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{title}{AnsiStyleEnumConstants.Reset}");
        if (!string.IsNullOrEmpty(hint))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{hint}{AnsiStyleEnumConstants.Reset}");
        }
        TerminalHelper.NewLine();

        var providers = registry.RegisteredProviders
            .Select(p => registry.TryGet(p))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        for (var i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            var marker = p.ProviderName == defaultProvider ? " (默认)" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {p.DisplayName}{marker}");
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
    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 已使用的总 Token 数
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// 模型上下文窗口最大 Token 数
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// 上下文类别名称
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 上下文项名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 该上下文项的 Token 数
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// 占最大 Token 数的百分比
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// 上下文类别列表
    /// </summary>
    public List<ContextCategory> Categories { get; set; } = [];
}

/// <summary>
/// 上下文类别
/// </summary>
public sealed class ContextCategory
{
    /// <summary>
    /// 类别名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 该类别的 Token 数
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// 排序序号，越小越靠前
    /// </summary>
    public int SortOrder { get; }

    /// <summary>
    /// 构造上下文类别实例
    /// </summary>
    /// <param name="name">类别名称</param>
    /// <param name="tokenCount">该类别的 Token 数</param>
    /// <param name="sortOrder">排序序号，默认 0</param>
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
    /// <summary>
    /// 渲染单个上下文数据为带类别条形图的文本
    /// </summary>
    /// <param name="data">上下文数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(ContextData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Context Window{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine($"  Model: {data.Model}");
        sb.AppendLine($"  Tokens: {data.TotalTokens:N0} / {data.MaxTokens:N0}");

        if (data.Categories.Count > 0)
        {
            sb.AppendLine();
            foreach (var cat in data.Categories.OrderBy(c => c.SortOrder))
            {
                var percentage = data.MaxTokens > 0 ? (double)cat.TokenCount / data.MaxTokens * 100 : 0;
                var bar = new string('█', (int)Math.Max(1, percentage / 5));
                sb.AppendLine($"  {TerminalColors.Primary}{cat.Name,-12}{AnsiStyleEnumConstants.Reset} {bar} {cat.TokenCount:N0} ({percentage:F1}%)");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 渲染多个上下文数据为按 Token 数降序排列的条形图文本
    /// </summary>
    /// <param name="data">上下文数据只读列表</param>
    /// <returns>渲染后的文本</returns>
    public static string Render(IReadOnlyList<ContextData> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Context Window{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();

        foreach (var d in data.OrderByDescending(d => d.TokenCount))
        {
            var bar = new string('█', (int)Math.Max(1, d.Percentage / 5));
            sb.AppendLine($"  {TerminalColors.Primary}{d.Category,-12}{AnsiStyleEnumConstants.Reset} {bar} {d.TokenCount:N0} ({d.Percentage:F1}%)");
        }

        return sb.ToString();
    }
}


// ─── BridgeConnectionState / BridgeStatusIndicator ───

/// <summary>
/// Bridge 连接状态
/// </summary>
public enum BridgeConnectionState
{
    /// <summary>
    /// 空闲状态
    /// </summary>
    [EnumValue("idle")]
    Idle,

    /// <summary>
    /// 未连接
    /// </summary>
    [EnumValue("disconnected")]
    Disconnected,

    /// <summary>
    /// 连接中
    /// </summary>
    [EnumValue("connecting")]
    Connecting,

    /// <summary>
    /// 已连接
    /// </summary>
    [EnumValue("connected")]
    Connected,

    /// <summary>
    /// 错误状态
    /// </summary>
    [EnumValue("error")]
    Error
}

/// <summary>
/// Bridge 状态指示器 — CLI 简化版
/// </summary>
public static class BridgeStatusIndicator
{
    /// <summary>
    /// 渲染连接状态为带颜色的状态文本
    /// </summary>
    /// <param name="state">连接状态</param>
    /// <returns>带 ANSI 颜色的状态文本</returns>
    public static string Render(BridgeConnectionState state) => GetStatusText(state);

    /// <summary>
    /// 获取连接状态对应的文本描述
    /// </summary>
    /// <param name="state">连接状态</param>
    /// <returns>带 ANSI 颜色的状态文本</returns>
    public static string GetStatusText(BridgeConnectionState state) => state switch
    {
        BridgeConnectionState.Connected => $"{TerminalColors.Success}● 已连接{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Connecting => $"{TerminalColors.Warning}● 连接中...{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Disconnected => $"{TerminalColors.Muted}○ 未连接{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Error => $"{TerminalColors.Error}● 错误{AnsiStyleEnumConstants.Reset}",
        _ => $"{TerminalColors.Muted}○ 未知{AnsiStyleEnumConstants.Reset}"
    };
}

// ─── Diff 相关 ───

/// <summary>
/// Diff 视图模式
/// </summary>
public enum DiffViewMode
{
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
public abstract class DiffSource
{
    /// <summary>
    /// 当前工作区 Diff 来源
    /// </summary>
    public sealed class Current : DiffSource { }

    /// <summary>
    /// 指定历史轮次的 Diff 来源
    /// </summary>
    public sealed class Turn : DiffSource
    {
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
public sealed class GitDiffService
{
    private readonly IFileSystem _fs;
    private readonly IGitCommandRunner? _gitRunner;

    /// <summary>
    /// 构造 Git Diff 服务实例
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="gitRunner">Git 命令执行器，可选，为 null 时返回空 Diff</param>
    public GitDiffService(IFileSystem fs, IGitCommandRunner? gitRunner = null)
    {
        _fs = fs;
        _gitRunner = gitRunner;
    }

    /// <summary>
    /// 异步获取当前工作区的 Git diff 数据
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>解析后的 Diff 数据，失败时返回空 Diff</returns>
    public async Task<DiffData> FetchDiffDataAsync(CancellationToken ct = default)
    {
        try
        {
            if (_gitRunner is null)
                return new DiffData(null, [], [], false);

            var result = await _gitRunner.ExecuteAsync("diff --stat", _fs.GetCurrentDirectory(), ct).ConfigureAwait(false);
            return ParseDiffStatOutput(result.Output);
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
    /// <summary>
    /// 渲染 Diff 对话框状态为文本，包含统计信息和文件列表
    /// </summary>
    /// <param name="state">Diff 对话框状态</param>
    /// <returns>渲染后的文本</returns>
    public string Render(DiffDialogState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Diff{AnsiStyleEnumConstants.Reset} ({state.ViewMode})");

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
                sb.AppendLine($"  {marker} {TerminalColors.Success}+{file.LinesAdded}{AnsiStyleEnumConstants.Reset} {TerminalColors.Error}-{file.LinesRemoved}{AnsiStyleEnumConstants.Reset} {file.Path}");
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
    /// <summary>
    /// 渲染变更文件列表为带增删行数标记的文本
    /// </summary>
    /// <param name="data">Diff 数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(DiffData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Changed Files{AnsiStyleEnumConstants.Reset}");
        foreach (var file in data.Files)
        {
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
    /// <summary>
    /// 将 Diff 行文本直接输出到终端
    /// </summary>
    /// <param name="diffLines">Diff 行文本</param>
    public void Render(string diffLines)
    {
        TerminalHelper.WriteLine(diffLines);
    }

    /// <summary>
    /// 根据 Diff 数据和视图模式渲染文件列表文本
    /// </summary>
    /// <param name="data">Diff 数据</param>
    /// <param name="mode">Diff 视图模式</param>
    /// <returns>渲染后的文本</returns>
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

    /// <summary>
    /// 构造使用量进度条实例
    /// </summary>
    /// <param name="percentage">百分比，0 到 1</param>
    /// <param name="width">进度条宽度，默认 20</param>
    /// <param name="filledColor">已填充部分颜色，可选，默认使用主色</param>
    /// <param name="emptyColor">未填充部分颜色，可选，默认使用弱化色</param>
    public UsageBar(double percentage, int width = 20, string? filledColor = null, string? emptyColor = null)
    {
        _percentage = percentage;
        _width = width;
        _filledColor = filledColor ?? TerminalColors.Primary;
        _emptyColor = emptyColor ?? TerminalColors.Muted;
    }

    /// <summary>
    /// 渲染进度条为带颜色的文本
    /// </summary>
    /// <returns>带 ANSI 颜色的进度条文本</returns>
    public string Render()
    {
        var filled = (int)Math.Round(_percentage * _width);
        if (filled < 0) filled = 0;
        if (filled > _width) filled = _width;
        var bar = $"{_filledColor}{new string('█', filled)}{_emptyColor}{new string('░', _width - filled)}{AnsiStyleEnumConstants.Reset}";
        return bar;
    }

    /// <summary>
    /// 静态渲染进度条，百分比超过 80 时使用警告色
    /// </summary>
    /// <param name="percentage">百分比，0 到 100</param>
    /// <param name="width">进度条宽度，默认 20</param>
    /// <returns>带 ANSI 颜色和百分比的进度条文本</returns>
    public static string Render(double percentage, int width = 20)
    {
        var filled = (int)Math.Round(percentage / 100 * width);
        if (filled < 0) filled = 0;
        if (filled > width) filled = width;
        var bar = new string('█', filled) + new string('░', width - filled);
        var color = percentage > 80 ? TerminalColors.Warning : TerminalColors.Primary;
        return $"{color}{bar}{AnsiStyleEnumConstants.Reset} {percentage:F1}%";
    }
}

// ─── CompactSummaryData / CompactSummaryRenderer ───

/// <summary>
/// 压缩摘要数据
/// </summary>
public sealed class CompactSummaryData
{
    /// <summary>
    /// 压缩前消息数
    /// </summary>
    public int MessagesBefore { get; init; }

    /// <summary>
    /// 压缩后消息数
    /// </summary>
    public int MessagesAfter { get; init; }

    /// <summary>
    /// 节省的 Token 数
    /// </summary>
    public int TokensSaved { get; init; }

    /// <summary>
    /// 已摘要的消息数
    /// </summary>
    public int MessagesSummarized { get; init; }

    /// <summary>
    /// 压缩方向
    /// </summary>
    public CompactDirection Direction { get; init; }

    /// <summary>
    /// 原始 Token 数
    /// </summary>
    public int OriginalTokens { get; init; }

    /// <summary>
    /// 压缩后 Token 数
    /// </summary>
    public int CompressedTokens { get; init; }
}

/// <summary>
/// 压缩摘要渲染器 — CLI 简化版
/// </summary>
public sealed class CompactSummaryRenderer
{
    /// <summary>
    /// 渲染压缩摘要数据为多行文本
    /// </summary>
    /// <param name="data">压缩摘要数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(CompactSummaryData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{TerminalColors.Primary}上下文已压缩{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine($"  消息: {data.MessagesSummarized} 条已摘要");
        sb.AppendLine($"  Token: {data.OriginalTokens:N0} → {data.CompressedTokens:N0} (节省 {data.OriginalTokens - data.CompressedTokens:N0})");
        return sb.ToString();
    }

    /// <summary>
    /// 静态渲染压缩摘要为单行简短文本
    /// </summary>
    /// <param name="data">压缩摘要数据</param>
    /// <returns>渲染后的单行文本</returns>
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
    /// <summary>
    /// 渲染模型选择列表为带选中标记和操作提示的文本
    /// </summary>
    /// <param name="models">可选模型数组</param>
    /// <param name="selectedIndex">当前选中索引</param>
    /// <param name="currentModelId">当前模型 ID</param>
    /// <param name="providerName">供应商名称</param>
    /// <param name="effortLevel">推理努力级别</param>
    /// <param name="isFastModeActive">是否启用快速模式</param>
    /// <returns>渲染后的文本</returns>
    public string Render(ModelEntry[] models, int selectedIndex, string currentModelId, string providerName, EffortLevel effortLevel, bool isFastModeActive)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}模型选择 ({providerName}){AnsiStyleEnumConstants.Reset}");
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
        sb.AppendLine($"{TerminalColors.Muted}  ↑↓ 选择 | ←→ Effort | Enter 确认 | Esc 取消{AnsiStyleEnumConstants.Reset}");

        return sb.ToString();
    }

    /// <summary>
    /// 在 Low、Medium、High、Max 之间循环切换推理努力级别
    /// </summary>
    /// <param name="current">当前努力级别</param>
    /// <param name="forward">true 向前循环，false 向后循环</param>
    /// <returns>切换后的努力级别</returns>
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

    /// <summary>
    /// 异步显示模型选择列表并等待用户输入
    /// </summary>
    /// <param name="currentModel">当前模型 ID</param>
    /// <param name="catalog">模型目录</param>
    /// <param name="provider">供应商名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>选中的模型 ID，失败时返回当前模型</returns>
    public static async Task<string?> ShowAsync(string currentModel, IModelCatalog catalog, string provider, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        var models = catalog.GetModelsForProvider(provider);
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}选择模型{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.NewLine();

        for (var i = 0; i < models.Length; i++)
        {
            var marker = models[i].Id == currentModel ? " *" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {models[i].DisplayName}{marker}");
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

// ─── TerminalCharts ───

/// <summary>
/// 终端图表 — CLI 简化版
/// </summary>
public static class TerminalCharts
{
    /// <summary>
    /// 渲染每日活动热力图为竖直方块条形图
    /// </summary>
    /// <param name="activities">每日活动只读列表</param>
    /// <param name="title">可选标题</param>
    /// <returns>渲染后的文本，无活动时返回空文本或仅标题</returns>
    public static string ActivityHeatmap(IReadOnlyList<DailyActivity> activities, string? title = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine($"{AnsiStyleEnumConstants.Bold}{title}{AnsiStyleEnumConstants.Reset}");
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
            sb.Append($"{TerminalColors.Primary}{blocks[idx]}{AnsiStyleEnumConstants.Reset}");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// 根据总 Token 数生成趣味事实文本，Token 不足百万时返回空字符串
    /// </summary>
    /// <param name="totalTokens">总 Token 数</param>
    /// <param name="daysActive">活跃天数</param>
    /// <param name="totalHours">总小时数</param>
    /// <returns>趣味事实文本，不满足条件时返回空字符串</returns>
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
