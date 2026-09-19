
namespace McpClient;

/// <summary>
/// 实例创建失败时抛出的异常
/// </summary>
public sealed class InstanceCreationException : WorkflowException {
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
    /// <param name="message">异常消息</param>
    /// <param name="targetType">目标类型（可选）</param>
    /// <param name="strategy">创建策略（可选）</param>
    /// <param name="context">异常上下文（可选）</param>
    public InstanceCreationException(
        string message,
        Type? targetType = null,
        InstanceCreationStrategy? strategy = null,
        ExceptionContext? context = null)
        : base(message, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpInstanceCreation.ToValue(), ErrorCategory.Mcp, context) {
        TargetType = targetType;
        Strategy = strategy;
    }

    /// <summary>
    /// 创建 InstanceCreationException（带内部异常）
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    /// <param name="targetType">目标类型（可选）</param>
    /// <param name="strategy">创建策略（可选）</param>
    /// <param name="context">异常上下文（可选）</param>
    public InstanceCreationException(
        string message,
        Exception innerException,
        Type? targetType = null,
        InstanceCreationStrategy? strategy = null,
        ExceptionContext? context = null)
        : base(message, innerException, global::JoinCode.Abstractions.Exceptions.ErrorCode.McpInstanceCreation.ToValue(), ErrorCategory.Mcp, context) {
        TargetType = targetType;
        Strategy = strategy;
    }

    /// <summary>
    /// 创建抽象类或接口实例化异常
    /// </summary>
    /// <param name="type">无法实例化的类型</param>
    /// <returns>InstanceCreationException 实例</returns>
    public static InstanceCreationException AbstractOrInterface(Type type) {
        return new InstanceCreationException(
            $"无法创建抽象类或接口的实例: {type.FullName}",
            targetType: type,
            strategy: InstanceCreationStrategy.Activator);
    }

    /// <summary>
    /// 创建缺少构造函数异常
    /// </summary>
    /// <param name="type">目标类型</param>
    /// <param name="strategy">使用的创建策略</param>
    /// <returns>InstanceCreationException 实例</returns>
    public static InstanceCreationException MissingConstructor(Type type, InstanceCreationStrategy strategy) {
        return new InstanceCreationException(
            $"类型 '{type.FullName}' 没有公共构造函数",
            targetType: type,
            strategy: strategy);
    }

    /// <summary>
    /// 创建构造函数调用失败异常
    /// </summary>
    /// <param name="type">目标类型</param>
    /// <param name="innerException">构造函数抛出的异常</param>
    /// <returns>InstanceCreationException 实例</returns>
    public static InstanceCreationException ConstructorFailed(Type type, Exception innerException) {
        return new InstanceCreationException(
            $"调用类型 '{type.FullName}' 的构造函数时失败",
            innerException,
            targetType: type,
            strategy: InstanceCreationStrategy.Activator);
    }
}