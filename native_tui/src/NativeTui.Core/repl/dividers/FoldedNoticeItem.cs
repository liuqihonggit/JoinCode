namespace NativeTui.Core.Repl;

/// <summary>
/// 更早消息折叠提示项（消息列表域）— ReplView 渲染项数上限（200）触发头部裁剪时的指示行：
/// `… 更早消息已折叠` dim 单行。被裁内容已自然滚入终端 scrollback 可回看（基准 CC-941 注释语义 —
/// 非虚拟化渲染内存守卫）；本项目 inline 全量屏无 React 虚拟化，物理裁剪 + 指示行为对等机制。
/// 高度 1（贴顶指示行，无 margin — 头部紧凑）。
/// </summary>
public sealed class FoldedNoticeItem : IScrollItem
{
    /// <summary>内容高度 = 1（无 margin — 头部紧凑指示行）。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>渲染：整行 dim `… 更早消息已折叠`。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, "… 更早消息已折叠", dim, frame.Right);
    }
}
