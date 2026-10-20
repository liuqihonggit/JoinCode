namespace NativeTui.Core.Repl;

/// <summary>模型选择项 DTO（M7-6）— 宿主从引擎 IModelCatalog 投影（后端获取），Core 零依赖。</summary>
/// <param name="Id">模型标识（切换时回传宿主执行）。</param>
/// <param name="DisplayName">显示名称（列表行）。</param>
/// <param name="Description">能力描述（名称行下方缩进行；空则折叠）。</param>
/// <param name="IsCurrent">是否当前在用模型（列表行尾 ✓ 标记 + Begin 焦点定位）。</param>
public sealed record ModelPickerItem(string Id, string DisplayName, string Description, bool IsCurrent);

/// <summary>
/// 模型选择器协调器（M7-6，全部自研）— 对齐基准 ModelPicker 交互语义：
/// Begin 焦点定位当前模型（无则 0）、上下 clamp 不环绕（列表语义，区别于权限三档环绕）、
/// 可见窗口 min(10, n) + 焦点跟随滚动（对齐基准 visibleOptionCount=Math.min(10, n)）。
/// 状态机同权限对话框（Begin 建立 / Complete 清空幂等）；结果经回调传递（无引擎阻塞等待方，
/// 不需要 TCS 桥）。消费方：ReplShell 模态改道（↑↓/Enter/Esc）+ ModelPickerDialog 渲染投影。
/// </summary>
public sealed class ModelPickerCoordinator
{
    /// <summary>最大可见条数（基准 visibleOptionCount=Math.min(10, n)）。</summary>
    public const int MaxVisible = 10;

    private IReadOnlyList<ModelPickerItem> _items = [];

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

    /// <summary>Begin 挂起 — 清单就位 + 焦点定位当前模型（无当前则 0）+ 窗口归零。</summary>
    public void Begin(IReadOnlyList<ModelPickerItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("模型清单不能为空（宿主保证 — IModelCatalog.GetModelsForProvider + EnsureCurrentModelInList）。", nameof(items));
        }

        _items = items;
        FocusIndex = IndexOfCurrent(items);
        WindowStart = 0;
    }

    /// <summary>焦点移动 — clamp 不环绕（列表语义）+ 滚动窗口跟随。</summary>
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
    }

    /// <summary>Complete 清空状态（幂等 — 二次调用 no-op）。</summary>
    public void Complete()
    {
        _items = [];
        FocusIndex = 0;
        WindowStart = 0;
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
