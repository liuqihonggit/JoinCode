namespace JoinCode.Vision.Quadtree;

/// <summary>
/// 四叉树桌面叠加坐标转换器 — 把四叉树格子坐标转换为屏幕绝对坐标(ADR 0032 延伸应用)
/// 纯计算,无 GDI 依赖。配合 show_desktop_overlay 实现桌面实际叠加显示
/// </summary>
[Register(typeof(IQuadtreeDesktopOverlayMapper), ServiceLifetime.Singleton)]
public sealed class QuadtreeDesktopOverlayMapper : IQuadtreeDesktopOverlayMapper
{
    /// <summary>
    /// 把网格中所有可见格子(alpha≠-1)转换为屏幕坐标矩形
    /// </summary>
    public IReadOnlyList<QuadtreeDesktopRect> MapToScreen(QuadtreeGrid grid, int originScreenX, int originScreenY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var result = new List<QuadtreeDesktopRect>(grid.Cells.Count);
        foreach (var cell in grid.Cells)
        {
            if (cell.Alpha < 0) continue;
            result.Add(new QuadtreeDesktopRect(
                cell.Code,
                originScreenX + cell.X,
                originScreenY + cell.Y,
                cell.Width,
                cell.Height,
                cell.Alpha));
        }
        return result;
    }
}
