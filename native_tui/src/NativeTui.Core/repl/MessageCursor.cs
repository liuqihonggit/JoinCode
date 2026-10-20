namespace NativeTui.Core.Repl;

/// <summary>
/// 可被消息光标导航的滚动项标记（交互层 Step1）— 消息类消息实现（用户/助手/工具调用/工具结果/
/// diff 结果/错误/Markdown/子代理分组）；banner、spinner、权限/模型对话框等装饰项不参与导航。
/// </summary>
public interface ICursorNavigable
{
}

/// <summary>
/// 消息光标导航协调器（交互层 Step1）— 对齐基准消息导航系统（MESSAGE_ACTIONS feature flag 上下文）：
/// Shift+↑ 进入（Begin 定位最后一条用户消息，基准 enterCursor scan；无用户消息回退最后一条可导航）、
/// ↑/↓/k/j 相邻可导航项逐条移动（跳过非导航装饰项，clamp 不环绕）、Shift+↑/↓ 跳相邻用户消息、
/// Ctrl+↑/↓ 首末可导航项、Esc 退出。选中索引为滚动区全局索引（高亮覆写与分组展开直接定位项实例）；
/// 消息动态增删安全 — 每次操作传入当前项列表（协调器不持有列表引用）。
/// 状态机同 ModelPickerCoordinator 先例（Begin 建立 / Exit 清空幂等）。
/// 消费方：ReplShell 第四交互态模态路由（HandleCursorNavKey）+ ScrollBox 高亮投影。
/// </summary>
public sealed class MessageCursor
{
    /// <summary>光标导航激活态（ReplShell 第四交互态路由依据）。</summary>
    public bool IsCursorActive { get; private set; }

    /// <summary>选中项在滚动区的全局索引（-1 = 无选中）。</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Begin 进入 — 定位最后一条用户消息（基准 enterCursor：向回扫描最后一条 user 角色）；
    /// 无用户消息回退最后一条可导航消息；无可导航消息不激活（banner+spinner 空会话）。</summary>
    public void Begin(IReadOnlyList<IScrollItem> items)
    {
        var lastUser = -1;
        var lastNavigable = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not ICursorNavigable)
            {
                continue;
            }

            lastNavigable = i;
            if (items[i] is UserMessageItem)
            {
                lastUser = i;
            }
        }

        var target = lastUser >= 0 ? lastUser : lastNavigable;
        IsCursorActive = target >= 0;
        SelectedIndex = target;
    }

    /// <summary>相邻可导航项移动 — 向 delta 方向扫描最近可导航项（跳过 banner/spinner 装饰项）；
    /// 方向上无可导航项保持不动（clamp 不环绕，对齐基准列表语义）。</summary>
    public void Move(IReadOnlyList<IScrollItem> items, int delta)
    {
        if (!IsCursorActive)
        {
            return;
        }

        var step = Math.Sign(delta);
        if (step == 0)
        {
            return;
        }

        var target = SelectedIndex;
        for (var i = SelectedIndex + step; i >= 0 && i < items.Count; i += step)
        {
            if (items[i] is ICursorNavigable)
            {
                target = i;
                break;
            }
        }

        SelectedIndex = target;
    }

    /// <summary>跳相邻用户消息（Shift+↑/↓ — 基准消息光标跳转 user 角色消息）；
    /// 指定方向无用户消息保持不动。</summary>
    public void MoveToUserMessage(IReadOnlyList<IScrollItem> items, int direction)
    {
        if (!IsCursorActive)
        {
            return;
        }

        var step = Math.Sign(direction);
        if (step == 0)
        {
            return;
        }

        for (var i = SelectedIndex + step; i >= 0 && i < items.Count; i += step)
        {
            if (items[i] is UserMessageItem)
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>跳首/末可导航消息（Ctrl+↑/↓）；无可导航项保持不动（动态删空防御）。</summary>
    public void MoveToEdge(IReadOnlyList<IScrollItem> items, bool first)
    {
        if (!IsCursorActive)
        {
            return;
        }

        var target = first ? FirstNavigable(items) : LastNavigable(items);
        if (target >= 0)
        {
            SelectedIndex = target;
        }
    }

    /// <summary>Exit 退出 — 清空选中（幂等 — 二次调用 no-op）。</summary>
    public void Exit()
    {
        IsCursorActive = false;
        SelectedIndex = -1;
    }

    /// <summary>首个可导航项索引（-1 = 无）。</summary>
    private static int FirstNavigable(IReadOnlyList<IScrollItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is ICursorNavigable)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>末个可导航项索引（-1 = 无）。</summary>
    private static int LastNavigable(IReadOnlyList<IScrollItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is ICursorNavigable)
            {
                return i;
            }
        }

        return -1;
    }
}
