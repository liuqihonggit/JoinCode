
namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记需要异常拦截处理的方法
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class InterceptAttribute : Attribute
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel LogLevel { get; }

    /// <summary>
    /// 是否重新抛出异常
    /// </summary>
    public bool RethrowException { get; }

    /// <summary>
    /// 自定义错误消息
    /// </summary>
    public string? CustomErrorMessage { get; }

    /// <summary>
    /// 创建拦截特性
    /// </summary>
    /// <param name="logLevel">日志级别</param>
    /// <param name="rethrowException">是否重新抛出异常</param>
    /// <param name="customErrorMessage">自定义错误消息</param>
    public InterceptAttribute(
        LogLevel logLevel = LogLevel.Error,
        bool rethrowException = true,
        string? customErrorMessage = null)
    {
        LogLevel = logLevel;
        RethrowException = rethrowException;
        CustomErrorMessage = customErrorMessage;
    }
}
