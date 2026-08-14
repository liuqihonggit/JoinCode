namespace JoinCode;

/// <summary>
/// 确认门控 — 协调 readTask 和 CliPermissionConfirmationHandler 的输入路由
/// 当确认待处理时，readTask 将输入路由到 Source 而非 inputChannel
/// </summary>
internal static class ConfirmationGate
{
    internal static volatile bool Pending;
    internal static TaskCompletionSource<string>? Source;
}
