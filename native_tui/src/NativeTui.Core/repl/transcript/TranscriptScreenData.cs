namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 全屏渲染数据投影（S6 收敛 — ReplShell 每帧从 TranscriptController 投影）：
/// 冻结行窗口 + 滚动偏移 + 视口行数 + footer 数据（BuildFooter 产物）。
/// 纯数据 record — 渲染由 <see cref="TranscriptRenderer"/> 执行；ReplView.Transcript 挂载
/// 非 null 即切换全屏 transcript 渲染（PromptInput 卸载语义 — 基准 transcript 整屏模态）。
/// </summary>
/// <param name="Lines">冻结快照行（进入时拍摄 — 会话内不变）。</param>
/// <param name="ScrollOffset">滚动行偏移（0-based — 行窗口首行索引）。</param>
/// <param name="ViewportHeight">视口行数（终端行数扣除 footer 行）。</param>
/// <param name="Footer">transcript footer 数据（标题+提示 / 搜索栏+徽章）。</param>
public sealed record TranscriptScreenData(
    IReadOnlyList<string> Lines,
    int ScrollOffset,
    int ViewportHeight,
    TranscriptFooterState Footer)
{
    /// <summary>内容总行数（窗口裁剪依据）。</summary>
    public int LineCount => Lines.Count;
}
