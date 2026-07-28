
namespace Core.Bridge;

#region 状态栏数据模型 — 对齐 TS 端 bridgeStatusUtil.ts

/// <summary>
/// Bridge 状态 — 对齐 TS 端 StatusState
/// </summary>
public enum BridgeStatusState
{
    /// <summary>空闲</summary>
    [EnumValue("idle")]
    Idle,
    /// <summary>已连接</summary>
    [EnumValue("attached")]
    Attached,
    /// <summary>有标题</summary>
    [EnumValue("titled")]
    Titled,
    /// <summary>重连中</summary>
    [EnumValue("reconnecting")]
    Reconnecting,
    /// <summary>失败</summary>
    [EnumValue("failed")]
    Failed,
}

/// <summary>
/// Bridge 状态信息 — 对齐 TS 端 BridgeStatusInfo
/// </summary>
public sealed class BridgeStatusInfo
{
    public required string Label { get; init; }
    public required string Color { get; init; } // "error" | "warning" | "success"
}

#endregion

/// <summary>
/// Bridge 状态栏 UI 工具集 — 对齐 TS 端 bridgeStatusUtil.ts
/// 闪烁动画渲染 + 状态推导 + URL 构建 + OSC 8 超链接
/// </summary>
public static class BridgeStatusUtil
{
    /// <summary>工具活动行显示过期时间 (30s)</summary>
    public const int ToolDisplayExpiryMs = 30_000;

    /// <summary>闪烁动画间隔 (150ms)</summary>
    public const int ShimmerIntervalMs = 150;

    /// <summary>
    /// 返回 HH:MM:SS 格式时间戳 — 对齐 TS 端 timestamp()
    /// </summary>
    public static string Timestamp()
    {
        return DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>
    /// 截断工具活动摘要到指定宽度 — 对齐 TS 端 abbreviateActivity
    /// </summary>
    public static string AbbreviateActivity(string summary, int maxWidth = 30)
    {
        if (string.IsNullOrEmpty(summary)) return summary;
        if (summary.Length <= maxWidth) return summary;
        return $"{summary[..(maxWidth - 1)]}…";
    }

    /// <summary>
    /// 构建 idle 状态的连接 URL — 对齐 TS 端 buildBridgeConnectUrl
    /// </summary>
    public static string BuildBridgeConnectUrl(string environmentId, string? ingressUrl = null)
    {
        var baseUrl = ingressUrl ?? "https://claude.ai";
        return $"{baseUrl}/bridge/{environmentId}";
    }

    /// <summary>
    /// 构建 session 活跃时的 URL — 对齐 TS 端 buildBridgeSessionUrl
    /// </summary>
    public static string BuildBridgeSessionUrl(string sessionId, string environmentId, string? ingressUrl = null)
    {
        var baseUrl = ingressUrl ?? "https://claude.ai";
        return $"{baseUrl}/bridge/{environmentId}/{sessionId}";
    }

    /// <summary>
    /// 计算反向扫描闪烁动画的索引位置 — 对齐 TS 端 computeGlimmerIndex
    /// </summary>
    public static int ComputeGlimmerIndex(int tick, int messageWidth)
    {
        if (messageWidth <= 0) return 0;
        // 反向扫描：从右到左
        var index = messageWidth - 1 - (tick % messageWidth);
        return Math.Max(0, Math.Min(index, messageWidth - 1));
    }

    /// <summary>
    /// 按视觉宽度将文本分割为三段 — 对齐 TS 端 computeShimmerSegments
    /// 使用 StringInfo 处理多字节字符
    /// </summary>
    public static (string Before, string Shimmer, string After) ComputeShimmerSegments(
        string text, int glimmerIndex)
    {
        if (string.IsNullOrEmpty(text)) return (string.Empty, string.Empty, string.Empty);

        var si = new StringInfo(text);
        var totalLength = si.LengthInTextElements;

        if (totalLength == 0) return (string.Empty, string.Empty, string.Empty);

        var idx = Math.Min(glimmerIndex, totalLength - 1);

        // 提取三段
        var before = idx > 0 ? si.SubstringByTextElements(0, idx) : string.Empty;
        var shimmer = si.SubstringByTextElements(idx, 1);
        var after = idx < totalLength - 1
            ? si.SubstringByTextElements(idx + 1, totalLength - idx - 1)
            : string.Empty;

        return (before, shimmer, after);
    }

    /// <summary>
    /// 根据连接状态推导状态标签和颜色 — 对齐 TS 端 getBridgeStatus
    /// </summary>
    public static BridgeStatusInfo GetBridgeStatus(bool error, bool connected, bool sessionActive, bool reconnecting)
    {
        if (error)
        {
            return new BridgeStatusInfo { Label = "Bridge Error", Color = "error" };
        }

        if (reconnecting)
        {
            return new BridgeStatusInfo { Label = "Reconnecting", Color = "warning" };
        }

        if (!connected)
        {
            return new BridgeStatusInfo { Label = "Disconnected", Color = "error" };
        }

        if (sessionActive)
        {
            return new BridgeStatusInfo { Label = "Active", Color = "success" };
        }

        return new BridgeStatusInfo { Label = "Connected", Color = "success" };
    }

    /// <summary>
    /// idle 状态底部文本
    /// </summary>
    public static string BuildIdleFooterText(string url)
    {
        return $"Waiting for connection: {url}";
    }

    /// <summary>
    /// 活跃状态底部文本
    /// </summary>
    public static string BuildActiveFooterText(string url)
    {
        return $"Bridge session active: {url}";
    }

    /// <summary>失败状态底部文本常量</summary>
    public const string FailedFooterText = "Bridge connection failed. Retrying...";

    /// <summary>
    /// 用 OSC 8 终端超链接协议包裹文本 — 对齐 TS 端 wrapWithOsc8Link
    /// 格式: \x1b]8;;URL\x1b\\TEXT\x1b]8;;\x1b\\
    /// </summary>
    public static string WrapWithOsc8Link(string text, string url)
    {
        // OSC 8 转义序列: ESC ] 8 ; ; URL ST TEXT ESC ] 8 ; ; ST
        // ST (String Terminator) 可以是 ESC \ 或 BEL (0x07)
        return $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\";
    }
}
