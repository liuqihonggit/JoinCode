
namespace Core.Context.Compact;

/// <summary>
/// 会话记忆提取服务 — 对齐 TS sessionMemory.ts
/// 负责初始化会话记忆文件、构建更新提示词、判断提取阈值
/// </summary>
public interface ISessionMemoryExtractionService
{
    /// <summary>
    /// 初始化会话记忆文件（如不存在则用默认模板创建）
    /// </summary>
    Task<string> InitializeSessionMemoryFileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建会话记忆更新提示词 — 消费 SessionMemoryPromptTemplate.BuildSessionMemoryUpdatePrompt()
    /// </summary>
    Task<string> BuildExtractionPromptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断是否应该执行提取（基于 token/tool-call 阈值）
    /// </summary>
    bool ShouldExtract(int currentTokenCount, int toolCallsSinceLastUpdate);

    /// <summary>
    /// 获取会话记忆文件路径
    /// </summary>
    string GetMemoryFilePath();

    /// <summary>
    /// 记录提取已完成（更新内部计数器）
    /// </summary>
    void RecordExtractionCompleted(int tokenCountAtExtraction);
}
