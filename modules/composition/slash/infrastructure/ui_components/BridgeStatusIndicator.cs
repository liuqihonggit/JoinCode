namespace JoinCode.Cli;

// ─── BridgeConnectionState / BridgeStatusIndicator ───

/// <summary>
/// Bridge 连接状态
/// </summary>
public enum BridgeConnectionState {
    /// <summary>
    /// 空闲状态
    /// </summary>
    [EnumValue("idle")]
    Idle,

    /// <summary>
    /// 未连接
    /// </summary>
    [EnumValue("disconnected")]
    Disconnected,

    /// <summary>
    /// 连接中
    /// </summary>
    [EnumValue("connecting")]
    Connecting,

    /// <summary>
    /// 已连接
    /// </summary>
    [EnumValue("connected")]
    Connected,

    /// <summary>
    /// 错误状态
    /// </summary>
    [EnumValue("error")]
    Error
}

/// <summary>
/// Bridge 状态指示器 — CLI 简化版
/// </summary>
public static class BridgeStatusIndicator {
    /// <summary>
    /// 渲染连接状态为带颜色的状态文本
    /// </summary>
    /// <param name="state">连接状态</param>
    /// <returns>带 ANSI 颜色的状态文本</returns>
    public static string Render(BridgeConnectionState state) => GetStatusText(state);

    /// <summary>
    /// 获取连接状态对应的文本描述
    /// </summary>
    /// <param name="state">连接状态</param>
    /// <returns>带 ANSI 颜色的状态文本</returns>
    public static string GetStatusText(BridgeConnectionState state) => state switch {
        BridgeConnectionState.Connected => $"{TerminalColors.Success}● 已连接{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Connecting => $"{TerminalColors.Warning}● 连接中...{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Disconnected => $"{TerminalColors.Muted}○ 未连接{AnsiStyleEnumConstants.Reset}",
        BridgeConnectionState.Error => $"{TerminalColors.Error}● 错误{AnsiStyleEnumConstants.Reset}",
        _ => $"{TerminalColors.Muted}○ 未知{AnsiStyleEnumConstants.Reset}"
    };
}