namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面场景缩放服务 — 四叉树象限缩小 + 状态更新 + 清晰度判断
/// </summary>
public interface IDesktopSceneZoomService {
    /// <summary>选象限缩小或退回上一层，返回子图 + 更新后的格子编码/层数 + 清晰度判断</summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="quadrant">象限编号: 1=左上 2=右上 3=左下 4=右下（back=true 时忽略）</param>
    /// <param name="back">true 时退回上一层（纠偏用），false 时正常缩小</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<DesktopSceneZoom> ZoomAsync(string sceneId, int quadrant, bool back = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// 桌面场景缩放结果
/// </summary>
/// <param name="SubImageBase64">缩小后的子图 base64 PNG</param>
/// <param name="CurrentCellCode">当前格子编码如 L0.2.1</param>
/// <param name="CurrentDepth">当前四叉树层数</param>
/// <param name="RegionWidth">当前区域宽度（像素）</param>
/// <param name="RegionHeight">当前区域高度（像素）</param>
/// <param name="IsClearEnough">是否已缩到可识别粒度（区域 ≤ 阈值）</param>
public sealed record DesktopSceneZoom(
    string SubImageBase64,
    string CurrentCellCode,
    int CurrentDepth,
    int RegionWidth,
    int RegionHeight,
    bool IsClearEnough);