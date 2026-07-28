namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 管道上下文基类 — 统一 Failed + ErrorMessage 模式，消除15处重复定义
/// </summary>
public abstract class PipelineContextBase : IPipelineContext
{
    public bool Failed { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 标记管道失败
    /// </summary>
    public void Fail(string message)
    {
        Failed = true;
        ErrorMessage = message;
    }
}
