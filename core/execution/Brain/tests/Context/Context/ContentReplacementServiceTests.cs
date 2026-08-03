namespace Brain.Tests.Context;

/// <summary>
/// ContentReplacementService 单元测试 — 对齐 TS enforceToolResultBudget + maybePersistLargeToolResult
/// TDD 红测试: per-message 预算机制
/// </summary>
public sealed class ContentReplacementServiceTests
{
    /// <summary>
    /// 红测试: 单个工具结果超过 per-message 预算时应被持久化
    /// 对齐 TS: enforceToolResultBudget — frozenSize + freshSize > limit 时选择最大结果持久化
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_SingleToolExceedsBudget_PersistsResult()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        // 创建一条 user 消息 + 一条超大 tool 消息
        var largeContent = new string('x', 250_000); // 250K，超过 200K 预算
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", largeContent),
        };

        var (result, newlyReplaced) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);

        // 应该有一条 tool 消息被替换为 persisted-output 预览
        var toolMsg = result.FirstOrDefault(m => m.Role == MessageRole.Tool);
        toolMsg.Should().NotBeNull();
        toolMsg!.Content.Should().Contain("<persisted-output>");
        toolMsg.Content.Should().Contain("</persisted-output>");

        // state 应该记录了 seenId 和 replacement
        state.SeenIds.Keys.Should().Contain("tool_1");
        state.Replacements.Should().ContainKey("tool_1");

        // newlyReplaced 应包含一条记录
        newlyReplaced.Should().ContainSingle(r => r.ToolUseId == "tool_1");
    }

    /// <summary>
    /// 红测试: 多个并行工具结果总和超过 per-message 预算时，最大的应被持久化
    /// 对齐 TS: selectFreshToReplace — 按大小降序贪心选择
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_MultipleToolsExceedBudget_PersistsLargest()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        // 3 个工具结果，每个 80K，总计 240K > 200K 预算
        var content80K = new string('a', 80_000);
        var content80K2 = new string('b', 80_000);
        var content80K3 = new string('c', 80_000);

        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", content80K),
            CreateToolMessage("tool_2", content80K2),
            CreateToolMessage("tool_3", content80K3),
        };

        var (result, _) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);

        // 至少有一个工具结果应该被持久化（最大的那个）
        var persistedCount = result.Count(m => m.Role == MessageRole.Tool && m.Content?.Contains("<persisted-output>") == true);
        persistedCount.Should().BeGreaterThanOrEqualTo(1);

        // state 应该记录了所有 seenIds
        state.SeenIds.Keys.Should().Contain("tool_1");
        state.SeenIds.Keys.Should().Contain("tool_2");
        state.SeenIds.Keys.Should().Contain("tool_3");
    }

    /// <summary>
    /// 红测试: 工具结果总和在预算内时，不应持久化任何结果
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_WithinBudget_NoPersistence()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        // 2 个工具结果，每个 50K，总计 100K < 200K 预算
        var content50K = new string('a', 50_000);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", content50K),
            CreateToolMessage("tool_2", content50K),
        };

        var (result, newlyReplaced) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);

        // 不应有任何持久化
        var persistedCount = result.Count(m => m.Role == MessageRole.Tool && m.Content?.Contains("<persisted-output>") == true);
        persistedCount.Should().Be(0);

        // newlyReplaced 应为空
        newlyReplaced.Should().BeEmpty();

        // 但 seenIds 应该记录
        state.SeenIds.Keys.Should().Contain("tool_1");
        state.SeenIds.Keys.Should().Contain("tool_2");
    }

    /// <summary>
    /// 红测试: 已持久化的结果在后续调用中应被重新应用（prompt cache 稳定性）
    /// 对齐 TS: mustReapply — 从 state.replacements 取缓存字符串
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_AlreadyReplaced_ReappliesSameReplacement()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        var largeContent = new string('x', 250_000);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", largeContent),
        };

        // 第一次调用: 持久化
        var (result1, newlyReplaced1) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);
        var firstReplacement = result1.First(m => m.Role == MessageRole.Tool).Content;

        // newlyReplaced 应有记录
        newlyReplaced1.Should().ContainSingle(r => r.ToolUseId == "tool_1");

        // 第二次调用: 应重新应用相同的替换字符串
        var (result2, newlyReplaced2) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);
        var secondReplacement = result2.First(m => m.Role == MessageRole.Tool).Content;

        secondReplacement.Should().Be(firstReplacement);

        // 第二次调用是 mustReapply，不应产生新的 newlyReplaced
        newlyReplaced2.Should().BeEmpty();
    }

    /// <summary>
    /// 红测试: neverPersistTools 中的工具不应被持久化
    /// 对齐 TS: skipToolNames — maxResultSizeChars: Infinity 的工具跳过
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_NeverPersistTools_SkipsSpecifiedTools()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        var largeContent = new string('x', 250_000);
        var neverPersist = new HashSet<string> { "Read" };

        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", largeContent, toolName: "Read"),
        };

        var (result, newlyReplaced) = await service.ApplyToolResultBudgetAsync(messages, state, "session1", neverPersist).ConfigureAwait(true);

        // Read 工具不应被持久化
        var toolMsg = result.First(m => m.Role == MessageRole.Tool);
        toolMsg.Content.Should().NotContain("<persisted-output>");

        // 但应该被标记为 seen（frozen）
        state.SeenIds.Keys.Should().Contain("tool_1");

        // newlyReplaced 应为空
        newlyReplaced.Should().BeEmpty();
    }

    /// <summary>
    /// 红测试: 不同 user message 的工具结果应独立计算预算
    /// 对齐 TS: collectCandidatesByMessage — 按 user message 分组
    /// </summary>
    [Fact]
    public async Task ApplyToolResultBudget_DifferentUserMessages_IndependentBudgets()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();
        var state = new ContentReplacementState();

        var content120K = new string('a', 120_000);

        // 两条 user 消息，各有一条 120K 的 tool 结果
        // 每条 user 消息的预算独立，120K < 200K，不应持久化
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "query 1"),
            CreateToolMessage("tool_1", content120K),
            new(MessageRole.Assistant, "response 1"),
            new(MessageRole.User, "query 2"),
            CreateToolMessage("tool_2", content120K),
        };

        var (result, newlyReplaced) = await service.ApplyToolResultBudgetAsync(messages, state, "session1").ConfigureAwait(true);

        // 每个 user message 组的 120K < 200K，不应持久化
        var persistedCount = result.Count(m => m.Role == MessageRole.Tool && m.Content?.Contains("<persisted-output>") == true);
        persistedCount.Should().Be(0);

        // newlyReplaced 应为空
        newlyReplaced.Should().BeEmpty();
    }

    /// <summary>
    /// 测试: ProvisionContentReplacementState 冷启动时返回新状态
    /// 对齐 TS: REPL.tsx useState(() => provisionContentReplacementState())
    /// </summary>
    [Fact]
    public void ProvisionContentReplacementState_ColdStart_ReturnsNewState()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var state = service.ProvisionContentReplacementState();

        state.Should().NotBeNull();
        state!.SeenIds.Should().BeEmpty();
        state.Replacements.Should().BeEmpty();
    }

    /// <summary>
    /// 测试: ProvisionContentReplacementState 有初始消息时走重建路径
    /// 对齐 TS: initialMessages 非空时调用 reconstructContentReplacementState
    /// </summary>
    [Fact]
    public async Task ProvisionContentReplacementState_WithMessages_ReconstructsState()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var largeContent = new string('x', 250_000);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", largeContent),
        };

        // 先做一次预算检查，产生 replacement
        var state1 = new ContentReplacementState();
        var (_, newlyReplaced) = await service.ApplyToolResultBudgetAsync(messages, state1, "session1").ConfigureAwait(true);
        newlyReplaced.Should().NotBeEmpty();

        // 用原始消息 + 记录重建状态（传入原始消息，非 budgeted 消息）
        // 对齐 TS: provisionContentReplacementState(initialMessages, initialContentReplacements)
        var state2 = service.ProvisionContentReplacementState(messages, newlyReplaced);

        state2.Should().NotBeNull();
        state2!.SeenIds.Keys.Should().Contain("tool_1");
        state2.Replacements.Should().ContainKey("tool_1");
    }

    /// <summary>
    /// 测试: ReconstructForSubagentResume 父级状态为 null 时返回 null
    /// 对齐 TS: parentState 为 undefined 时直接返回 undefined
    /// </summary>
    [Fact]
    public void ReconstructForSubagentResume_NullParent_ReturnsNull()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var messages = new List<ApiMessage> { new(MessageRole.User, "test") };

        var result = service.ReconstructForSubagentResume(null, messages, []);

        result.Should().BeNull();
    }

    /// <summary>
    /// 测试: ReconstructForSubagentResume 从父级状态继承替换
    /// 对齐 TS: resumeAgent.ts — parentState.replacements 作为 inheritedReplacements
    /// </summary>
    [Fact]
    public void ReconstructForSubagentResume_WithParent_InheritsReplacements()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        // 构建父级状态
        var parentState = new ContentReplacementState();
        parentState.Replacements["tool_1"] = "<persisted-output>parent replacement</persisted-output>";
        parentState.SeenIds.TryAdd("tool_1", 0);

        // 子智能体恢复的消息包含 tool_1
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "test query"),
            CreateToolMessage("tool_1", "original content"),
        };

        var result = service.ReconstructForSubagentResume(parentState, messages, []);

        result.Should().NotBeNull();
        result!.Replacements.Should().ContainKey("tool_1");
        result.Replacements["tool_1"].Should().Be("<persisted-output>parent replacement</persisted-output>");
        result.SeenIds.Keys.Should().Contain("tool_1");
    }

    /// <summary>
    /// 测试: ReconstructState(消息+记录+继承) 完整路径
    /// 对齐 TS: reconstructContentReplacementState — 4步重建
    /// </summary>
    [Fact]
    public void ReconstructState_WithMessagesAndRecords_RebuildsCorrectly()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "query"),
            CreateToolMessage("tool_1", "content1"),
            CreateToolMessage("tool_2", "content2"),
        };

        var records = new List<ContentReplacementRecord>
        {
            new()
            {
                Kind = ContentReplacementRecordKind.ToolResult,
                ToolUseId = "tool_1",
                Replacement = "<persisted-output>replaced1</persisted-output>",
            },
        };

        var inherited = new Dictionary<string, string>
        {
            ["tool_2"] = "<persisted-output>inherited2</persisted-output>",
        };

        var state = service.ReconstructState(messages, records, inherited);

        // tool_1 从 records 恢复
        state.Replacements.Should().ContainKey("tool_1");
        state.Replacements["tool_1"].Should().Be("<persisted-output>replaced1</persisted-output>");

        // tool_2 从 inherited 继承（records 中没有 tool_2 的替换）
        state.Replacements.Should().ContainKey("tool_2");
        state.Replacements["tool_2"].Should().Be("<persisted-output>inherited2</persisted-output>");

        // 所有 candidate IDs 应在 seenIds 中
        state.SeenIds.Keys.Should().Contain("tool_1");
        state.SeenIds.Keys.Should().Contain("tool_2");
    }

    /// <summary>
    /// 测试: ReconstructState 仅记录时过滤非 ToolResult 类型
    /// 对齐 TS: r.kind === 'tool-result' 过滤
    /// </summary>
    [Fact]
    public void ReconstructState_RecordsOnly_FiltersByKind()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var records = new List<ContentReplacementRecord>
        {
            new()
            {
                Kind = ContentReplacementRecordKind.ToolResult,
                ToolUseId = "tool_1",
                Replacement = "replaced1",
            },
        };

        var state = service.ReconstructState(records);

        state.SeenIds.Keys.Should().Contain("tool_1");
        state.Replacements.Should().ContainKey("tool_1");
    }

    /// <summary>
    /// 测试: MaybePersistLargeToolResult 空内容返回 NoOutputTemplate
    /// 对齐 TS: isToolResultContentEmpty — 空结果替换为标记文本
    /// </summary>
    [Fact]
    public void MaybePersistLargeToolResult_EmptyContent_ReturnsNoOutputTemplate()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var result = service.MaybePersistLargeToolResult("TestTool", "id1", "", "session1");

        result.Should().NotBeNull();
        result.Should().Contain("TestTool");
    }

    /// <summary>
    /// 测试: MaybePersistLargeToolResult 内容在阈值内返回 null
    /// 对齐 TS: content.Length <= threshold → 不持久化
    /// </summary>
    [Fact]
    public void MaybePersistLargeToolResult_BelowThreshold_ReturnsNull()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var smallContent = new string('a', 1000);
        var result = service.MaybePersistLargeToolResult("TestTool", "id1", smallContent, "session1");

        result.Should().BeNull();
    }

    /// <summary>
    /// 测试: MaybePersistLargeToolResult 超大内容返回替换字符串
    /// 对齐 TS: content.Length > threshold → 持久化并返回 persisted-output
    /// </summary>
    [Fact]
    public void MaybePersistLargeToolResult_ExceedsThreshold_ReturnsReplacement()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        var largeContent = new string('x', 250_000);
        var result = service.MaybePersistLargeToolResult("TestTool", "id1", largeContent, "session1");

        result.Should().NotBeNull();
        result.Should().Contain("<persisted-output>");
        result.Should().Contain("</persisted-output>");
    }

    /// <summary>
    /// 测试: ProvisionContentReplacementState 功能关闭时返回 null
    /// 对齐 TS: getFeatureValue_CACHED_MAY_BE_STALE('tengu_hawthorn_steeple', false) → undefined
    /// </summary>
    [Fact]
    public void ProvisionContentReplacementState_FeatureDisabled_ReturnsNull()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService(enabled: false);

        var state = service.ProvisionContentReplacementState();

        state.Should().BeNull();
    }

    /// <summary>
    /// 测试: MaybePersistLargeToolResult Infinity 阈值工具返回 null
    /// 对齐 TS: getPersistenceThreshold 返回 Infinity(-1) 的工具永不持久化
    /// </summary>
    [Fact]
    public void MaybePersistLargeToolResult_NeverPersistTool_ReturnsNull()
    {
        var fileService = new MockToolResultFileService();
        var service = CreateService();

        // Read 工具的阈值为 -1 (Infinity)，永不持久化
        var largeContent = new string('x', 250_000);
        var result = service.MaybePersistLargeToolResult("Read", "id1", largeContent, "session1");

        result.Should().BeNull();
    }

    private static ContentReplacementService CreateService(
        bool enabled = true,
        int maxToolResultsPerMessageChars = 200000)
    {
        var fileService = new MockToolResultFileService();
        var config = new Core.Configuration.QueryEngineConfig
        {
            ContentReplacement = new()
            {
                Enabled = enabled,
                MaxToolResultsPerMessageChars = maxToolResultsPerMessageChars,
            }
        };
        return new ContentReplacementService(
            fileService,
            Microsoft.Extensions.Options.Options.Create(config));
    }

    private static ApiMessage CreateToolMessage(string toolCallId, string content, string toolName = "TestTool")
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["ToolCallId"] = JsonSerializer.SerializeToElement(toolCallId),
            ["ToolName"] = JsonSerializer.SerializeToElement(toolName),
        };
        return new ApiMessage(MessageRole.Tool, content, metadata);
    }

    /// <summary>
    /// Mock IToolResultFileService — 模拟持久化到磁盘
    /// </summary>
    private sealed class MockToolResultFileService : IToolResultFileService
    {
        public PersistedToolResult PersistToolResult(string sessionId, string toolUseId, string content)
        {
            var (preview, hasMore) = GeneratePreview(content, ContentReplacementConstants.PreviewSizeChars);
            return new PersistedToolResult
            {
                Filepath = $"/mock/tool-results/{sessionId}/{SanitizeFilename(toolUseId)}.txt",
                OriginalSize = content.Length,
                IsJson = false,
                Preview = preview,
                HasMore = hasMore
            };
        }

        public Task<PersistedToolResult> PersistToolResultAsync(string sessionId, string toolUseId, string content, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PersistToolResult(sessionId, toolUseId, content));
        }

        public string? ReadToolResult(string sessionId, string toolUseId) => null;

        private static (string Preview, bool HasMore) GeneratePreview(string content, int maxChars)
        {
            if (content.Length <= maxChars) return (content, false);
            var truncated = content.AsSpan(0, maxChars);
            var lastNewline = truncated.LastIndexOf('\n');
            var cutPoint = lastNewline > maxChars / 2 ? lastNewline : maxChars;
            return (content.AsSpan(0, cutPoint).ToString(), true);
        }

        private static string SanitizeFilename(string id)
        {
            var chars = id.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
                    chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
