namespace Core.Context;

/// <summary>
/// 流式块处理器接口 — 工具检测、思考分离、循环检测、用量提取
/// </summary>
public interface IChatStreamChunkProcessor
{
    /// <summary>
    /// 创建迭代状态
    /// </summary>
    IterationState CreateIterationState();

    /// <summary>
    /// 处理流式块
    /// </summary>
    StreamChunkResult ProcessChunk(StreamEvent chunk, IterationState state);
}
