namespace Infrastructure.Network.Downloader.StateMachine;

/// <summary>
/// 状态转换结果 — 记录一次状态机转换的结果
/// </summary>
/// <param name="Success">转换是否成功</param>
/// <param name="PreviousState">转换前状态</param>
/// <param name="NewState">转换后状态(失败时=PreviousState)</param>
/// <param name="Error">失败时的错误信息(含 [DOWN001] 错误码)</param>
public sealed record DownloadStateTransition(
    bool Success,
    DownloadState PreviousState,
    DownloadState NewState,
    string? Error);
