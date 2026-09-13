
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 斜杠命令接口
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 命令名称 (不含斜杠)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 命令描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 命令用法
    /// </summary>
    string Usage { get; }

    /// <summary>
    /// 执行命令
    /// </summary>
    Task ExecuteAsync(ICommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 命令上下文接口 - 插件通过此接口与命令系统交互
/// </summary>
public interface ICommandContext
{
    /// <summary>
    /// 原始输入
    /// </summary>
    string RawInput { get; }

    /// <summary>
    /// 命令名称
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// 命令参数
    /// </summary>
    string[] Arguments { get; }

    /// <summary>
    /// 当前会话ID
    /// </summary>
    string SessionId { get; }

    /// <summary>
    /// 日志记录器
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// 控制台输出
    /// </summary>
    IConsoleOutput ConsoleOutput { get; }

    /// <summary>
    /// 输出文本到控制台
    /// </summary>
    void Output(string message);

    /// <summary>
    /// 输出错误到控制台
    /// </summary>
    void OutputError(string message);

    /// <summary>
    /// 输出成功信息到控制台
    /// </summary>
    void OutputSuccess(string message);

    /// <summary>
    /// 输出警告信息到控制台
    /// </summary>
    void OutputWarning(string message);

    /// <summary>
    /// 提示用户输入
    /// </summary>
    string? Prompt(string message);

    /// <summary>
    /// 提示用户确认
    /// </summary>
    bool Confirm(string message);

    /// <summary>
    /// 输出带颜色的文本
    /// </summary>
    void Output(string message, ConsoleColor color);

    /// <summary>
    /// 读取密码输入（隐藏字符）
    /// </summary>
    string ReadPassword(string prompt);
}

/// <summary>
/// 命令特性 - 用于自动注册
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandAttribute : Attribute
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
}
