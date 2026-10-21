namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 全屏渲染器（S6 收敛）— 消费 <see cref="TranscriptScreenData"/> 渲染整屏模态：
/// 行窗口 [ScrollOffset, ScrollOffset+ViewportHeight) 自屏顶逐行写入（超宽由 WriteText
/// rightBound 裁剪），屏底 1 行 transcript footer（paddingX=2 对齐 FooterBar 组装先例）：
/// 搜索栏打开 = `/` + 查询 + 右对齐 `N/M` 徽章（TranscriptSearchBar 形态）；
/// 查询激活（栏关）= 标题+提示 · n/N 跳转 + 右对齐徽章；
/// 默认 = dim 标题+提示（TranscriptModeFooter 汉化文案）。
/// 纯静态渲染 — 状态唯一来源是 TranscriptController（经 ReplShell 每帧投影）。
/// </summary>
public static class TranscriptRenderer
{
    /// <summary>footer 行行数（视口行数扣减依据 — 屏底恒 1 行）。</summary>
    public const int FooterRows = 1;

    /// <summary>footer 内容左起列（paddingX=2 — 对齐 FooterBar 组装层 frame.X=2 先例）。</summary>
    private const int FooterPaddingX = 2;

    /// <summary>渲染整屏 transcript（行窗口 → footer 行；屏高 ≥ 1）。</summary>
    public static void Render(Screen screen, Theme theme, TranscriptScreenData data)
    {
        RenderLines(screen, data);
        RenderFooter(screen, theme, data);
    }

    /// <summary>帧尾光标位（inline 模式 — ReplShell frameEnd 消费）：
    /// 搜索栏打开 = 查询文本尾（可感知插入点）；否则屏底行首（浏览态无编辑光标）。</summary>
    public static CursorPos CursorPos(TranscriptScreenData data, int screenRows)
    {
        if (data.Footer.SearchQuery is { } query)
        {
            return new CursorPos(FooterPaddingX + 1 + RuneWidth.OfText(query), screenRows - 1);
        }

        return new CursorPos(0, screenRows - 1);
    }

    /// <summary>行窗口渲染 — 视口逐行写冻结行（行索引超快照末尾即止；右缘裁剪走 rightBound）。</summary>
    private static void RenderLines(Screen screen, TranscriptScreenData data)
    {
        for (var viewRow = 0; viewRow < data.ViewportHeight && viewRow < screen.Rows - FooterRows; viewRow++)
        {
            var lineIndex = data.ScrollOffset + viewRow;
            if (lineIndex >= data.Lines.Count)
            {
                return;
            }

            screen.WriteText(viewRow, 0, data.Lines[lineIndex], 0, screen.Cols);
        }
    }

    /// <summary>footer 行渲染（屏底 1 行）— 搜索栏 / 查询激活带徽章 / 默认标题+提示 三形态。</summary>
    private static void RenderFooter(Screen screen, Theme theme, TranscriptScreenData data)
    {
        var row = screen.Rows - FooterRows;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var right = screen.Cols - FooterPaddingX;
        if (data.Footer.SearchQuery is { } query)
        {
            // 搜索栏形态：`/` dim 前缀 + 查询默认色（编辑焦点可感知）+ 右对齐徽章
            var col = screen.WriteText(row, FooterPaddingX, "/", dim, right);
            if (query.Length > 0)
            {
                screen.WriteText(row, col, query, 0, right);
            }

            RenderBadge(screen, row, right, data.Footer, dim);
            return;
        }

        if (data.Footer.SearchActive)
        {
            // 查询提交态（栏关 — n/N 继续工作）：标题+提示 追加 n/N 导航提示 + 右对齐徽章
            var col = screen.WriteText(row, FooterPaddingX, data.Footer.FullText, dim, right);
            screen.WriteText(row, col, $"{Figures.MiddotSeparator}n/N 跳转", dim, right);
            RenderBadge(screen, row, right, data.Footer, dim);
            return;
        }

        screen.WriteText(row, FooterPaddingX, data.Footer.FullText, dim, right);
    }

    /// <summary>搜索徽章右对齐（`N/M` — 无匹配 0/M 不渲染，对齐基准徽章只在有匹配时出现）。</summary>
    private static void RenderBadge(Screen screen, int row, int right, TranscriptFooterState footer, int dim)
    {
        if (footer.SearchCount == 0)
        {
            return;
        }

        var text = $"{footer.SearchCurrent}/{footer.SearchCount}";
        var col = Math.Max(FooterPaddingX, right - RuneWidth.OfText(text));
        screen.WriteText(row, col, text, dim, right);
    }
}
