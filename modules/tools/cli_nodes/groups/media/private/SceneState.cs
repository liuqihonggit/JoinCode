// 桌面场景状态读取 DTO — 与宿主 DesktopSceneState（modules/abstractions/hands/desktop/IDesktopSceneStateStore.cs）
// 的 PascalCase 序列化协议一致。desktop_scene_status 只读不写：宿主 desktop_look/zoom 负责写，
// exe 读同一份 ~/.jcc/scenarios/{sceneId}.json，零写冲突、零状态分叉。
using System.Text.Json.Serialization;

namespace JoinCode.CliNode.Media;

internal sealed record SceneZoomEntry(
    int Depth, string CellCode, int Quadrant, DateTimeOffset Timestamp);

internal sealed record SceneState(
    string SceneId,
    int CurrentDepth,
    string CurrentCellCode,
    IReadOnlyList<SceneZoomEntry> ZoomHistory,
    string? LastScreenshotPath,
    string? LastAction,
    DateTimeOffset CreatedAt);

// 宿主写入侧 DesktopSceneStateJsonContext 无 naming policy → PascalCase 原样落盘。
// 这里显式不设 PropertyNamingPolicy，保证字段名逐字匹配（SceneId/CurrentDepth/...）。
[JsonSerializable(typeof(SceneState))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class SceneStateJsonContext : JsonSerializerContext;
