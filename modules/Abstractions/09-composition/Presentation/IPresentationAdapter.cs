namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 表示层适配器接口 — 解耦 CLI 和 TUI 的统一抽象
/// CLI 模式: ConsolePresentationAdapter (纯文本输出，适合自动化测试)
/// TUI 模式: TuiPresentationAdapter (桥接到 AgentTuiApp 渲染层)
/// </summary>
public interface IPresentationAdapter : IDisposable
{
    /// <summary>
    /// 启动表示层
    /// </summary>
    void Start();

    /// <summary>
    /// 停止表示层
    /// </summary>
    void Stop();

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 显示助手消息
    /// </summary>
    void DisplayAssistantMessage(string content);

    /// <summary>
    /// 显示系统消息
    /// </summary>
    void DisplaySystemMessage(string content);

    /// <summary>
    /// 显示错误消息
    /// </summary>
    void DisplayErrorMessage(string content);

    /// <summary>
    /// 更新流式消息内容
    /// </summary>
    void UpdateStreamingMessage(string content);

    /// <summary>
    /// 提交流式消息的最终内容
    /// </summary>
    void CommitStreamingMessage(string finalContent);

    /// <summary>
    /// 如果流式消息不为空则提交
    /// </summary>
    void CommitStreamingIfNotEmpty();

    /// <summary>
    /// 开始新的对话轮次
    /// </summary>
    void StartNewTurn();

    /// <summary>
    /// 显示工具调用开始
    /// </summary>
    void ShowToolStart(string toolName, string? arguments);

    /// <summary>
    /// 显示工具调用结果
    /// </summary>
    void ShowToolResult(string toolName, string? result, bool success);

    /// <summary>
    /// 显示工具调用进度
    /// </summary>
    void ShowToolProgress(string toolName, string progressType, string progressMessage);

    /// <summary>
    /// 显示权限对话框
    /// </summary>
    void ShowPermissionDialog(string dialogContent, string toolName);

    /// <summary>
    /// 请求用户输入
    /// </summary>
    Task<string> RequestInputAsync(string? prompt = null, CancellationToken ct = default);

    /// <summary>
    /// 请求用户确认
    /// </summary>
    Task<bool> ConfirmAsync(string message, CancellationToken ct = default);
}
