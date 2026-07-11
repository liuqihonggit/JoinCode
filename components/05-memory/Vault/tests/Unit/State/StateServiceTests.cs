
namespace Core.Tests.Services;

/// <summary>
/// StateService 单元测试 - 使用内存存储实现高速测试
/// </summary>
public sealed class StateServiceTests : IDisposable
{
    private readonly InMemoryStateService _stateService;

    public StateServiceTests()
    {
        _stateService = new InMemoryStateService();
    }

    public void Dispose()
    {
        _stateService.Dispose();
    }

    [Fact]
    public void SaveState_ShouldCreateState()
    {
        // Arrange
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");

        // Act
        _stateService.SaveState("Test system prompt", chatHistory);

        // Assert
        var (systemPrompt, loadedMessageList) = _stateService.LoadState();
        Assert.Equal("Test system prompt", systemPrompt);
        Assert.NotEmpty(loadedMessageList);
    }

    [Fact]
    public void LoadState_ExistingState_ShouldRestoreMessageList()
    {
        // Arrange
        var systemPrompt = "Test system prompt";
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        chatHistory.AddAssistantMessage("Test response");
        _stateService.SaveState(systemPrompt, chatHistory);

        // Act
        var (loadedSystemPrompt, loadedMessageList) = _stateService.LoadState();

        // Assert
        Assert.Equal(systemPrompt, loadedSystemPrompt);
        Assert.True(loadedMessageList.Count >= 2);
    }

    [Fact]
    public void LoadState_NonExistingState_ShouldReturnEmptyValues()
    {
        // Act
        var (systemPrompt, chatHistory) = _stateService.LoadState();

        // Assert
        Assert.Equal(string.Empty, systemPrompt);
        Assert.NotNull(chatHistory);
        Assert.Empty(chatHistory);
    }

    [Fact]
    public void ClearState_ExistingState_ShouldReturnTrue()
    {
        // Arrange
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        _stateService.SaveState("Test prompt", chatHistory);

        // Act
        var cleared = _stateService.ClearState();

        // Assert - ClearState 返回 true 表示成功删除了状态记录
        Assert.True(cleared);

        // 验证状态已被清除 - 重新加载应该返回空值
        var (systemPrompt, loadedMessageList) = _stateService.LoadState();
        Assert.Equal(string.Empty, systemPrompt);
        Assert.Empty(loadedMessageList);
    }

    [Fact]
    public void ClearState_NonExistingState_ShouldReturnFalse()
    {
        // Act
        var cleared = _stateService.ClearState();

        // Assert
        Assert.False(cleared);
    }

    [Fact]
    public async Task SaveStateAsync_ShouldCreateState()
    {
        // Arrange
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");

        // Act
        await _stateService.SaveStateAsync("Test system prompt", chatHistory).ConfigureAwait(true);

        // Assert
        var (systemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);
        Assert.Equal("Test system prompt", systemPrompt);
        Assert.NotEmpty(loadedMessageList);
    }

    [Fact]
    public async Task LoadStateAsync_ExistingState_ShouldRestoreMessageList()
    {
        // Arrange
        var systemPrompt = "Test system prompt";
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        await _stateService.SaveStateAsync(systemPrompt, chatHistory).ConfigureAwait(true);

        // Act
        var (loadedSystemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(systemPrompt, loadedSystemPrompt);
        Assert.NotNull(loadedMessageList);
    }

    [Fact]
    public async Task ClearStateAsync_ExistingState_ShouldReturnTrue()
    {
        // Arrange
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        await _stateService.SaveStateAsync("Test prompt", chatHistory).ConfigureAwait(true);

        // Act
        var cleared = await _stateService.ClearStateAsync().ConfigureAwait(true);

        // Assert - ClearStateAsync 返回 true 表示成功删除了状态记录
        Assert.True(cleared);

        // 验证状态已被清除 - 重新加载应该返回空值
        var (systemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);
        Assert.Equal(string.Empty, systemPrompt);
        Assert.Empty(loadedMessageList);
    }

    [Fact]
    public void SaveState_WithDifferentMessageTypes_ShouldPreserveAll()
    {
        // Arrange
        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage("System message");
        chatHistory.AddUserMessage("User message");
        chatHistory.AddAssistantMessage("Assistant message");

        // Act
        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert
        Assert.True(loadedMessageList.Count >= 3);
    }

    /// <summary>
    /// 红测试: 验证保存带 ToolCalls Metadata 的 Assistant 工具调用消息后，
    /// 加载能完整恢复 Metadata。当前实现丢失 Metadata，导致重启后前缀缓存破坏。
    /// </summary>
    [Fact]
    public void SaveState_WithToolCallMetadata_ShouldPreserveToolCalls()
    {
        // Arrange — 模拟 QueryLoopMiddleware 构建的工具调用 Assistant 消息
        var toolCalls = new List<ToolCallEntry>
        {
            new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"README.md\"}" }
        };
        var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "请读取 README.md"),
            new(MessageRole.Assistant, null, assistantMetadata)
        };

        // Act
        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 加载后应能恢复 ToolCalls Metadata
        Assert.True(loadedMessageList.Count >= 2);
        var loadedAssistant = loadedMessageList.FirstOrDefault(m => m.Role == MessageRole.Assistant);
        Assert.NotNull(loadedAssistant);
        Assert.NotNull(loadedAssistant.Metadata);

        var extractedCalls = loadedAssistant.ExtractToolCalls();
        Assert.NotEmpty(extractedCalls);
        Assert.Equal("call_001", extractedCalls[0].Id);
        Assert.Equal("Read", extractedCalls[0].Name);
    }

    /// <summary>
    /// 红测试: 验证保存带 ToolCallId/ToolName Metadata 的 Tool 结果消息后，
    /// 加载能完整恢复 Metadata。当前实现丢失 Metadata，导致重启后工具结果与工具调用失联。
    /// </summary>
    [Fact]
    public void SaveState_WithToolResultMetadata_ShouldPreserveToolCallIdAndName()
    {
        // Arrange — 模拟 QueryLoopMiddleware 构建的工具结果消息
        var toolMetadata = ToolCallEntry.BuildToolResultMetadata("call_001", "Read");
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "请读取 README.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])),
            new(MessageRole.Tool, "File content here", toolMetadata)
        };

        // Act
        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 加载后应能恢复 ToolCallId 和 ToolName
        var loadedTool = loadedMessageList.FirstOrDefault(m => m.Role == MessageRole.Tool);
        Assert.NotNull(loadedTool);
        Assert.NotNull(loadedTool.Metadata);

        Assert.Equal("call_001", loadedTool.ExtractToolCallId());
        Assert.Equal("Read", loadedTool.ExtractToolName());
    }

    /// <summary>
    /// 红测试: 验证多轮工具调用对话保存加载后，消息顺序和 Metadata 完整，
    /// 确保重启后发送给 LLM 的前缀与重启前一致（前缀缓存不失效）。
    /// </summary>
    [Fact]
    public void SaveLoad_WithMultiTurnToolConversation_ShouldPreserveOrderAndMetadata()
    {
        // Arrange — 模拟完整的两轮工具调用对话
        var chatHistory = new MessageList
        {
            // 第1轮
            new(MessageRole.User, "读取 README.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"README.md\"}" }])),
            new(MessageRole.Tool, "README content", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")),
            new(MessageRole.Assistant, "已读取 README.md"),
            // 第2轮
            new(MessageRole.User, "读取 CLAUDE.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_002", Name = "Read", Arguments = "{\"file_path\":\"CLAUDE.md\"}" }])),
            new(MessageRole.Tool, "CLAUDE content", ToolCallEntry.BuildToolResultMetadata("call_002", "Read")),
            new(MessageRole.Assistant, "已读取 CLAUDE.md")
        };

        // Act
        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 验证消息顺序
        Assert.Equal(8, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal(MessageRole.Tool, loadedMessageList[2].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[3].Role);
        Assert.Equal(MessageRole.User, loadedMessageList[4].Role);

        // Assert — 验证第1轮 ToolCalls Metadata
        var turn1Assistant = loadedMessageList[1];
        var turn1Calls = turn1Assistant.ExtractToolCalls();
        Assert.NotEmpty(turn1Calls);
        Assert.Equal("call_001", turn1Calls[0].Id);
        Assert.Equal("Read", turn1Calls[0].Name);

        // Assert — 验证第1轮 Tool 结果 Metadata
        var turn1Tool = loadedMessageList[2];
        Assert.Equal("call_001", turn1Tool.ExtractToolCallId());
        Assert.Equal("Read", turn1Tool.ExtractToolName());

        // Assert — 验证第2轮 ToolCalls Metadata
        var turn2Assistant = loadedMessageList[5];
        var turn2Calls = turn2Assistant.ExtractToolCalls();
        Assert.NotEmpty(turn2Calls);
        Assert.Equal("call_002", turn2Calls[0].Id);
        Assert.Equal("Read", turn2Calls[0].Name);

        // Assert — 验证第2轮 Tool 结果 Metadata
        var turn2Tool = loadedMessageList[6];
        Assert.Equal("call_002", turn2Tool.ExtractToolCallId());
        Assert.Equal("Read", turn2Tool.ExtractToolName());
    }

    /// <summary>
    /// 红测试: 验证 LoadState 对重复消息去重。
    /// 场景: 早期版本把 Tool 消息保存为 User 角色，导致同一内容出现两次（user + tool）。
    /// 期望: LoadState 保留 Tool 角色版本，移除 User 角色版本（基于内容去重，tool 优先）。
    /// </summary>
    [Fact]
    public void LoadState_WithDuplicateContentDifferentRole_ShouldDeduplicateKeepTool()
    {
        // Arrange — 模拟 db 中残留的早期错误数据：同一内容保存了 user 和 tool 两个版本
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "读取文件"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])),
            // 早期错误版本：工具结果保存为 user 角色
            new(MessageRole.User, "File content here"),
            // 后期正确版本：工具结果保存为 tool 角色
            new(MessageRole.Tool, "File content here", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")),
            new(MessageRole.Assistant, "已读取文件")
        };

        _stateService.SaveState("System prompt", chatHistory);

        // Act
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 去重后应该只有 4 条消息（user, assistant, tool, assistant）
        Assert.Equal(4, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal(MessageRole.Tool, loadedMessageList[2].Role);
        Assert.Equal("File content here", loadedMessageList[2].Content);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[3].Role);
    }

    /// <summary>
    /// 红测试: 验证 LoadState 对完全相同的消息（Role + Content）去重。
    /// 场景: 同一消息被保存两次（如重试场景）。
    /// 期望: LoadState 只保留一条。
    /// </summary>
    [Fact]
    public void LoadState_WithExactDuplicateMessages_ShouldDeduplicate()
    {
        // Arrange
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "Hello"),
            new(MessageRole.User, "Hello"),  // 完全重复
            new(MessageRole.Assistant, "Hi there"),
            new(MessageRole.Assistant, "Hi there")  // 完全重复
        };

        _stateService.SaveState("System prompt", chatHistory);

        // Act
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 去重后应该只有 2 条消息
        Assert.Equal(2, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal("Hello", loadedMessageList[0].Content);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal("Hi there", loadedMessageList[1].Content);
    }

    /// <summary>
    /// 验证同内容不同 tool_call_id 的 Tool 结果消息不被去重丢弃。
    /// 场景: 两次工具调用返回相同内容（如两个文件内容相同，或相同错误信息），
    /// 但 tool_call_id 不同。去重逻辑若仅基于 Content 丢弃第二条，会导致
    /// 孤立 tool_call（assistant 声明了 N 个 tool_call 但只有 N-1 条 tool 结果）→ API 400 + 永久数据丢失。
    /// </summary>
    [Fact]
    public void LoadState_WithSameContentDifferentToolCallId_ShouldPreserveBothToolResults()
    {
        // Arrange — 两个工具调用返回相同内容 "Hello World"，但 tool_call_id 不同
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "读取两个文件"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [
                    new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"a.txt\"}" },
                    new() { Id = "call_002", Name = "Read", Arguments = "{\"file_path\":\"b.txt\"}" }
                ])),
            new(MessageRole.Tool, "Hello World", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")),
            new(MessageRole.Tool, "Hello World", ToolCallEntry.BuildToolResultMetadata("call_002", "Read")),
        };

        _stateService.SaveState("System prompt", chatHistory);

        // Act
        var (_, loadedMessageList) = _stateService.LoadState();

        // Assert — 两条 Tool 结果都必须保留（不能因内容相同而丢弃第二条）
        var toolMessages = loadedMessageList.Where(m => m.Role == MessageRole.Tool).ToList();
        Assert.Equal(2, toolMessages.Count);
        Assert.Equal("call_001", toolMessages[0].ExtractToolCallId());
        Assert.Equal("call_002", toolMessages[1].ExtractToolCallId());
    }
}
