
namespace Core.Context.Compact;

/// <summary>
/// 微压缩服务 — 对齐 TS microCompact.ts
/// 纯规则压缩，不调用 LLM。核心思路：把旧工具调用的大段输出替换为一行占位符，省 token 但不丢失消息结构。
/// 两种触发模式：
///   1. 时间间隔压缩：最后一条助手消息距今超过阈值（默认60分钟）时自动触发
///   2. 普通微压缩：无时间条件，直接清除旧工具结果内容
/// </summary>
[Register]
public sealed partial class MicrocompactService : IMicrocompactService {
    [Inject] private readonly IClockService _clock;

    /// <summary>粗略估算：每4字节≈1个token（英文为主时约4字符/token）</summary>
    private const int BytesPerToken = 4;

    /// <summary>图片/文档类内容块的固定token估算值 — 对齐 TS 端逻辑</summary>
    private const int ImageMaxTokenSize = 2000;

    /// <summary>
    /// 可压缩的工具名集合 — 对齐 TS COMPACTABLE_TOOLS
    /// 只有这些工具的输出结果才会被微压缩清除（因为它们通常产生大段文本输出）
    /// 不在此集合中的工具（如计算器、简单查询）结果不会被清除
    /// </summary>
    private static readonly HashSet<string> CompactableTools =
    [
        FileToolNameConstants.FileRead,       // "Read" — 读取文件内容，输出通常很长
        ShellToolNameConstants.Bash,  // "Bash" — 命令执行结果
        ShellToolNameConstants.Powershell,    // "PowerShell" — PowerShell执行结果
        SearchToolNameConstants.Grep,         // "Grep" — 搜索匹配结果
        SearchToolNameConstants.Glob,         // "Glob" — 文件列表
        WebToolNameConstants.WebSearch,       // "WebSearch" — 网页搜索结果
        WebToolNameConstants.WebFetch,        // "WebFetch" — 网页抓取内容
        FileToolNameConstants.FileEdit,       // "Edit" — 编辑操作确认信息
        FileToolNameConstants.FileWrite,      // "Write" — 写入操作确认信息
    ];

    /// <summary>
    /// 普通微压缩 — 清除旧工具结果内容，保留最近 N 个
    /// 流程：收集可压缩工具ID → 保留最近N个 → 其余替换为占位符
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="compactableToolNames">自定义可压缩工具名集合，null则使用默认 CompactableTools</param>
    /// <param name="keepRecent">保留最近N个工具结果不被清除，默认5</param>
    public MicrocompactResult CompactMessages(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames = null,
        int keepRecent = 5) {
        ArgumentNullException.ThrowIfNull(messages);

        // 第一步：从所有助手消息中提取属于可压缩工具的调用ID，按出现顺序排列
        var compactableIds = CollectCompactableToolIds(messages, compactableToolNames);
        if (compactableIds.Count == 0) {
            // 没有可压缩的工具调用，直接返回未压缩结果
            return new MicrocompactResult {
                Messages = messages,
                ToolsCleared = 0,
                TokensSaved = 0,
                WasCompacted = false
            };
        }

        // 第二步：将工具调用ID分为两组 — keepSet(保留) 和 clearSet(清除)
        // keepSet 取最后 keepRecent 个ID（即最近的工具调用），其余全部清除
        var keepRecentSafe = Math.Max(1, keepRecent);
        var keepSet = new HashSet<string>(compactableIds.Skip(Math.Max(0, compactableIds.Count - keepRecentSafe)), StringComparer.Ordinal);
        var clearSet = new HashSet<string>(compactableIds.Where(id => !keepSet.Contains(id)), StringComparer.Ordinal);

        if (clearSet.Count == 0) {
            // 所有工具结果都在保留窗口内，无需清除
            return new MicrocompactResult {
                Messages = messages,
                ToolsCleared = 0,
                TokensSaved = 0,
                WasCompacted = false
            };
        }

        // 第三步：执行清除 — 将 clearSet 中工具结果的内容替换为占位符
        var (result, tokensSaved) = ClearToolResults(messages, clearSet);

        return new MicrocompactResult {
            Messages = result,
            ToolsCleared = clearSet.Count,
            TokensSaved = tokensSaved,
            WasCompacted = tokensSaved > 0
        };
    }

    /// <summary>
    /// 时间间隔微压缩 — 当对话空闲超过阈值时触发
    /// 场景：用户离开很久后回来，旧的工具输出已无参考价值，可以安全清除
    /// 与普通微压缩的区别：仅在时间间隔足够大时才触发，避免活跃对话中误清
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="gapThresholdMinutes">空闲时间阈值（分钟），默认60分钟</param>
    /// <param name="keepRecent">保留最近N个工具结果，默认5</param>
    /// <returns>时间间隔压缩结果；不满足条件时返回null，由下一个中间件处理</returns>
    public TimeBasedMicrocompactResult? TimeBasedCompact(
        IReadOnlyList<ApiMessage> messages,
        int gapThresholdMinutes = 60,
        int keepRecent = 5) {
        ArgumentNullException.ThrowIfNull(messages);

        // 找到最后一条助手消息，用于判断对话空闲时间
        var lastAssistant = messages.LastOrDefault(m => m.Role == MessageRole.Assistant);
        if (lastAssistant is null) {
            return null;
        }

        // 提取最后助手消息的时间戳（从元数据中获取）
        var lastTimestamp = lastAssistant.ExtractTimestamp();
        if (lastTimestamp is null) {
            return null;
        }

        // 计算空闲时间，不足阈值则不触发
        var gapMinutes = (_clock.GetUtcNow() - lastTimestamp.Value).TotalMinutes;
        if (gapMinutes < gapThresholdMinutes) {
            return null;
        }

        // 满足时间条件后，执行与普通微压缩相同的清除逻辑
        var compactableIds = CollectCompactableToolIds(messages, null);
        var keepRecentSafe = Math.Max(1, keepRecent);
        var keepSet = new HashSet<string>(compactableIds.Skip(Math.Max(0, compactableIds.Count - keepRecentSafe)), StringComparer.Ordinal);
        var clearSet = new HashSet<string>(compactableIds.Where(id => !keepSet.Contains(id)), StringComparer.Ordinal);

        if (clearSet.Count == 0) {
            return null;
        }

        var (result, tokensSaved) = ClearToolResults(messages, clearSet);

        if (tokensSaved == 0) {
            return null;
        }

        return new TimeBasedMicrocompactResult {
            Messages = result,
            GapMinutes = gapMinutes,
            ToolsCleared = clearSet.Count,
            ToolsKept = keepSet.Count,
            TokensSaved = tokensSaved
        };
    }

    /// <summary>
    /// 估算消息 token 数 — 对齐 TS calculateMessageTokens
    /// 粗略估算，用于判断是否需要触发压缩以及计算压缩节省量
    /// 估算维度：文本内容 + 多模态内容块 + 助手消息中的工具调用
    /// </summary>
    public int EstimateMessageTokens(IReadOnlyList<ApiMessage> messages) {
        var totalTokens = 0;
        foreach (var msg in messages) {
            // 文本内容的token估算：字符数/4
            if (msg.Content is not null) {
                totalTokens += RoughTokenCount(msg.Content);
            }

            // 多模态内容块 — 对齐 TS: image 2000, document 2000
            if (msg.ContentBlocks is not null) {
                foreach (var block in msg.ContentBlocks) {
                    if (block.Type == ToolContentType.Image || block.Type == ToolContentType.Document) {
                        // 图片和文档使用固定估算值，因为实际token数难以从字面计算
                        totalTokens += ImageMaxTokenSize;
                    } else if (block.Type == ToolContentType.Text && block.Text is not null) {
                        totalTokens += RoughTokenCount(block.Text);
                    }
                }
            }

            // Assistant 消息中的 tool_use — 对齐 TS: name + input
            // 工具调用本身也消耗token（工具名 + 参数），需要计入
            if (msg.Role == MessageRole.Assistant) {
                foreach (var (_, name) in msg.ExtractToolCalls()) {
                    totalTokens += RoughTokenCount(name) + RoughTokenCount("{}"); // name + 空参数估算
                }
            }
        }

        // 最终乘以4/3的修正系数 — 粗略补偿分词器开销（实际token通常比字符/4略多）
        return (int)Math.Ceiling(totalTokens * 4.0 / 3.0);
    }

    /// <summary>
    /// 清除工具结果 — 对齐 TS maybeTimeBasedMicrocompact 中的 block 替换逻辑
    /// 核心操作：遍历 Tool 角色消息，将 clearSet 中的工具结果内容替换为占位符
    /// 注意：不删除消息本身，只替换内容。保持消息结构完整，避免破坏对话轮次对应关系
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="clearSet">需要清除内容的工具调用ID集合</param>
    /// <returns>替换后的消息列表 + 节省的token数</returns>
    private static (List<ApiMessage> Messages, int TokensSaved) ClearToolResults(
        IReadOnlyList<ApiMessage> messages,
        HashSet<string> clearSet) {
        var tokensSaved = 0;
        var result = new List<ApiMessage>(messages.Count);

        foreach (var msg in messages) {
            // 非工具结果消息或无内容，原样保留
            if (msg.Role != MessageRole.Tool || msg.Content is null) {
                result.Add(msg);
                continue;
            }

            // 不是待清除的工具结果，原样保留
            var toolCallId = msg.ExtractToolCallId();
            if (toolCallId is null || !clearSet.Contains(toolCallId)) {
                result.Add(msg);
                continue;
            }

            // 已经被清除过的消息不再重复处理（幂等保护）
            // 对齐 TS: block.content !== TIME_BASED_MC_CLEARED_MESSAGE
            if (string.Equals(msg.Content, ContentReplacementConstants.ToolResultClearedMessage, StringComparison.Ordinal)) {
                result.Add(msg);
                continue;
            }

            // 执行替换：原内容 → "[Old tool result content cleared]"
            // 保留元数据、模型ID、token用量等，只替换文本内容
            tokensSaved += RoughTokenCount(msg.Content);
            result.Add(new ApiMessage(msg.Role, ContentReplacementConstants.ToolResultClearedMessage, msg.Metadata, msg.ModelId, msg.TokenUsage));
        }

        return (result, tokensSaved);
    }

    /// <summary>
    /// 收集可压缩的工具调用 ID — 对齐 TS collectCompactableToolIds
    /// 遍历所有助手消息，提取属于可压缩工具集合的调用ID
    /// 返回的ID列表按消息中出现顺序排列，因此末尾的就是最近的工具调用
    /// </summary>
    /// <param name="messages">消息列表</param>
    /// <param name="compactableToolNames">自定义可压缩工具名集合，null则使用默认</param>
    /// <returns>可压缩的工具调用ID列表（按出现顺序）</returns>
    private static List<string> CollectCompactableToolIds(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames) {
        var effectiveToolNames = compactableToolNames ?? CompactableTools;
        var ids = new List<string>();

        foreach (var msg in messages) {
            // 只从助手消息中提取工具调用（工具调用总是由助手发起）
            if (msg.Role != MessageRole.Assistant)
                continue;

            foreach (var (id, name) in msg.ExtractToolCalls()) {
                // 只收集属于可压缩工具集合的调用ID
                if (effectiveToolNames.Contains(name)) {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// 粗略token计数 — 字符数/4，最少1
    /// 这是简化估算，实际token数取决于分词器，但用于压缩判断足够精确
    /// </summary>
    private static int RoughTokenCount(string text) {
        return Math.Max(1, text.Length / BytesPerToken);
    }
}
