namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 简单用户交互服务 — 用于 UI 通知、确认、简单提问
/// 消费方: InteractiveElicitationHandler (MCP 认证交互)
/// 关系: IInteractiveService 提供结构化多选提问（MCP 工具协议），本接口提供简单交互
/// </summary>
public interface IUserInteractionService
{
    /// <summary>
    /// 询问用户问题
    /// </summary>
    Task<UserInteractionResult> AskQuestionAsync(string question, List<string>? options = null, bool multiSelect = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息给用户
    /// </summary>
    Task SendMessageAsync(string message, MessageType messageType = MessageType.Info, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求用户确认
    /// </summary>
    Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// 用户交互结果
/// </summary>
public sealed record UserInteractionResult(
    bool Success,
    string? Response = null,
    List<string>? SelectedOptions = null,
    string? ErrorMessage = null);

/// <summary>
/// 消息类型
/// </summary>
public enum MessageType
{
    Info,
    Warning,
    Error,
    Success
}
