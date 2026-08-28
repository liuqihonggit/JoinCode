namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 四叉树象限 — 左下起算（对齐"格子左下角编号"原则）
/// </summary>
public enum Quadrant
{
    [EnumValue("SW")] SW,
    [EnumValue("SE")] SE,
    [EnumValue("NW")] NW,
    [EnumValue("NE")] NE,
}

/// <summary>
/// 八方位方向 — 用于格子间邻居导航，补四叉树数字编码缺乏的四周感知
/// </summary>
public enum CardinalDirection
{
    [EnumValue("N")] N,
    [EnumValue("S")] S,
    [EnumValue("W")] W,
    [EnumValue("E")] E,
    [EnumValue("NW")] NW,
    [EnumValue("NE")] NE,
    [EnumValue("SW")] SW,
    [EnumValue("SE")] SE,
}

/// <summary>
/// 四叉树格子 — 递归编码的定位单元
/// </summary>
/// <param name="Code">数字点分路径编码，如 L0.2.1（L + 层序.象限序...）</param>
/// <param name="Quadrant">相对父格子的象限（SW/SE/NW/NE）</param>
/// <param name="Region">相对根的累积方位（中文语义，如"右上"），辅助 LLM 方位感知</param>
/// <param name="X">左上角 X 坐标（像素）</param>
/// <param name="Y">左上角 Y 坐标（像素）</param>
/// <param name="Width">格子宽度（像素）</param>
/// <param name="Height">格子高度（像素）</param>
/// <param name="Alpha">标注强度：-1=隐藏(无色)，0..1=标注强度</param>
public sealed record QuadtreeCell(
    string Code,
    Quadrant Quadrant,
    string Region,
    int X,
    int Y,
    int Width,
    int Height,
    double Alpha);

/// <summary>
/// 四叉树网格 — 指定层数下所有格子的集合
/// </summary>
/// <param name="Cells">格子列表（按编码排序）</param>
/// <param name="ImageWidth">原图宽度（像素）</param>
/// <param name="ImageHeight">原图高度（像素）</param>
/// <param name="Depth">四叉树层数（缩放精度）</param>
public sealed record QuadtreeGrid(
    IReadOnlyList<QuadtreeCell> Cells,
    int ImageWidth,
    int ImageHeight,
    int Depth);

/// <summary>
/// 格子缩放结果 — 聚焦指定格子裁剪子图后重新编码
/// </summary>
/// <param name="SubImageBase64">裁剪子图的 base64 PNG</param>
/// <param name="Grid">子图上重新构建的四叉树网格（编码重置为 L0 起算）</param>
/// <param name="SourceCellCode">被聚焦的源格子编码</param>
public sealed record QuadtreeZoomResult(
    string SubImageBase64,
    QuadtreeGrid Grid,
    string SourceCellCode);

/// <summary>
/// 渲染结果 — 虚线网格叠加到原图后的 base64 PNG
/// </summary>
/// <param name="RenderedBase64">渲染后图片的 base64 PNG</param>
/// <param name="MediaType">媒体类型（如 "image/png"）</param>
public sealed record QuadtreeRenderResult(
    string RenderedBase64,
    string MediaType);
