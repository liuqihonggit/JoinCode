namespace Integration.Tests.PrefixCache.Unit;

public sealed class MessageHealingTests
{
    private static ApiMessage SystemMsg(string content) =>
        new(MessageRole.System, content);

    private static ApiMessage UserMsg(string content) =>
        new(MessageRole.User, content);

    private static ApiMessage AssistantMsg(string content) =>
        new(MessageRole.Assistant, content);

    private static ApiMessage AssistantToolCallMsg(string toolCallId, string toolName) =>
        new(MessageRole.Assistant, null, new Dictionary<string, JsonElement>
        {
            ["ToolCallId"] = JsonElementHelper.FromString(toolCallId),
            ["ToolCall"] = JsonElementHelper.FromString(toolName),
            ["ToolCalls"] = JsonElementHelper.FromJson($"[{{\"Id\":\"{toolCallId}\",\"Name\":\"{toolName}\"}}]")
        });

    private static ApiMessage ToolResultMsg(string toolCallId, string content) =>
        new(MessageRole.Tool, content, new Dictionary<string, JsonElement> { ["ToolCallId"] = JsonElementHelper.FromString(toolCallId) });

    [Fact]
    public void Heal_TrailingAssistantToolCallWithoutResult_InsertsSyntheticToolResult()
    {
        var messages = new List<ApiMessage>
        {
            SystemMsg("system"),
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(4);
        healed[0].Role.Should().Be(MessageRole.System);
        healed[1].Role.Should().Be(MessageRole.User);
        healed[2].Role.Should().Be(MessageRole.Assistant);
        healed[2].Content.Should().Be("[Tool use interrupted]");
        healed[3].Role.Should().Be(MessageRole.Tool);
        healed[3].Content.Should().Contain("tool_use_error");
        healed[3].Metadata.Should().ContainKey("IsSynthetic");
    }

    [Fact]
    public void Heal_TrailingAssistantToolCallWithText_PreservesTextInsertsSyntheticResult()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            new ApiMessage(MessageRole.Assistant, "Let me check that.", new Dictionary<string, JsonElement>
            {
                ["ToolCallId"] = JsonElementHelper.FromString("tc_1"),
                ["ToolCall"] = JsonElementHelper.FromString("read_file")
            }),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(3);
        healed[1].Role.Should().Be(MessageRole.Assistant);
        healed[1].Content.Should().Be("Let me check that.");
        healed[1].Metadata.Should().BeNull("tool call metadata should be stripped from trailing assistant");
        healed[2].Role.Should().Be(MessageRole.Tool);
        healed[2].Content.Should().Contain("tool_use_error");
    }

    [Fact]
    public void Heal_PairedToolCallAndResult_PreservesBoth()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", "file content"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(3);
        healed[1].Role.Should().Be(MessageRole.Assistant);
        healed[2].Role.Should().Be(MessageRole.Tool);
    }

    [Fact]
    public void Heal_OrphanedToolResultWithoutToolCall_RemovesOrphan()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantMsg("response"),
            ToolResultMsg("tc_unknown", "orphaned result"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(2);
        healed[0].Role.Should().Be(MessageRole.User);
        healed[1].Role.Should().Be(MessageRole.Assistant);
    }

    [Fact]
    public void Heal_MultipleTrailingToolCallsWithoutResults_InsertsSyntheticResultsForAll()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", "content"),
            AssistantToolCallMsg("tc_2", "search"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(5);
        healed[2].Role.Should().Be(MessageRole.Tool);
        healed[3].Role.Should().Be(MessageRole.Assistant);
        healed[3].Content.Should().Be("[Tool use interrupted]");
        healed[4].Role.Should().Be(MessageRole.Tool);
        healed[4].Content.Should().Contain("tool_use_error");
    }

    [Fact]
    public void Heal_EmptyMessages_ReturnsEmpty()
    {
        var healed = MessageHealer.Heal([]);

        healed.Should().BeEmpty();
    }

    [Fact]
    public void Heal_NoToolCalls_ReturnsSameMessages()
    {
        var messages = new List<ApiMessage>
        {
            SystemMsg("system"),
            UserMsg("hello"),
            AssistantMsg("hi"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(3);
    }

    [Fact]
    public void Heal_LargeToolResult_TruncatesToMaxChars()
    {
        var largeContent = new string('x', 10000);
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", largeContent),
        };

        var healed = MessageHealer.Heal(messages, maxToolResultChars: 1000);

        healed[2].Content.Should().NotBeNull();
        healed[2].Content!.Length.Should().BeLessThan(10000);
        healed[2].Content!.Length.Should().BeLessThanOrEqualTo(1100);
    }

    [Fact]
    public void Heal_ToolResultUnderLimit_NotTruncated()
    {
        var content = "short content";
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", content),
        };

        var healed = MessageHealer.Heal(messages, maxToolResultChars: 1000);

        healed[2].Content.Should().Be(content);
    }

    [Fact]
    public void Heal_MiddleToolCallWithResult_PreservesMiddle()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", "content"),
            AssistantMsg("based on the file..."),
            UserMsg("follow up"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(5);
    }

    [Fact]
    public void Heal_ConsecutiveUserMessages_PreservesAll()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            UserMsg("world"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(2);
    }

    [Fact]
    public void Heal_MixedPairedAndUnpaired_InsertsSyntheticForUnpaired()
    {
        var messages = new List<ApiMessage>
        {
            UserMsg("hello"),
            AssistantToolCallMsg("tc_1", "read_file"),
            ToolResultMsg("tc_1", "content"),
            AssistantToolCallMsg("tc_2", "search"),
            UserMsg("next question"),
        };

        var healed = MessageHealer.Heal(messages);

        healed.Should().HaveCount(6);
        healed[0].Role.Should().Be(MessageRole.User);
        healed[1].Role.Should().Be(MessageRole.Assistant);
        healed[2].Role.Should().Be(MessageRole.Tool);
        healed[3].Role.Should().Be(MessageRole.Assistant);
        healed[3].Content.Should().Be("[Tool use interrupted]");
        healed[4].Role.Should().Be(MessageRole.Tool);
        healed[4].Content.Should().Contain("tool_use_error");
        healed[5].Role.Should().Be(MessageRole.User);
    }
}
