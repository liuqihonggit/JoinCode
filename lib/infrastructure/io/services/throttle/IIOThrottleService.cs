namespace IO.Services;

/// <summary>
/// IO 限流服务接口 - 提供全局文件操作流量控制
/// </summary>
public interface IIOThrottleService
{
    /// <summary>
    /// 获取当前并发操作数
    /// </summary>
    int CurrentConcurrentOperations { get; }

    /// <summary>
    /// 获取当前可用令牌数（速率限制）
    /// </summary>
    double CurrentTokens { get; }

    /// <summary>
    /// 异步等待执行许可（并发 + 速率限制）
    /// </summary>
    /// <param name="operationType">操作类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行许可，释放后将归还资源</returns>
    Task<IIOExecutionLease> AcquireAsync(
        IOOperationType operationType = IOOperationType.Read,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取执行许可（非阻塞）
    /// </summary>
    /// <param name="operationType">操作类型</param>
    /// <param name="lease">执行许可</param>
    /// <returns>是否成功获取</returns>
    bool TryAcquire(
        IOOperationType operationType,
        out IIOExecutionLease? lease);
}

/// <summary>
/// IO 执行许可 - 使用 using 语句自动释放资源
/// </summary>
public interface IIOExecutionLease : IDisposable
{
    /// <summary>
    /// 获取许可的时间戳
    /// </summary>
    DateTime AcquiredAt { get; }

    /// <summary>
    /// 操作类型
    /// </summary>
    IOOperationType OperationType { get; }
}

/// <summary>
/// IO 操作类型
/// </summary>
public enum IOOperationType
{
    /// <summary>
    /// 读操作
    /// </summary>
    Read,

    /// <summary>
    /// 写操作
    /// </summary>
    Write,

    /// <summary>
    /// 删除操作
    /// </summary>
    Delete
}
