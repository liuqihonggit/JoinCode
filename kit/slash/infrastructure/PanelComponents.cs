namespace JoinCode.Cli;

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

