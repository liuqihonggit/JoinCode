namespace NativeTui.Core.Repl;

/// <summary>
/// 快捷键帮助面板（S6 任务 5 — 空闲态 '?' 弹出，对齐基准 PromptInputHelpMenu 核心键位清单汉化）：
/// 标题行 + 键位行序列（键名 + 两空格 + 中文描述，整行 dim — 基准 help menu 无边框 dim 文本行形态）；
/// 渲染位置 = 输入框上方（基准 PromptInput 上方同位）；Esc/'?' 关闭（ReplShell 分派）。
/// 纯静态清单 — 键位与 ReplShell 分派表同步维护（本项目实际存在键位，基准未实现键位不列）。
/// </summary>
public sealed class HelpPanel
{
    /// <summary>键位清单（标题 + 键位行 — 高度与渲染数据源）。</summary>
    public static readonly string[] Lines =
    [
        "快捷键",
        "?  本面板 · esc 关闭",
        "enter  发送 · tab  命令补全",
        "shift+enter  输入换行（多行编辑 · ctrl+_ 撤销）",
        "↑ ↓  翻阅输入历史 · ctrl+r  搜索历史",
        "shift+↑  消息导航 · enter  展开分组 · c  复制全文",
        "ctrl+o  详细转录 · ctrl+l  刷新界面",
        "shift+tab  切换权限模式",
        "ctrl+c  复制最后回复",
        "ctrl+d  退出（再按一次确认）",
        "esc  打断 · 空输入时清空",
    ];

    /// <summary>面板行数（高度 — ReplView 布局链消费；派生自 Lines 唯一数据源）。</summary>
    public static int Height => Lines.Length;

    /// <summary>渲染帮助面板（frame 高度须 ≥ Height — 逐行 dim 文本，右缘裁剪走 rightBound）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        for (var i = 0; i < Lines.Length && i < frame.Height; i++)
        {
            screen.WriteText(frame.Y + i, frame.X + 2, Lines[i], dim, frame.Right);
        }
    }
}
