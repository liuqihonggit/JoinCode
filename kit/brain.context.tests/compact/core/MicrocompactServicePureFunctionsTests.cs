namespace Core.Context.Compact;

/// <summary>
/// MicrocompactService 内部纯函数测试 — ClearToolResults + CollectCompactableToolIds
/// 改 internal static 后直接测清除逻辑/幂等性/收集顺序,不依赖 MicrocompactService 实例
/// </summary>
public sealed class MicrocompactServicePureFunctionsTests {
    // ---------- ClearToolResults ----------

    [Fact]
    public void ClearToolResults_EmptyMessages_ReturnsEmptyAndZeroSaved() {
        var (messages, saved) = MicrocompactService.ClearToolResults([], new HashSet<string>());

        Assert.Empty(messages);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_NonToolMessage_PreservedAsIs() {
        var userMsg = new ApiMessage(MessageRole.User, "hello");
        var (messages, saved) = MicrocompactService.ClearToolResults([userMsg], new HashSet<string>());

        Assert.Single(messages);
        Assert.Equal(userMsg, messages[0]);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_ToolMessageNotInClearSet_PreservedAsIs() {
        var toolMsg = CreateToolResult("call_1", "content");
        var (messages, saved) = MicrocompactService.ClearToolResults([toolMsg], new HashSet<string> { "call_99" });

        Assert.Single(messages);
        Assert.Equal(toolMsg, messages[0]);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_ToolMessageInClearSet_ReplacedWithPlaceholder() {
        var toolMsg = CreateToolResult("call_1", new string('x', 1000));
        var (messages, saved) = MicrocompactService.ClearToolResults([toolMsg], new HashSet<string> { "call_1" });

        Assert.Single(messages);
        Assert.Equal(ContentReplacementConstants.ToolResultClearedMessage, messages[0].Content);
        Assert.True(saved > 0);
    }

    [Fact]
    public void ClearToolResults_AlreadyClearedMessage_Preserved_Idempotent() {
        var clearedMsg = CreateToolResult("call_1", ContentReplacementConstants.ToolResultClearedMessage);
        var (messages, saved) = MicrocompactService.ClearToolResults([clearedMsg], new HashSet<string> { "call_1" });

        Assert.Single(messages);
        Assert.Equal(ContentReplacementConstants.ToolResultClearedMessage, messages[0].Content);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_ToolMessageWithNullContent_PreservedAsIs() {
        var toolMsg = new ApiMessage(MessageRole.Tool, null);
        var (messages, saved) = MicrocompactService.ClearToolResults([toolMsg], new HashSet<string> { "call_1" });

        Assert.Single(messages);
        Assert.Null(messages[0].Content);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_ToolMessageWithoutToolCallId_PreservedAsIs() {
        var toolMsg = new ApiMessage(MessageRole.Tool, "content"); // 无 ToolCallId metadata
        var (messages, saved) = MicrocompactService.ClearToolResults([toolMsg], new HashSet<string> { "call_1" });

        Assert.Single(messages);
        Assert.Equal("content", messages[0].Content);
        Assert.Equal(0, saved);
    }

    [Fact]
    public void ClearToolResults_MixedMessages_OnlyMatchingCleared() {
        var userMsg = new ApiMessage(MessageRole.User, "q");
        var tool1 = CreateToolResult("call_1", new string('a', 1000));
        var assistantMsg = new ApiMessage(MessageRole.Assistant, "resp");
        var tool2 = CreateToolResult("call_2", new string('b', 2000));

        var (messages, saved) = MicrocompactService.ClearToolResults(
            [userMsg, tool1, assistantMsg, tool2],
            new HashSet<string> { "call_1" });

        Assert.Equal(4, messages.Count);
        Assert.Equal(userMsg, messages[0]);
        Assert.Equal(ContentReplacementConstants.ToolResultClearedMessage, messages[1].Content);
        Assert.Equal(assistantMsg, messages[2]);
        Assert.Equal(tool2.Content, messages[3].Content); // tool2 未清除
        Assert.True(saved > 0);
    }

    // ---------- CollectCompactableToolIds ----------

    [Fact]
    public void CollectCompactableToolIds_EmptyMessages_ReturnsEmpty() {
        var ids = MicrocompactService.CollectCompactableToolIds([], null);

        Assert.Empty(ids);
    }

    [Fact]
    public void CollectCompactableToolIds_NoAssistantMessages_ReturnsEmpty() {
        var messages = new List<ApiMessage> {
            new(MessageRole.User, "q"),
            new(MessageRole.Tool, "result"),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Empty(ids);
    }

    [Fact]
    public void CollectCompactableToolIds_AssistantWithoutToolCalls_ReturnsEmpty() {
        var messages = new List<ApiMessage> {
            new(MessageRole.Assistant, "plain text"),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Empty(ids);
    }

    [Fact]
    public void CollectCompactableToolIds_CompactableTool_Collected() {
        var messages = new List<ApiMessage> {
            CreateAssistantWithToolCalls("call_1", "bash"),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Single(ids);
        Assert.Equal("call_1", ids[0]);
    }

    [Fact]
    public void CollectCompactableToolIds_NonCompactableTool_NotCollected() {
        var messages = new List<ApiMessage> {
            CreateAssistantWithToolCalls("call_1", "NonCompactableTool"),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Empty(ids);
    }

    [Fact]
    public void CollectCompactableToolIds_MultipleCalls_PreservesOrder() {
        var messages = new List<ApiMessage> {
            CreateAssistantWithToolCalls("call_1", "bash"),
            CreateAssistantWithToolCalls("call_2", "grep"),
            CreateAssistantWithToolCalls("call_3", "bash"),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Equal(new[] { "call_1", "call_2", "call_3" }, ids);
    }

    [Fact]
    public void CollectCompactableToolIds_CustomCompactableSet_UsedInsteadOfDefault() {
        var messages = new List<ApiMessage> {
            CreateAssistantWithToolCalls("call_1", "MyCustomTool"),
            CreateAssistantWithToolCalls("call_2", "bash"),
        };
        var custom = new HashSet<string> { "MyCustomTool" };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, custom);

        Assert.Single(ids);
        Assert.Equal("call_1", ids[0]);
    }

    [Fact]
    public void CollectCompactableToolIds_MultipleToolCallsInSingleMessage_AllCollected() {
        var messages = new List<ApiMessage> {
            CreateAssistantWithMultipleToolCalls(
                ("call_1", "bash"), ("call_2", "grep")),
        };

        var ids = MicrocompactService.CollectCompactableToolIds(messages, null);

        Assert.Equal(2, ids.Count);
        Assert.Equal("call_1", ids[0]);
        Assert.Equal("call_2", ids[1]);
    }

    // ---------- Helpers ----------

    private static ApiMessage CreateAssistantWithToolCalls(string id, string name) {
        var json = $$"""[{"Id":"{{id}}","Name":"{{name}}","Arguments":"{}"}]""";
        using var doc = JsonDocument.Parse(json);
        var metadata = new Dictionary<string, JsonElement> {
            ["ToolCalls"] = doc.RootElement.Clone(),
        };
        return new ApiMessage(MessageRole.Assistant, null, metadata);
    }

    private static ApiMessage CreateAssistantWithMultipleToolCalls(params (string Id, string Name)[] calls) {
        var items = string.Join(",", calls.Select(c => $$"""{"Id":"{{c.Id}}","Name":"{{c.Name}}","Arguments":"{}"}"""));
        var json = $"[{items}]";
        using var doc = JsonDocument.Parse(json);
        var metadata = new Dictionary<string, JsonElement> {
            ["ToolCalls"] = doc.RootElement.Clone(),
        };
        return new ApiMessage(MessageRole.Assistant, null, metadata);
    }

    private static ApiMessage CreateToolResult(string toolCallId, string content) {
        var metadata = new Dictionary<string, JsonElement> {
            ["ToolCallId"] = JsonSerializer.SerializeToElement(toolCallId),
        };
        return new ApiMessage(MessageRole.Tool, content, metadata);
    }
}
