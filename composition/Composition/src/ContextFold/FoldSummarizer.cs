namespace JoinCode.Composition.ContextFold;

/// <summary>
/// 上下文折叠摘要器实现 — L4 历史压缩层调 LLM 把对话头部摘要化
/// <para>在 Composition 层实现，注入 IChatClient（Abstractions），调 LLM 生成 head 摘要。</para>
/// <para>对齐 SubAgentSummaryClient 的调用模式：GetChatCompletionService → GetApiMessageContentsAsync。</para>
/// </summary>
[Register(typeof(IFoldSummarizer))]
public sealed partial class FoldSummarizer : ServiceEntity, IFoldSummarizer
{
    private readonly ILogger<FoldSummarizer>? _logger;
    private readonly IChatClient _kernel;

    public FoldSummarizer(IChatClient kernel, ILogger<FoldSummarizer>? logger = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> SummarizeForFoldAsync(
        IReadOnlyList<ApiMessage> headMessages,
        CancellationToken cancellationToken = default)
    {
        if (headMessages.Count == 0)
            return string.Empty;

        var transcript = BuildTranscript(headMessages);
        var chatHistory = new MessageList
        {
            new(MessageRole.System, SystemPrompt),
            new(MessageRole.User, transcript),
        };

        var completion = _kernel.GetChatCompletionService();
        var results = await completion.GetApiMessageContentsAsync(chatHistory, cancellationToken: cancellationToken).ConfigureAwait(false);
        var summary = results.FirstOrDefault()?.Content;

        if (string.IsNullOrEmpty(summary))
        {
            throw new InvalidOperationException("L4 折叠摘要 LLM 返回空");
        }

        return summary;
    }

    private static string BuildTranscript(IReadOnlyList<ApiMessage> messages)
    {
        return string.Join("\n", messages.Select(msg =>
        {
            var role = msg.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                MessageRole.Tool => "tool",
                _ => msg.Role.ToString(),
            };
            return $"[{role}]: {msg.Content ?? string.Empty}";
        }));
    }

    private const string SystemPrompt = """
        你是对话历史摘要助手。你的任务是将对话的早期部分压缩成详细摘要，以便后续工作能在不丢失上下文的情况下继续。
        最近的消息保持完整，不需要摘要。仅将你的摘要集中在早期消息中讨论、学习和完成的内容上。

        在提供最终摘要之前，将你的分析包裹在 <analysis> 标签中，以组织你的思路并确保你涵盖了所有必要要点。在你的分析过程中：
        1. 按时间顺序分析早期消息的每个部分。对每个部分彻底识别：
            - 用户的明确请求和意图
            - 处理用户请求的方法
            - 关键决策、技术概念和代码模式
            - 具体细节，如文件名、完整代码片段、函数签名、文件编辑
            - 遇到的错误以及如何修复它们
            - 特别注意收到的具体用户反馈
        2. 仔细检查技术准确性和完整性。

        你的摘要应包括以下部分：

        1. 主要请求和意图：详细捕捉用户的所有明确请求和意图
        2. 关键技术概念：列出讨论的所有重要技术概念、技术和框架。
        3. 文件和代码部分：枚举检查、修改或创建的具体文件和代码部分。
           在适用的情况下包含完整代码片段，并包含为什么这个文件读取或编辑很重要的摘要。
        4. 错误和修复：列出遇到的所有错误，以及是如何修复它们的。
           特别注意收到的具体用户反馈，尤其是如果用户告诉你以不同方式做某事。
        5. 问题解决：记录已解决的问题和任何正在进行的故障排除工作。
        6. 所有用户消息：列出所有不是工具结果的用户消息。这些对于理解用户的反馈和变化的意图至关重要。
        7. 待处理任务：概述被明确要求处理的任何待处理任务。
        8. 当前工作：详细描述在摘要请求之前正在处理的内容，特别注意用户和助理的最近消息。
           在适用的情况下包含文件名和代码片段。
        9. 可选下一步：列出与正在做的最近工作相关的下一步。
           重要：确保这一步与用户最近的明确请求直接一致。
           如果有下一步，包含最近对话中的直接引语，准确显示正在处理什么任务以及在哪里停止。

        你的输出格式：

        <analysis>
        [你的思考过程，确保彻底准确地涵盖所有要点]
        </analysis>

        <summary>
        1. 主要请求和意图：
            [详细描述]

        2. 关键技术概念：
            - [概念 1]
            - [概念 2]

        3. 文件和代码部分：
            - [文件名 1]
                - [为什么这个文件很重要的摘要]
                - [重要代码片段]

        4. 错误和修复：
            - [错误描述]：
                - [你如何修复它]

        5. 问题解决：
            [描述]

        6. 所有用户消息：
            - [详细的非工具使用用户消息]

        7. 待处理任务：
            - [任务 1]

        8. 当前工作：
            [当前工作的精确描述]

        9. 可选下一步：
            [可选的下一步]

        </summary>

        请仅基于早期消息提供你的摘要，遵循这个结构并确保精确性和彻底性。
        """;
}
