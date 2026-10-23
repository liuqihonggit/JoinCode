namespace NativeTui.Core.Repl;

/// <summary>模型选择项 DTO（M7-6）— 宿主从引擎 IModelCatalog 投影（后端获取），Core 零依赖。</summary>
/// <param name="Id">模型标识（切换时回传宿主执行）。</param>
/// <param name="DisplayName">显示名称（列表行）。</param>
/// <param name="Description">能力描述（名称行下方缩进行；空则折叠）。</param>
/// <param name="IsCurrent">是否当前在用模型（列表行尾 ✓ 标记 + Begin 焦点定位）。</param>
/// <param name="SupportsEffort">是否支持 effort（TASK066 D5 — 宿主从引擎 IModelCatalog.SupportsEffort 投影；
/// false 时焦点停在本项 effort 循环 no-op + effort 区渲染 not supported；默认 false 兼容 vendor 清单）。</param>
/// <param name="SupportsMaxEffort">是否支持 max 档（引擎 IModelCatalog.SupportsMaxEffort 投影 — 决定循环环四值/三值）。</param>
/// <param name="DefaultEffort">模型默认档位（基准 getDefaultEffortLevelForOption — 引擎无模型级默认数据源，
/// 统一 High 兜底，对齐基准 'high' 回退）。</param>
public sealed record ModelPickerItem(string Id, string DisplayName, string Description, bool IsCurrent,
    bool SupportsEffort = false, bool SupportsMaxEffort = false, PickerEffortLevel DefaultEffort = PickerEffortLevel.High);

/// <summary>
/// 模型选择器 effort 配置（TASK066 D5 — Begin 注入；null = 不启用 effort 区，vendor 选择器形态）。
/// InitialEffort = 会话当前档位（宿主从引擎 IExecutionSettingsProvider.EffortLevel 投影，
/// 引擎 Auto → null = 未设置，展示/循环用焦点项默认档 — 基准 effortValue undefined 语义）。
/// </summary>
public sealed record PickerEffortConfig(PickerEffortLevel? InitialEffort);

/// <summary>
/// 模型选择器协调器（M7-6，全部自研）— 对齐基准 ModelPicker 交互语义：
/// Begin 焦点定位当前模型（无则 0）、上下 clamp 不环绕（列表语义，区别于权限三档环绕）、
/// 可见窗口 min(10, n) + 焦点跟随滚动（对齐基准 visibleOptionCount=Math.min(10, n)）。
/// effort 态（TASK066 D5）：left/right 环绕循环（基准 cycleEffortLevel 四值/三值环 + 越界 clamp high）、
/// 焦点移动默认档跟随（基准 handleFocus — 未切换且初始未设置时取焦点项默认档）、
/// 确认传出仅切换过且焦点项支持（基准 onSelect 第二参数 selectedEffort）。
/// 状态机同权限对话框（Begin 建立 / Complete 清空幂等）；结果经回调传递（无引擎阻塞等待方，
/// 不需要 TCS 桥）。消费方：ReplShell 模态改道（↑↓/←→/Enter/Esc）+ ModelPickerDialog 渲染投影。
/// </summary>
public sealed class ModelPickerCoordinator
{
    /// <summary>最大可见条数（基准 visibleOptionCount=Math.min(10, n)）。</summary>
    public const int MaxVisible = 10;

    private IReadOnlyList<ModelPickerItem> _items = [];

    /// <summary>effort 配置（null = 未启用 — vendor 选择器形态）。</summary>
    private PickerEffortConfig? _effortConfig;

    /// <summary>effort 内部档位（null = 未设置 — 展示用焦点项默认档兜底）。</summary>
    private PickerEffortLevel? _effort;

    /// <summary>是否切换过 effort（基准 hasToggledEffort — 决定确认时是否传出档位）。</summary>
    private bool _hasToggledEffort;

    /// <summary>当前清单（挂起期间有效）。</summary>
    public IReadOnlyList<ModelPickerItem> Items => _items;

    /// <summary>焦点索引（clamp 于 [0, count-1]）。</summary>
    public int FocusIndex { get; private set; }

    /// <summary>是否挂起中（模态路由依据）。</summary>
    public bool IsPending => _items.Count > 0;

    /// <summary>焦点项（未挂起为 null）。</summary>
    public ModelPickerItem? Focused => IsPending && FocusIndex < _items.Count ? _items[FocusIndex] : null;

    /// <summary>可见条数 = min(10, n)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _items.Count);

    /// <summary>折叠条数 = max(0, n - 10)（"and N more…" 行依据）。</summary>
    public int HiddenCount => Math.Max(0, _items.Count - VisibleCount);

    /// <summary>滚动窗口起点（0 起，保证焦点可见的最小窗口位移）。</summary>
    public int WindowStart { get; private set; }

    /// <summary>effort 区是否启用（Begin 注入配置 — vendor 选择器为 false）。</summary>
    public bool EffortEnabled => _effortConfig is not null;

    /// <summary>effort 内部档位（null = 未设置 — 基准 effort state undefined 语义）。</summary>
    public PickerEffortLevel? Effort => _effort;

    /// <summary>是否切换过 effort（确认传出判定 — 基准 hasToggledEffort）。</summary>
    public bool HasToggledEffort => _hasToggledEffort;

    /// <summary>effort 展示档位（基准 displayEffort ModelPicker.tsx:170）：
    /// 未设置 → 焦点项默认档兜底；max 且焦点项不支持 max → 降级 high（渲染投影，不改内部态）。</summary>
    public PickerEffortLevel DisplayEffort
    {
        get
        {
            var level = _effort ?? Focused?.DefaultEffort ?? PickerEffortLevel.High;
            return level == PickerEffortLevel.Max && Focused is not { SupportsMaxEffort: true }
                ? PickerEffortLevel.High
                : level;
        }
    }

    /// <summary>确认时传出的 effort 档位（基准 ModelPicker.tsx:244 selectedEffort）：
    /// 仅切换过且焦点项支持 effort 时非 null（未切换 / 焦点不支持 → null = 不应用）。</summary>
    public PickerEffortLevel? SelectedEffort => _hasToggledEffort && Focused is { SupportsEffort: true } ? _effort : null;

    /// <summary>Begin 挂起（vendor 兼容重载 — 不启用 effort 区）。</summary>
    public void Begin(IReadOnlyList<ModelPickerItem> items) => Begin(items, null);

    /// <summary>Begin 挂起 — 清单就位 + 焦点定位当前模型（无当前则 0）+ 窗口归零 +
    /// effort 配置/初始档位就位（未切换标记复位）。</summary>
    public void Begin(IReadOnlyList<ModelPickerItem> items, PickerEffortConfig? effortConfig)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("模型清单不能为空（宿主保证 — IModelCatalog.GetModelsForProvider + EnsureCurrentModelInList）。", nameof(items));
        }

        _items = items;
        FocusIndex = IndexOfCurrent(items);
        WindowStart = 0;
        _effortConfig = effortConfig;
        _effort = effortConfig?.InitialEffort;
        _hasToggledEffort = false;
    }

    /// <summary>焦点移动 — clamp 不环绕（列表语义）+ 滚动窗口跟随 +
    /// 未切换且初始未设置时 effort 默认档跟随焦点项（基准 handleFocus ModelPicker.tsx:173-178）。</summary>
    public void MoveFocus(int delta)
    {
        if (!IsPending)
        {
            return;
        }

        FocusIndex = Math.Clamp(FocusIndex + delta, 0, _items.Count - 1);
        if (FocusIndex < WindowStart)
        {
            WindowStart = FocusIndex;
        }
        else if (FocusIndex >= WindowStart + VisibleCount)
        {
            WindowStart = FocusIndex - VisibleCount + 1;
        }

        if (!_hasToggledEffort && _effortConfig?.InitialEffort is null)
        {
            _effort = Focused?.DefaultEffort;
        }
    }

    /// <summary>effort 环绕循环（基准 handleCycleEffort + cycleEffortLevel ModelPicker.tsx:187-194/431-442）：
    /// 未启用 / 无焦点项 / 焦点项不支持 effort → no-op 返回 false；
    /// 环 = 焦点项支持 max ? 四值 : 三值，起点 = 当前档 ?? 焦点项默认档，越界档 clamp 到 high 索引；
    /// 成功后置切换标记（确认传出依据）。返回是否生效。</summary>
    public bool CycleEffort(bool forward)
    {
        if (_effortConfig is null || Focused is not { SupportsEffort: true } focused)
        {
            return false;
        }

        _effort = (_effort ?? focused.DefaultEffort).Cycle(forward, focused.SupportsMaxEffort);
        _hasToggledEffort = true;
        return true;
    }

    /// <summary>Complete 清空状态（幂等 — 二次调用 no-op）。</summary>
    public void Complete()
    {
        _items = [];
        FocusIndex = 0;
        WindowStart = 0;
        _effortConfig = null;
        _effort = null;
        _hasToggledEffort = false;
    }

    /// <summary>定位当前模型索引（无标记则 0）。</summary>
    private static int IndexOfCurrent(IReadOnlyList<ModelPickerItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsCurrent)
            {
                return i;
            }
        }

        return 0;
    }
}
