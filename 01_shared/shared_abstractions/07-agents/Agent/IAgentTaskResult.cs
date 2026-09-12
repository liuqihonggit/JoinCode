namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 任务结果接口 - 定义任务执行结果的通用结构
/// </summary>
public interface IAgentTaskResult
{
    /// <summary>
    /// 任务唯一标识符
    /// </summary>
    string TaskId { get; }

    /// <summary>
    /// 任务是否成功完成
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// 任务输出内容
    /// </summary>
    string Output { get; }

    /// <summary>
    /// 错误信息（如果任务失败）
    /// </summary>
    string? Error { get; }

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    long ExecutionTimeMs { get; }

    /// <summary>
    /// 任务开始时间
    /// </summary>
    DateTime StartedAt { get; }

    /// <summary>
    /// 任务完成时间
    /// </summary>
    DateTime CompletedAt { get; }

    /// <summary>
    /// 结果元数据
    /// </summary>
    Dictionary<string, JsonElement> GetMetadata();

    /// <summary>
    /// 获取指定键的元数据值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">键名</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>元数据值</returns>
    T? GetMetadataValue<T>(string key, T? defaultValue = default);
}
