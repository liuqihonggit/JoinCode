namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 四叉树标注器 — 纯网格计算（无图像依赖，可独立单测）
/// 编码：数字点分路径 L0.2.1，象限序 SW=0/SE=1/NW=2/NE=3（左下起算）
/// </summary>
public interface IQuadtreeAnnotator
{
    /// <summary>构建指定层数的空网格（所有格子 alpha=-1 隐藏）</summary>
    /// <param name="imageWidth">原图宽度（像素）</param>
    /// <param name="imageHeight">原图高度（像素）</param>
    /// <param name="depth">四叉树层数（1=4格，2=16格，n=4^n 格）</param>
    QuadtreeGrid BuildGrid(int imageWidth, int imageHeight, int depth);

    /// <summary>查询格子的八方位邻居编码（同层）</summary>
    /// <param name="cellCode">源格子编码（如 L0.2.1）</param>
    /// <param name="direction">八方位方向</param>
    /// <param name="imageWidth">原图宽度（用于边界判断）</param>
    /// <param name="imageHeight">原图高度</param>
    /// <param name="depth">当前网格层数</param>
    /// <returns>邻居格子编码，边界外返回 null</returns>
    string? GetNeighbor(string cellCode, CardinalDirection direction, int imageWidth, int imageHeight, int depth);

    /// <summary>批量染色格子（更新 alpha 值）</summary>
    /// <param name="grid">原网格</param>
    /// <param name="paints">格子编码 → alpha(-1=隐藏, 0..1=强度) 映射</param>
    /// <returns>更新后的网格（不可变，返回新实例）</returns>
    QuadtreeGrid PaintCells(QuadtreeGrid grid, IReadOnlyDictionary<string, double> paints);
}

/// <summary>
/// 四叉树渲染器 — 图像操作（裁剪 + 渲染虚线叠加），依赖 SkiaSharp
/// </summary>
public interface IQuadtreeRenderer
{
    /// <summary>渲染虚线网格叠加到原图 — 虚线 + 线性比例宽度 + 透明度</summary>
    /// <param name="imageBase64">原图 base64</param>
    /// <param name="grid">四叉树网格（仅渲染 alpha≠-1 的格子）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后图片的 base64 PNG</returns>
    Task<QuadtreeRenderResult> RenderAsync(string imageBase64, QuadtreeGrid grid, CancellationToken cancellationToken = default);

    /// <summary>聚焦指定格子 — 裁剪子图 + 重新构建四叉树编码</summary>
    /// <param name="imageBase64">原图 base64</param>
    /// <param name="cellCode">要聚焦的格子编码</param>
    /// <param name="imageWidth">原图宽度（定位格子）</param>
    /// <param name="imageHeight">原图高度</param>
    /// <param name="sourceDepth">源格子所在层数</param>
    /// <param name="targetDepth">子图新网格层数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>子图 base64 + 新网格（编码重置 L0 起算）</returns>
    Task<QuadtreeZoomResult> ZoomAsync(
        string imageBase64,
        string cellCode,
        int imageWidth,
        int imageHeight,
        int sourceDepth,
        int targetDepth,
        CancellationToken cancellationToken = default);
}
