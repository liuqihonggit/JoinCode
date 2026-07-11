namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 管道上下文基础接口 — 所有管道 Context 都应实现
/// 提供统一的失败标记和错误信息机制
/// </summary>
public interface IPipelineContext
{
    /// <summary>管道是否失败</summary>
    bool Failed { get; set; }

    /// <summary>失败原因</summary>
    string? ErrorMessage { get; set; }

    /// <summary>标记管道失败</summary>
    void Fail(string message);
}
