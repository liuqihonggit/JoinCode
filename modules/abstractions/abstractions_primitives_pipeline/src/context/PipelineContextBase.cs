namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 管道上下文基类 — 统一 Failed + ErrorMessage 模式，消除15处重复定义
/// </summary>
public abstract class PipelineContextBase : IPipelineContext {
    /// <summary>获取或设置是否失败。</summary>
    public bool Failed { get; set; }

    /// <summary>获取或设置错误消息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 标记管道失败
    /// </summary>
    public void Fail(string message) {
        Failed = true;
        ErrorMessage = message;
    }
}