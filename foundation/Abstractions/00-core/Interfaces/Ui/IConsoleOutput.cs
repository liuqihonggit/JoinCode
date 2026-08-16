namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 控制台输出接口
/// </summary>
public interface IConsoleOutput
{
    /// <summary>
    /// 输出文本
    /// </summary>
    void WriteLine(string message);

    /// <summary>
    /// 输出错误信息
    /// </summary>
    void WriteError(string message);

    /// <summary>
    /// 输出成功信息
    /// </summary>
    void WriteSuccess(string message);

    /// <summary>
    /// 输出警告信息
    /// </summary>
    void WriteWarning(string message);

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
    void WriteLine(string message, ConsoleColor color);

    /// <summary>
    /// 读取密码输入（隐藏字符）
    /// </summary>
    string ReadPassword(string prompt);
}
