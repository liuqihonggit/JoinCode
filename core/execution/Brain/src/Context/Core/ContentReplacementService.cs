namespace Core.Context;

public interface IContentReplacementService
{
    /// <summary>
    /// 对齐 TS maybePersistLargeToolResult — 纯函数，不修改任何 state
    /// 仅检查内容大小和工具阈值，超限时持久化到磁盘并返回替换字符串
    /// </summary>
    string? MaybePersistLargeToolResult(string toolName, string toolUseId, string content, string sessionId);

    /// <summary>
    /// 对齐 TS applyToolResultBudget — 返回处理后的消息和新产生的替换记录
    /// newlyReplaced: 本次调用新做出的替换决策（不含 mustReapply 的重新应用）
    /// ⚠️ 副作用: 会原地修改 state 参数 — 向 state.SeenIds 添加已见 ID，向 state.Replacements 添加新替换映射
    /// 需要隔离时先调用 state.Clone() 克隆副本（对齐 TS cloneContentReplacementState）
    /// 异步: 对齐 TS Promise.all 并发持久化，串行文件 I/O → 并行
    /// </summary>
    Task<(IReadOnlyList<ApiMessage> Messages, IReadOnlyList<ContentReplacementRecord> NewlyReplaced)> ApplyToolResultBudgetAsync(
        IReadOnlyList<ApiMessage> messages,
        ContentReplacementState state,
        string sessionId,
        HashSet<string>? neverPersistTools = null,
        CancellationToken cancellationToken = default);

    ContentReplacementState ReconstructState(IReadOnlyList<ContentReplacementRecord> records);

    /// <summary>
    /// 对齐 TS reconstructContentReplacementState — 从消息历史+记录+继承替换重建状态
    /// </summary>
    ContentReplacementState ReconstructState(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlyList<ContentReplacementRecord> records,
        IReadOnlyDictionary<string, string>? inheritedReplacements = null);

    /// <summary>
    /// 对齐 TS provisionContentReplacementState — 功能开关入口
    /// 功能开关关闭时返回 null（query 会跳过整个预算执行）
    /// 有初始消息时走重建路径，冷启动时走新建路径
    /// </summary>
    ContentReplacementState? ProvisionContentReplacementState(
        IReadOnlyList<ApiMessage>? initialMessages = null,
        IReadOnlyList<ContentReplacementRecord>? initialContentReplacements = null);

    /// <summary>
    /// 对齐 TS reconstructForSubagentResume — 子智能体恢复重建
    /// 传入父级 state 的 replacements 作为继承替换源
    /// 父级状态为 null 时直接返回 null（功能开关关闭）
    /// </summary>
    ContentReplacementState? ReconstructForSubagentResume(
        ContentReplacementState? parentState,
        IReadOnlyList<ApiMessage> resumedMessages,
        IReadOnlyList<ContentReplacementRecord> sidechainRecords);
}

[Register]
public sealed partial class ContentReplacementService : IContentReplacementService
{
    private readonly IToolResultFileService _fileService;
    [Inject] private readonly ILogger<ContentReplacementService>? _logger;
    private readonly bool _enabled;
    private readonly int _maxToolResultsPerMessageChars;

    /// <summary>
    /// DI 自动解析构造函数 — IOptions&lt;QueryEngineConfig&gt; 可选
    /// 未注册时使用 ContentReplacementConfig 默认值（enabled=true, 200000 chars）
    /// </summary>
    public ContentReplacementService(
        IToolResultFileService fileService,
        IOptions<Configuration.QueryEngineConfig>? configOptions = null,
        ILogger<ContentReplacementService>? logger = null)
    {
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _logger = logger;

        var crConfig = configOptions?.Value?.ContentReplacement ?? new Configuration.ContentReplacementConfig();
        _enabled = crConfig.Enabled;
        _maxToolResultsPerMessageChars = crConfig.MaxToolResultsPerMessageChars;
    }

    /// <summary>
    /// 对齐 TS maybePersistLargeToolResult — 纯函数，不修改任何 state
    /// 仅检查内容大小和工具阈值，超限时持久化到磁盘并返回替换字符串
    /// </summary>
    public string? MaybePersistLargeToolResult(
        string toolName,
        string toolUseId,
        string content,
        string sessionId)
    {
        // 对齐 TS isToolResultContentEmpty — 空结果替换为标记文本
        // inc-4586: 空 tool_result 在 prompt 尾部会导致某些模型发出停止序列
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Format(ContentReplacementConstants.NoOutputTemplate, toolName);
        }

        // 对齐 TS getPersistenceThreshold — 工具级阈值
        var threshold = ContentReplacementConstants.GetPersistenceThreshold(toolName);

        // Infinity(-1) = 永不持久化（如 Read，防止 Read→file→Read 循环）
        if (threshold < 0)
            return null;

        if (content.Length <= threshold)
        {
            return null;
        }

        // 对齐 TS: maybePersistLargeToolResult 不检查 seenIds
        // seenIds 属于 budget 机制的状态，maybePersist 是独立的 per-tool 即时持久化
        // 两者独立运行，不应耦合

        // 对齐 TS: 持久化失败时返回 null（保留原始内容）
        PersistedToolResult? persisted = null;
        try
        {
            persisted = _fileService.PersistToolResult(sessionId, toolUseId, content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist tool result: Tool={ToolName}, Id={ToolUseId}", toolName, toolUseId);
            return null;
        }

        var replacement = ContentReplacementConstants.BuildPersistedOutputMessage(persisted);

        // 对齐 TS: maybePersistLargeToolResult 是纯函数，不修改 state
        // state 的修改仅由 ApplyToolResultBudget (enforceToolResultBudget) 负责
        // 调用方如需同步 state，应自行处理

        _logger?.LogDebug("Persisted large tool result: Tool={ToolName}, Id={ToolUseId}, Size={Size}, File={Filepath}",
            toolName, toolUseId, content.Length, persisted.Filepath);

        return replacement;
    }

    public async Task<(IReadOnlyList<ApiMessage> Messages, IReadOnlyList<ContentReplacementRecord> NewlyReplaced)> ApplyToolResultBudgetAsync(
        IReadOnlyList<ApiMessage> messages,
        ContentReplacementState state,
        string sessionId,
        HashSet<string>? neverPersistTools = null,
        CancellationToken cancellationToken = default)
    {
        neverPersistTools ??= [];

        // 对齐 TS skipToolNames — 自动添加 Infinity 工具（如 Read）
        // TS: query.ts L391 过滤 maxResultSizeChars: Infinity 的工具
        // 注意: 不修改调用者传入的集合，创建新集合
        var effectiveNeverPersist = new HashSet<string>(neverPersistTools, StringComparer.Ordinal);
        foreach (var name in ContentReplacementConstants.GetNeverPersistToolNames())
        {
            effectiveNeverPersist.Add(name);
        }

        // 1. 按 user message 分组 — 对齐 TS collectCandidatesByMessage
        var groups = CollectCandidatesByMessage(messages);

        // 2. 构建 toolCallId → toolName 映射 — 对齐 TS: 仅在 skipToolNames 非空时构建
        // TS: const nameByToolUseId = skipToolNames.size > 0 ? buildToolNameMap(messages) : undefined
        var toolNameMap = effectiveNeverPersist.Count > 0
            ? BuildToolNameMap(messages)
            : null;

        // 3. 对每组执行预算检查，收集需要持久化的候选
        var replacementMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var newlyReplaced = new List<ContentReplacementRecord>();
        var budget = _maxToolResultsPerMessageChars;
        var toPersistAll = new List<(string ToolCallId, string Content)>();

        foreach (var group in groups)
        {
            ProcessGroup(group, state, effectiveNeverPersist, toolNameMap, budget, replacementMap, toPersistAll);
        }

        // 4. 并发持久化 — 对齐 TS: await Promise.all(toPersist.map(async c => ...))
        if (toPersistAll.Count > 0)
        {
            var persistResults = await Task.WhenAll(
                toPersistAll.Select(async c =>
                {
                    try
                    {
                        var persisted = await _fileService.PersistToolResultAsync(sessionId, c.ToolCallId, c.Content, cancellationToken).ConfigureAwait(false);
                        var replacement = ContentReplacementConstants.BuildPersistedOutputMessage(persisted);

                        // 对齐 TS: 标记 seen 与 replacements 同步，保证并发读者不会看到 X∈seenIds 但 X∉replacements
                        state.Replacements[c.ToolCallId] = replacement;
                        state.SeenIds.TryAdd(c.ToolCallId, 0);
                        replacementMap[c.ToolCallId] = replacement;

                        _logger?.LogDebug("Budget-persisted tool result: Id={ToolUseId}, Size={Size}, File={Filepath}",
                            c.ToolCallId, c.Content.Length, persisted.Filepath);

                        return (Success: true, ToolCallId: c.ToolCallId, Replacement: replacement);
                    }
                    catch (Exception ex)
                    {
                        // 对齐 TS: 持久化失败时跳过，保留原始内容
                        _logger?.LogWarning(ex, "Failed to budget-persist tool result: Id={ToolUseId}", c.ToolCallId);
                        state.SeenIds.TryAdd(c.ToolCallId, 0); // 标记为 seen 但不替换
                        return (Success: false, ToolCallId: c.ToolCallId, Replacement: (string?)null);
                    }
                })).ConfigureAwait(false);

            foreach (var r in persistResults)
            {
                if (r.Success && r.Replacement is not null)
                {
                    newlyReplaced.Add(new ContentReplacementRecord
                    {
                        Kind = ContentReplacementRecordKind.ToolResult,
                        ToolUseId = r.ToolCallId,
                        Replacement = r.Replacement,
                    });
                }
            }
        }

        // 4. 无替换时返回原始消息
        if (replacementMap.Count == 0)
        {
            // 仍需 re-apply 已有的 replacements
            var needsReapply = messages.Any(m =>
                m.Role == MessageRole.Tool &&
                m.ExtractToolCallId() is { } id &&
                state.Replacements.ContainsKey(id));

            if (!needsReapply)
                return (messages, newlyReplaced);
        }

        // 5. 替换消息内容
        var result = new List<ApiMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var toolCallId = msg.ExtractToolCallId();
            if (msg.Role == MessageRole.Tool && toolCallId is not null && replacementMap.TryGetValue(toolCallId, out var replacement))
            {
                result.Add(new ApiMessage(MessageRole.Tool, replacement, msg.Metadata));
            }
            else if (msg.Role == MessageRole.Tool && toolCallId is not null && state.Replacements.TryGetValue(toolCallId, out var existingReplacement))
            {
                result.Add(new ApiMessage(MessageRole.Tool, existingReplacement, msg.Metadata));
            }
            else
            {
                result.Add(msg);
            }
        }

        return (result, newlyReplaced);
    }

    /// <summary>
    /// 按 user message 分组 — 对齐 TS collectCandidatesByMessage
    /// assistant 消息创建组边界，连续的 user/tool 消息属于同一组
    /// seenAsstIds: 同一 assistant ID 的片段不创建新边界（abort/parallel-tools 场景）
    /// </summary>
    private static List<List<(int Index, string ToolCallId, string Content, string ToolName)>> CollectCandidatesByMessage(
        IReadOnlyList<ApiMessage> messages)
    {
        var groups = new List<List<(int Index, string ToolCallId, string Content, string ToolName)>>();
        var current = new List<(int Index, string ToolCallId, string Content, string ToolName)>();

        // 对齐 TS seenAsstIds — 同一 assistant ID 重复出现时不创建新边界
        // 场景: abort/hook-stop 导致 [asst(X), user(trA), asst(X), user(trB)]
        // normalizeMessagesForAPI 会合并同 ID 片段，预算也必须视为同一组
        var seenAsstIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role == MessageRole.Assistant)
            {
                // 提取 assistant 消息 ID（从 ToolCalls[0].Id 或生成基于索引的 ID）
                var asstId = ExtractAssistantMessageId(msg, i);
                if (!seenAsstIds.Contains(asstId))
                {
                    // 新的 assistant 消息 → 刷新组
                    if (current.Count > 0)
                    {
                        groups.Add(current);
                        current = new();
                    }
                    seenAsstIds.Add(asstId);
                }
                // 同 ID 的 assistant 片段不创建新边界
                continue;
            }

            if (msg.Role == MessageRole.Tool)
            {
                var toolCallId = msg.ExtractToolCallId();
                if (toolCallId is not null && !string.IsNullOrEmpty(msg.Content))
                {
                    // 对齐 TS isContentAlreadyCompacted — 已被 persisted-output 替换的内容不再作为 candidate
                    if (IsContentAlreadyCompacted(msg.Content))
                        continue;

                    // 对齐 TS hasImageBlock — 仅跳过包含图片的 tool result
                    // TS: content.some(b => typeof b === 'object' && 'type' in b && b.type === 'image')
                    // 不跳过含 tool_reference 等非图片类型的合法 budget candidate
                    if (msg.ContentBlocks is not null && msg.ContentBlocks.Any(b => b.Type == ToolContentType.Image))
                        continue;

                    var toolName = msg.ExtractToolName() ?? string.Empty;
                    current.Add((i, toolCallId, msg.Content, toolName));
                }
            }
            // user/system 消息不创建边界
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups;
    }

    /// <summary>
    /// 提取 assistant 消息的唯一标识 — 对齐 TS message.message.id
    /// 优先使用 ToolCalls[0].Id，否则使用索引作为后备
    /// </summary>
    private static string ExtractAssistantMessageId(ApiMessage msg, int fallbackIndex)
    {
        foreach (var (id, _) in msg.ExtractToolCalls())
        {
            if (!string.IsNullOrEmpty(id))
                return $"asst:{id}";
        }

        // 无 ToolCalls 的 assistant 消息（纯文本回复）使用索引
        return $"asst:idx:{fallbackIndex}";
    }

    /// <summary>
    /// 处理单个消息组 — 对齐 TS enforceToolResultBudget 对每组的处理
    /// 不执行持久化，仅收集需要持久化的候选到 toPersistAll
    /// </summary>
    private static void ProcessGroup(
        List<(int Index, string ToolCallId, string Content, string ToolName)> candidates,
        ContentReplacementState state,
        HashSet<string> neverPersistTools,
        Dictionary<string, string>? toolNameMap,
        int budget,
        Dictionary<string, string> replacementMap,
        List<(string ToolCallId, string Content)> toPersistAll)
    {
        // 三分区: mustReapply / frozen / fresh — 对齐 TS partitionByPriorDecision
        var mustReapply = new List<(string ToolCallId, string Replacement)>();
        var frozenSize = 0;
        var fresh = new List<(string ToolCallId, string Content, string ToolName, int Size)>();

        foreach (var c in candidates)
        {
            if (state.Replacements.TryGetValue(c.ToolCallId, out var replacement))
            {
                // mustReapply: 之前已被替换，必须重新应用相同字符串
                mustReapply.Add((c.ToolCallId, replacement));
                replacementMap[c.ToolCallId] = replacement;
                frozenSize += replacement.Length;
            }
            else if (state.SeenIds.ContainsKey(c.ToolCallId))
            {
                // frozen: 之前已见但未替换，不可触碰
                frozenSize += c.Content.Length;
            }
            else
            {
                // fresh: 首次出现，可做决策
                fresh.Add((c.ToolCallId, c.Content, c.ToolName, c.Content.Length));
            }
        }

        if (fresh.Count == 0)
        {
            // 对齐 TS: mustReapply/frozen 的 ID 已在 seenIds 中，重新添加是幂等操作
            foreach (var c in candidates)
                state.SeenIds.TryAdd(c.ToolCallId, 0);
            return;
        }

        // 过滤 neverPersistTools — 对齐 TS skipToolNames
        var eligible = new List<(string ToolCallId, string Content, string ToolName, int Size)>();
        foreach (var f in fresh)
        {
            var effectiveToolName = f.ToolName;
            if (toolNameMap is not null && toolNameMap.TryGetValue(f.ToolCallId, out var mappedName))
                effectiveToolName = mappedName;

            if (neverPersistTools.Contains(effectiveToolName))
            {
                // 跳过的工具立即标记为 seen（frozen），不计入 freshSize
                state.SeenIds.TryAdd(f.ToolCallId, 0);
                continue;
            }

            eligible.Add(f);
        }

        var freshSize = eligible.Sum(f => f.Size);

        // 检查是否需要持久化 — 对齐 TS: frozenSize + freshSize > limit
        if (frozenSize + freshSize <= budget)
        {
            // 未超预算，所有 eligible 标记为 seen（frozen）
            foreach (var f in eligible)
                state.SeenIds.TryAdd(f.ToolCallId, 0);
            return;
        }

        // 选择要持久化的结果 — 对齐 TS selectFreshToReplace: 按大小降序贪心选择
        var sorted = eligible.OrderByDescending(f => f.Size).ToList();
        var remaining = frozenSize + freshSize;
        var toPersist = new List<(string ToolCallId, string Content)>();

        foreach (var f in sorted)
        {
            if (remaining <= budget) break;
            toPersist.Add((f.ToolCallId, f.Content));
            // 对齐 TS: 减去全量大小，不加预览大小
            // TS 注释: "previews are ~2K and results hitting this path are much larger,
            // so subtracting the full size is a close approximation for selection purposes"
            remaining -= f.Size;
        }

        // 对齐 TS 原子性: 先标记非选中的 candidate 为 seen
        // 非选中 = 所有 candidates 中不在 toPersist 中的
        var selectedIds = new HashSet<string>(
            toPersist.Select(p => p.ToolCallId), StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (!selectedIds.Contains(c.ToolCallId))
                state.SeenIds.TryAdd(c.ToolCallId, 0);
        }

        // 收集需要持久化的候选 — 持久化由调用方并发执行
        // 对齐 TS: toPersist.push(...selected)
        toPersistAll.AddRange(toPersist);
    }

    /// <summary>
    /// 构建 toolCallId → toolName 映射 — 对齐 TS buildToolNameMap
    /// 从 assistant 消息的 tool_use blocks 提取，而非 tool 消息的 Metadata
    /// tool_use 总在 tool_result 之前，所以到预算检查时名称已知
    /// 使用 ApiMessageExtensions.ExtractToolCalls 统一提取
    /// </summary>
    private static Dictionary<string, string> BuildToolNameMap(IReadOnlyList<ApiMessage> messages)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var msg in messages)
        {
            if (msg.Role != MessageRole.Assistant)
                continue;

            foreach (var (id, name) in msg.ExtractToolCalls())
            {
                map[id] = name;
            }
        }
        return map;
    }

    public ContentReplacementState ReconstructState(IReadOnlyList<ContentReplacementRecord> records)
    {
        var state = new ContentReplacementState();
        foreach (var record in records)
        {
            // 对齐 TS: 仅处理 kind=ToolResult 的记录
            if (record.Kind != ContentReplacementRecordKind.ToolResult)
                continue;
            state.SeenIds.TryAdd(record.ToolUseId, 0);
            state.Replacements[record.ToolUseId] = record.Replacement;
        }
        return state;
    }

    /// <summary>
    /// 对齐 TS reconstructContentReplacementState — 从消息历史提取 candidate IDs，
    /// 从记录恢复 replacements，从 inheritedReplacements 继承缺失的替换
    /// </summary>
    public ContentReplacementState ReconstructState(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlyList<ContentReplacementRecord> records,
        IReadOnlyDictionary<string, string>? inheritedReplacements = null)
    {
        var state = new ContentReplacementState();

        // 1. 从消息历史提取所有 candidate IDs — 对齐 TS collectCandidatesByMessage
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = CollectCandidatesByMessage(messages);
        foreach (var group in groups)
        {
            foreach (var c in group)
                candidateIds.Add(c.ToolCallId);
        }

        // 2. 将所有 candidate IDs 添加到 seenIds
        foreach (var id in candidateIds)
            state.SeenIds.TryAdd(id, 0);

        // 3. 从 records 恢复 replacements（仅当 kind=ToolResult 且 toolUseId 在 candidateIds 中）
        // 对齐 TS: r.kind === 'tool-result' && candidateIds.has(r.toolUseId)
        foreach (var record in records)
        {
            if (record.Kind == ContentReplacementRecordKind.ToolResult &&
                candidateIds.Contains(record.ToolUseId))
            {
                state.Replacements[record.ToolUseId] = record.Replacement;
            }
        }

        // 4. 从 inheritedReplacements 继承（仅当 id 在 candidateIds 中且没有已有 replacement）
        if (inheritedReplacements is not null)
        {
            foreach (var kvp in inheritedReplacements)
            {
                if (candidateIds.Contains(kvp.Key) && !state.Replacements.ContainsKey(kvp.Key))
                {
                    state.Replacements[kvp.Key] = kvp.Value;
                }
            }
        }

        return state;
    }

    /// <summary>
    /// 对齐 TS provisionContentReplacementState — 功能开关入口
    /// 功能开关关闭时返回 null（query 会跳过整个预算执行）
    /// 有初始消息时走重建路径（保证恢复会话时 prompt cache 一致性）
    /// 无初始消息时走新建路径
    /// </summary>
    public ContentReplacementState? ProvisionContentReplacementState(
        IReadOnlyList<ApiMessage>? initialMessages = null,
        IReadOnlyList<ContentReplacementRecord>? initialContentReplacements = null)
    {
        // 对齐 TS getFeatureValue_CACHED_MAY_BE_STALE('tengu_hawthorn_steeple', false)
        // C# 通过 ContentReplacementConfig.Enabled 配置，默认启用
        if (!_enabled)
            return null;

        if (initialMessages is not null && initialMessages.Count > 0)
        {
            return ReconstructState(
                initialMessages,
                initialContentReplacements ?? []);
        }

        return new ContentReplacementState();
    }

    /// <summary>
    /// 对齐 TS reconstructForSubagentResume — 子智能体恢复重建
    /// 传入父级 state 的 replacements 作为继承替换源
    /// 父级状态为 null 时直接返回 null（功能开关关闭）
    /// </summary>
    public ContentReplacementState? ReconstructForSubagentResume(
        ContentReplacementState? parentState,
        IReadOnlyList<ApiMessage> resumedMessages,
        IReadOnlyList<ContentReplacementRecord> sidechainRecords)
    {
        if (parentState is null)
            return null;

        // 将父级 replacements 转为 IReadOnlyDictionary 传入 ReconstructState
        // 父级已有的替换决策会填充 sidechain 记录中没有覆盖到的条目
        return ReconstructState(
            resumedMessages,
            sidechainRecords,
            parentState.Replacements);
    }

    /// <summary>
    /// 对齐 TS isContentAlreadyCompacted — 检测内容是否已被 persisted-output 或截断标记替换
    /// 已替换的内容不应再作为 budget candidate，防止重复持久化
    /// 使用 StartsWith 而非 Contains — 对齐 TS 注释:
    /// "avoids false-positives when the tag appears anywhere else in the content"
    /// </summary>
    private static bool IsContentAlreadyCompacted(string content)
    {
        // 检测 <persisted-output> 标签 — MaybePersistLargeToolResult 的替换产物
        // TS: content.startsWith("<persisted-output>") — 只在开头匹配
        if (content.StartsWith(ContentReplacementConstants.PersistedOutputOpen, StringComparison.Ordinal))
            return true;

        // 检测截断标记 — ToolResultTruncator 的替换产物
        if (content.StartsWith(ContentReplacementConstants.TruncatedPrefix, StringComparison.Ordinal))
            return true;

        return false;
    }
}
