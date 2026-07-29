namespace JoinCode.Abstractions.Exceptions;

/// <summary>
/// 配置异常
/// </summary>
public sealed class ConfigurationException : WorkflowException
{
    /// <summary>
    /// 配置键
    /// </summary>
    public string? ConfigurationKey { get; }

    /// <summary>
    /// 配置值
    /// </summary>
    public string? ConfigurationValue { get; }

    /// <summary>
    /// 配置文件路径
    /// </summary>
    public string? ConfigurationFilePath { get; }

    /// <summary>
    /// 创建 ConfigurationException
    /// </summary>
    public ConfigurationException(
        string message,
        string? configurationKey = null,
        string? configurationValue = null,
        string? configurationFilePath = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationGeneral.ToValue(), ErrorCategory.Configuration, context)
    {
        ConfigurationKey = configurationKey;
        ConfigurationValue = configurationValue;
        ConfigurationFilePath = configurationFilePath;
    }

    /// <summary>
    /// 创建 ConfigurationException（带内部异常）
    /// </summary>
    public ConfigurationException(
        string message,
        Exception innerException,
        string? configurationKey = null,
        string? configurationValue = null,
        string? configurationFilePath = null,
        string? errorCode = null,
        ExceptionContext? context = null)
        : base(message, innerException, errorCode ?? global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationGeneral.ToValue(), ErrorCategory.Configuration, context)
    {
        ConfigurationKey = configurationKey;
        ConfigurationValue = configurationValue;
        ConfigurationFilePath = configurationFilePath;
    }

    /// <summary>
    /// 创建缺失配置异常
    /// </summary>
    public static ConfigurationException Missing(string key, string? filePath = null)
    {
        return new ConfigurationException(
            $"缺少必需的配置项: {key}",
            configurationKey: key,
            configurationFilePath: filePath,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationMissing.ToValue());
    }

    /// <summary>
    /// 创建无效配置异常
    /// </summary>
    public static ConfigurationException Invalid(string key, string value, string reason, string? filePath = null)
    {
        return new ConfigurationException(
            $"配置项 '{key}' 的值无效: {reason}",
            configurationKey: key,
            configurationValue: value,
            configurationFilePath: filePath,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationInvalid.ToValue());
    }

    /// <summary>
    /// 创建配置解析异常
    /// </summary>
    public static ConfigurationException ParseError(string filePath, string reason, Exception? innerException = null)
    {
        var ex = new ConfigurationException(
            $"无法解析配置文件 '{filePath}': {reason}",
            innerException ?? throw new ArgumentNullException(nameof(innerException)),
            configurationFilePath: filePath,
            errorCode: global::JoinCode.Abstractions.Exceptions.ErrorCode.ConfigurationParseError.ToValue());
        return ex;
    }
}
