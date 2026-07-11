namespace Brain.Tests.Context.Compact;

/// <summary>
/// MicrocompactService 单元测试 — 对齐 TS microCompact.ts
/// 验证工具结果清除逻辑、COMPACTABLE_TOOLS 过滤、keepRecent 保留策略
/// </summary>
public sealed class MicrocompactServiceTests
{
    [Fact]
    public void CompactMessages_ClearsOldToolResults()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
            CreateToolResultMessage("call_1", new string('x', 1000)),
            CreateAssistantToolCallMessage("call_2", "Bash"),
            CreateToolResultMessage("call_2", new string('y', 1000)),
            CreateAssistantToolCallMessage("call_3", "Bash"),
            CreateToolResultMessage("call_3", new string('z', 1000)),
        };

        var result = service.CompactMessages(messages, keepRecent: 1);

        result.WasCompacted.Should().BeTrue();
        result.ToolsCleared.Should().Be(2);
        // 最近的 call_3 保留，call_1 和 call_2 被清除
        var clearedMsgs = result.Messages.Where(m => m.Content == ContentReplacementConstants.ToolResultClearedMessage).ToList();
        clearedMsgs.Should().HaveCount(2);
        // call_3 保留原始内容
        var keptMsg = result.Messages.FirstOrDefault(m => m.Role == MessageRole.Tool && m.Content?.Contains('z') == true);
        keptMsg.Should().NotBeNull();
    }

    [Fact]
    public void CompactMessages_OnlyCompactsCompactableTools()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
            CreateToolResultMessage("call_1", new string('x', 1000)),
            CreateAssistantToolCallMessage("call_2", "UnknownTool"),
            CreateToolResultMessage("call_2", new string('y', 1000)),
        };

        var result = service.CompactMessages(messages, keepRecent: 1);

        // 只有 Bash 是 compactable，UnknownTool 不在列表中
        // call_1 是唯一的 compactable ID，keepRecent=1 保留它，所以没有清除
        result.WasCompacted.Should().BeFalse();
    }

    [Fact]
    public void CompactMessages_SkipsAlreadyClearedResults()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
            CreateToolResultMessage("call_1", ContentReplacementConstants.ToolResultClearedMessage),
            CreateAssistantToolCallMessage("call_2", "Bash"),
            CreateToolResultMessage("call_2", new string('y', 1000)),
        };

        var result = service.CompactMessages(messages, keepRecent: 1);

        // call_1 已清除，call_2 保留（keepRecent=1），无新清除
        result.WasCompacted.Should().BeFalse();
    }

    [Fact]
    public void CompactMessages_NoCompactableTools_ReturnsUnchanged()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "hello"),
            new(MessageRole.Assistant, "hi"),
        };

        var result = service.CompactMessages(messages);

        result.WasCompacted.Should().BeFalse();
        result.Messages.Should().BeSameAs(messages);
    }

    [Fact]
    public void CompactMessages_UsesToolCallsMetadata()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        // 使用 ChatService 实际存储格式: Metadata["ToolCalls"] = [{Id, Name, Arguments}]
        var toolCalls = new List<Dictionary<string, JsonElement>>
        {
            new()
            {
                ["Id"] = JsonSerializer.SerializeToElement("call_1"),
                ["Name"] = JsonSerializer.SerializeToElement("Grep"),
                ["Arguments"] = JsonSerializer.SerializeToElement("{}"),
            }
        };
        var assistantMetadata = new Dictionary<string, JsonElement>
        {
            ["ToolCalls"] = JsonSerializer.SerializeToElement(toolCalls, TestJsonContext.Default.ListDictionaryStringJsonElement),
        };
        var messages = new List<ApiMessage>
        {
            new(MessageRole.Assistant, null, assistantMetadata),
            CreateToolResultMessage("call_1", new string('x', 1000)),
        };

        var result = service.CompactMessages(messages, keepRecent: 0);

        // keepRecent=0 但 Math.Max(1, 0) = 1，所以 call_1 保留
        result.WasCompacted.Should().BeFalse();
    }

    [Fact]
    public void CompactMessages_CustomCompactableToolNames()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var customTools = new HashSet<string> { "MyCustomTool" };
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "MyCustomTool"),
            CreateToolResultMessage("call_1", new string('x', 1000)),
            CreateAssistantToolCallMessage("call_2", "Bash"),
            CreateToolResultMessage("call_2", new string('y', 1000)),
        };

        var result = service.CompactMessages(messages, compactableToolNames: customTools, keepRecent: 0);

        // 只有 MyCustomTool 是 compactable，Bash 不在自定义列表中
        // call_1 保留（keepRecent=1），无清除
        result.WasCompacted.Should().BeFalse();
    }

    [Fact]
    public void TimeBasedCompact_ReturnsNull_WhenNoTimestamp()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.Assistant, "hello"), // 无 timestamp
        };

        var result = service.TimeBasedCompact(messages);

        result.Should().BeNull();
    }

    [Fact]
    public void TimeBasedCompact_ReturnsNull_WhenGapTooSmall()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var metadata = new Dictionary<string, JsonElement>
        {
            ["timestamp"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToString("O")),
        };
        var messages = new List<ApiMessage>
        {
            new(MessageRole.Assistant, "hello", metadata),
        };

        var result = service.TimeBasedCompact(messages, gapThresholdMinutes: 60);

        result.Should().BeNull();
    }

    [Fact]
    public void EstimateMessageTokens_ReturnsNonZero()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "hello world"),
        };

        var tokens = service.EstimateMessageTokens(messages);

        tokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateMessageTokens_IncludesContentBlocks()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var contentBlocks = new List<ToolContent>
        {
            new() { Type = ToolContentType.Image, Data = "base64data", MimeType = "image/png" },
            new() { Type = ToolContentType.Text, Text = "some text" },
        };
        var messages = new List<ApiMessage>
        {
            new() { Role = MessageRole.User, Content = "hello", ContentBlocks = contentBlocks },
        };

        var tokens = service.EstimateMessageTokens(messages);

        // image 固定 2000 tokens + text + "hello"
        tokens.Should().BeGreaterThan(2000);
    }

    [Fact]
    public void EstimateMessageTokens_IncludesToolUseBlocks()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
        };

        var tokens = service.EstimateMessageTokens(messages);

        // tool_use name "Bash" + 空参数估算
        tokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompactMessages_EmptyList_ReturnsUnchanged()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>();

        var result = service.CompactMessages(messages);

        result.WasCompacted.Should().BeFalse();
        result.Messages.Should().BeSameAs(messages);
    }

    [Fact]
    public void CompactMessages_MultipleToolResultsInSequence()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
            CreateToolResultMessage("call_1", new string('x', 1000)),
            CreateAssistantToolCallMessage("call_2", "Grep"),
            CreateToolResultMessage("call_2", new string('y', 1000)),
            CreateAssistantToolCallMessage("call_3", "Read"),
            CreateToolResultMessage("call_3", new string('z', 1000)),
            CreateAssistantToolCallMessage("call_4", "Bash"),
            CreateToolResultMessage("call_4", new string('w', 1000)),
        };

        var result = service.CompactMessages(messages, keepRecent: 2);

        result.WasCompacted.Should().BeTrue();
        result.ToolsCleared.Should().Be(2);
        // call_3 和 call_4 保留，call_1 和 call_2 被清除
        var clearedMsgs = result.Messages.Where(m => m.Content == ContentReplacementConstants.ToolResultClearedMessage).ToList();
        clearedMsgs.Should().HaveCount(2);
    }

    [Fact]
    public void TimeBasedCompact_ActuallyClears_WhenGapExceeded()
    {
        var service = new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance);
        var oldTimestamp = DateTime.UtcNow.AddMinutes(-120).ToString("O");
        var assistantMetadata = new Dictionary<string, JsonElement>
        {
            ["timestamp"] = JsonSerializer.SerializeToElement(oldTimestamp),
        };
        var messages = new List<ApiMessage>
        {
            CreateAssistantToolCallMessage("call_1", "Bash"),
            CreateToolResultMessage("call_1", new string('x', 5000)),
            CreateAssistantToolCallMessage("call_2", "Bash"),
            CreateToolResultMessage("call_2", new string('y', 5000)),
            // 最后一条 assistant 消息带旧时间戳
            new(MessageRole.Assistant, "done", assistantMetadata),
        };

        var result = service.TimeBasedCompact(messages, gapThresholdMinutes: 60, keepRecent: 1);

        result.Should().NotBeNull();
        result!.ToolsCleared.Should().Be(1);
        result!.TokensSaved.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 创建 Assistant 工具调用消息 — 使用简化的 Metadata 格式
    /// </summary>
    private static ApiMessage CreateAssistantToolCallMessage(string toolCallId, string toolName)
    {
        var toolCalls = new List<Dictionary<string, JsonElement>>
        {
            new()
            {
                ["Id"] = JsonSerializer.SerializeToElement(toolCallId),
                ["Name"] = JsonSerializer.SerializeToElement(toolName),
                ["Arguments"] = JsonSerializer.SerializeToElement("{}"),
            }
        };
        var metadata = new Dictionary<string, JsonElement>
        {
            ["ToolCalls"] = JsonSerializer.SerializeToElement(toolCalls, TestJsonContext.Default.ListDictionaryStringJsonElement),
        };
        return new ApiMessage(MessageRole.Assistant, null, metadata);
    }

    /// <summary>
    /// 创建 Tool 结果消息 — 对齐 ChatService 存储格式 Metadata["ToolCallId"]
    /// </summary>
    private static ApiMessage CreateToolResultMessage(string toolCallId, string content)
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["ToolCallId"] = JsonSerializer.SerializeToElement(toolCallId),
        };
        return new ApiMessage(MessageRole.Tool, content, metadata);
    }
}

[JsonSerializable(typeof(List<Dictionary<string, JsonElement>>))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class TestJsonContext : JsonSerializerContext;
