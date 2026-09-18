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

