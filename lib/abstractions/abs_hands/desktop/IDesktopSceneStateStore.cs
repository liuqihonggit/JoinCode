namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 桌面场景状态存储服务 — 跨 mcp_call 进程持久化夹逼进度
/// </summary>
public interface IDesktopSceneStateStore
{
    /// <summary>加载场景状态（跨进程文件中转）</summary>
    Task<DesktopSceneState?> LoadAsync(string sceneId, CancellationToken cancellationToken = default);

    /// <summary>保存场景状态（写文件供下次调用读取）</summary>
    Task SaveAsync(DesktopSceneState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// 桌面场景状态 — 夹逼进度快照
/// </summary>
/// <param name="SceneId">场景 ID</param>
/// <param name="CurrentDepth">当前四叉树层数</param>
/// <param name="CurrentCellCode">当前格子编码如 L0.2.1</param>
/// <param name="ZoomHistory">夹逼历史路径</param>
/// <param name="LastScreenshotPath">最后截图文件路径</param>
/// <param name="LastAction">最后执行的动作</param>
/// <param name="CreatedAt">场景创建时间</param>
public sealed record DesktopSceneState(
    string SceneId,
    int CurrentDepth,
    string CurrentCellCode,
    IReadOnlyList<ZoomHistoryEntry> ZoomHistory,
    string? LastScreenshotPath,
    string? LastAction,
    DateTimeOffset CreatedAt);

/// <summary>
/// 夹逼历史条目 — 记录每次 zoom 的层/格子/象限
/// </summary>
/// <param name="Depth">层数</param>
/// <param name="CellCode">格子编码</param>
/// <param name="Quadrant">选择的象限</param>
/// <param name="Timestamp">时间戳</param>
public sealed record ZoomHistoryEntry(int Depth, string CellCode, int Quadrant, DateTimeOffset Timestamp);
