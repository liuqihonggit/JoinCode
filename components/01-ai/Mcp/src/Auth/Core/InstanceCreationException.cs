
namespace McpClient;

/// <summary>
/// 实例创建失败时抛出的异常
/// </summary>
public sealed class InstanceCreationException : WorkflowException
{
    /// <summary>
    /// 目标类型
    /// </summary>
    public Type? TargetType { get; }

    /// <summary>
    /// 创建策略
    /// </summary>
    public InstanceCreationStrategy? Strategy { get; }

    /// <inheritdoc />
    public override bool IsRetryable => false;

    /// <summary>
    /// 创建 InstanceCreationException
    /// </summary>
    public InstanceCreationException(
        string message,
        Type? targetType = null,
        InstanceCreationStrategy? strategy = null,
        ExceptionContext? context = null)
        : base(message, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpInstanceCreation.ToValue(), ErrorCategory.Mcp, context)
    {
        TargetType = targetType;
        Strategy = strategy;
    }

    /// <summary>
    /// 创建 InstanceCreationException（带内部异常）
    /// </summary>
    public InstanceCreationException(
        string message,
        Exception innerException,
        Type? targetType = null,
        InstanceCreationStrategy? strategy = null,
        ExceptionContext? context = null)
        : base(message, innerException, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpInstanceCreation.ToValue(), ErrorCategory.Mcp, context)
    {
        TargetType = targetType;
        Strategy = strategy;
    }

    /// <summary>
    /// 创建抽象类或接口实例化异常
    /// </summary>
    public static InstanceCreationException AbstractOrInterface(Type type)
    {
        return new InstanceCreationException(
            $"无法创建抽象类或接口的实例: {type.FullName}",
            targetType: type,
            strategy: InstanceCreationStrategy.Activator);
    }

    /// <summary>
    /// 创建缺少构造函数异常
    /// </summary>
    public static InstanceCreationException MissingConstructor(Type type, InstanceCreationStrategy strategy)
    {
        return new InstanceCreationException(
            $"类型 '{type.FullName}' 没有公共构造函数",
            targetType: type,
            strategy: strategy);
    }

    /// <summary>
    /// 创建构造函数调用失败异常
    /// </summary>
    public static InstanceCreationException ConstructorFailed(Type type, Exception innerException)
    {
        return new InstanceCreationException(
            $"调用类型 '{type.FullName}' 的构造函数时失败",
            innerException,
            targetType: type,
            strategy: InstanceCreationStrategy.Activator);
    }
}
