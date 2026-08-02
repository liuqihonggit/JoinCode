
namespace Core.Tests.Services;

/// <summary>
/// StateService 单元测试 — 纯内存实现，对齐 Claude Code 原版
/// </summary>
public sealed class StateServiceTests : IDisposable
{
    private readonly StateService _stateService;

    public StateServiceTests()
    {
        _stateService = new StateService(new SystemClockService());
    }

    public void Dispose()
    {
        _stateService.Dispose();
    }

    [Fact]
    public void SaveState_ShouldCreateState()
    {
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");

        _stateService.SaveState("Test system prompt", chatHistory);

        var (systemPrompt, loadedMessageList) = _stateService.LoadState();
        Assert.Equal("Test system prompt", systemPrompt);
        Assert.NotEmpty(loadedMessageList);
    }

    [Fact]
    public void LoadState_ExistingState_ShouldRestoreMessageList()
    {
        var systemPrompt = "Test system prompt";
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        chatHistory.AddAssistantMessage("Test response");
        _stateService.SaveState(systemPrompt, chatHistory);

        var (loadedSystemPrompt, loadedMessageList) = _stateService.LoadState();

        Assert.Equal(systemPrompt, loadedSystemPrompt);
        Assert.True(loadedMessageList.Count >= 2);
    }

    [Fact]
    public void LoadState_NonExistingState_ShouldReturnEmptyValues()
    {
        var (systemPrompt, chatHistory) = _stateService.LoadState();

        Assert.Equal(string.Empty, systemPrompt);
        Assert.NotNull(chatHistory);
        Assert.Empty(chatHistory);
    }

    [Fact]
    public void ClearState_ExistingState_ShouldReturnTrue()
    {
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        _stateService.SaveState("Test prompt", chatHistory);

        var cleared = _stateService.ClearState();

        Assert.True(cleared);

        var (systemPrompt, loadedMessageList) = _stateService.LoadState();
        Assert.Equal(string.Empty, systemPrompt);
        Assert.Empty(loadedMessageList);
    }

    [Fact]
    public void ClearState_NonExistingState_ShouldReturnFalse()
    {
        var cleared = _stateService.ClearState();

        Assert.False(cleared);
    }

    [Fact]
    public async Task SaveStateAsync_ShouldCreateState()
    {
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");

        await _stateService.SaveStateAsync("Test system prompt", chatHistory).ConfigureAwait(true);

        var (systemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);
        Assert.Equal("Test system prompt", systemPrompt);
        Assert.NotEmpty(loadedMessageList);
    }

    [Fact]
    public async Task LoadStateAsync_ExistingState_ShouldRestoreMessageList()
    {
        var systemPrompt = "Test system prompt";
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        await _stateService.SaveStateAsync(systemPrompt, chatHistory).ConfigureAwait(true);

        var (loadedSystemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);

        Assert.Equal(systemPrompt, loadedSystemPrompt);
        Assert.NotNull(loadedMessageList);
    }

    [Fact]
    public async Task ClearStateAsync_ExistingState_ShouldReturnTrue()
    {
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("Test message");
        await _stateService.SaveStateAsync("Test prompt", chatHistory).ConfigureAwait(true);

        var cleared = await _stateService.ClearStateAsync().ConfigureAwait(true);

        Assert.True(cleared);

        var (systemPrompt, loadedMessageList) = await _stateService.LoadStateAsync().ConfigureAwait(true);
        Assert.Equal(string.Empty, systemPrompt);
        Assert.Empty(loadedMessageList);
    }

    [Fact]
    public void SaveState_WithDifferentMessageTypes_ShouldPreserveAll()
    {
        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage("System message");
        chatHistory.AddUserMessage("User message");
        chatHistory.AddAssistantMessage("Assistant message");

        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        Assert.True(loadedMessageList.Count >= 3);
    }

    /// <summary>
    /// 验证保存带 ToolCalls Metadata 的 Assistant 工具调用消息后，
    /// 加载能完整恢复 Metadata。
    /// </summary>
    [Fact]
    public void SaveState_WithToolCallMetadata_ShouldPreserveToolCalls()
    {
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

        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

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
    /// 验证保存带 ToolCallId/ToolName Metadata 的 Tool 结果消息后，
    /// 加载能完整恢复 Metadata。
    /// </summary>
    [Fact]
    public void SaveState_WithToolResultMetadata_ShouldPreserveToolCallIdAndName()
    {
        var toolMetadata = ToolCallEntry.BuildToolResultMetadata("call_001", "Read");
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "请读取 README.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])),
            new(MessageRole.Tool, "File content here", toolMetadata)
        };

        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        var loadedTool = loadedMessageList.FirstOrDefault(m => m.Role == MessageRole.Tool);
        Assert.NotNull(loadedTool);
        Assert.NotNull(loadedTool.Metadata);

        Assert.Equal("call_001", loadedTool.ExtractToolCallId());
        Assert.Equal("Read", loadedTool.ExtractToolName());
    }

    /// <summary>
    /// 验证多轮工具调用对话保存加载后，消息顺序和 Metadata 完整。
    /// </summary>
    [Fact]
    public void SaveLoad_WithMultiTurnToolConversation_ShouldPreserveOrderAndMetadata()
    {
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "读取 README.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"README.md\"}" }])),
            new(MessageRole.Tool, "README content", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")),
            new(MessageRole.Assistant, "已读取 README.md"),
            new(MessageRole.User, "读取 CLAUDE.md"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_002", Name = "Read", Arguments = "{\"file_path\":\"CLAUDE.md\"}" }])),
            new(MessageRole.Tool, "CLAUDE content", ToolCallEntry.BuildToolResultMetadata("call_002", "Read")),
            new(MessageRole.Assistant, "已读取 CLAUDE.md")
        };

        _stateService.SaveState("System prompt", chatHistory);
        var (_, loadedMessageList) = _stateService.LoadState();

        Assert.Equal(8, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal(MessageRole.Tool, loadedMessageList[2].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[3].Role);
        Assert.Equal(MessageRole.User, loadedMessageList[4].Role);

        var turn1Assistant = loadedMessageList[1];
        var turn1Calls = turn1Assistant.ExtractToolCalls();
        Assert.NotEmpty(turn1Calls);
        Assert.Equal("call_001", turn1Calls[0].Id);
        Assert.Equal("Read", turn1Calls[0].Name);

        var turn1Tool = loadedMessageList[2];
        Assert.Equal("call_001", turn1Tool.ExtractToolCallId());
        Assert.Equal("Read", turn1Tool.ExtractToolName());

        var turn2Assistant = loadedMessageList[5];
        var turn2Calls = turn2Assistant.ExtractToolCalls();
        Assert.NotEmpty(turn2Calls);
        Assert.Equal("call_002", turn2Calls[0].Id);
        Assert.Equal("Read", turn2Calls[0].Name);

        var turn2Tool = loadedMessageList[6];
        Assert.Equal("call_002", turn2Tool.ExtractToolCallId());
        Assert.Equal("Read", turn2Tool.ExtractToolName());
    }

    /// <summary>
    /// 验证 LoadState 对重复消息去重。
    /// </summary>
    [Fact]
    public void LoadState_WithDuplicateContentDifferentRole_ShouldDeduplicateKeepTool()
    {
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "读取文件"),
            new(MessageRole.Assistant, null, ToolCallEntry.BuildAssistantMetadata(
                [new() { Id = "call_001", Name = "Read", Arguments = "{}" }])),
            new(MessageRole.User, "File content here"),
            new(MessageRole.Tool, "File content here", ToolCallEntry.BuildToolResultMetadata("call_001", "Read")),
            new(MessageRole.Assistant, "已读取文件")
        };

        _stateService.SaveState("System prompt", chatHistory);

        var (_, loadedMessageList) = _stateService.LoadState();

        Assert.Equal(4, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal(MessageRole.Tool, loadedMessageList[2].Role);
        Assert.Equal("File content here", loadedMessageList[2].Content);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[3].Role);
    }

    /// <summary>
    /// 验证 LoadState 对完全相同的消息（Role + Content）去重。
    /// </summary>
    [Fact]
    public void LoadState_WithExactDuplicateMessages_ShouldDeduplicate()
    {
        var chatHistory = new MessageList
        {
            new(MessageRole.User, "Hello"),
            new(MessageRole.User, "Hello"),
            new(MessageRole.Assistant, "Hi there"),
            new(MessageRole.Assistant, "Hi there")
        };

        _stateService.SaveState("System prompt", chatHistory);

        var (_, loadedMessageList) = _stateService.LoadState();

        Assert.Equal(2, loadedMessageList.Count);
        Assert.Equal(MessageRole.User, loadedMessageList[0].Role);
        Assert.Equal("Hello", loadedMessageList[0].Content);
        Assert.Equal(MessageRole.Assistant, loadedMessageList[1].Role);
        Assert.Equal("Hi there", loadedMessageList[1].Content);
    }

    /// <summary>
    /// 验证同内容不同 tool_call_id 的 Tool 结果消息不被去重丢弃。
    /// </summary>
    [Fact]
    public void LoadState_WithSameContentDifferentToolCallId_ShouldPreserveBothToolResults()
    {
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

        var (_, loadedMessageList) = _stateService.LoadState();

        var toolMessages = loadedMessageList.Where(m => m.Role == MessageRole.Tool).ToList();
        Assert.Equal(2, toolMessages.Count);
        Assert.Equal("call_001", toolMessages[0].ExtractToolCallId());
        Assert.Equal("call_002", toolMessages[1].ExtractToolCallId());
    }
}
