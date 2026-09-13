namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 四叉树桌面叠加坐标转换器 — 把四叉树格子坐标转换为屏幕绝对坐标
/// 纯计算,无 GDI 依赖,可独立单测。配合 show_desktop_overlay(ADR 0032 延伸应用)实现桌面实际叠加显示
/// </summary>
public interface IQuadtreeDesktopOverlayMapper
{
    /// <summary>
    /// 把网格中所有可见格子(alpha≠-1)转换为屏幕坐标矩形
    /// </summary>
    /// <param name="grid">四叉树网格</param>
    /// <param name="originScreenX">原图左上角在屏幕的 X 坐标</param>
    /// <param name="originScreenY">原图左上角在屏幕的 Y 坐标</param>
    /// <returns>屏幕坐标矩形列表(每个对应一个可见格子,按网格顺序)</returns>
    IReadOnlyList<QuadtreeDesktopRect> MapToScreen(QuadtreeGrid grid, int originScreenX, int originScreenY);
}
