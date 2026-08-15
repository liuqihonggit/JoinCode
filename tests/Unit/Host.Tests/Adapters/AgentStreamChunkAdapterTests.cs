namespace JoinCode.Host.Tests.Adapters;

public class AgentStreamChunkAdapterTests
{
    private const string TestAgentId = "agent-test-001";

    [Fact]
    public void ContentChunk_ShouldMapToContentEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            Content = "hello world",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Content);
        evt.Content.Should().Be("hello world");
    }

    [Fact]
    public void ThinkingStartChunk_ShouldMapToThinkingEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.ThinkingStart,
            ThinkingContent = "Let me think...",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("Let me think...");
    }

    [Fact]
    public void ThinkingChunk_ShouldMapToThinkingEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Thinking,
            ThinkingContent = "analyzing...",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("analyzing...");
    }

    [Fact]
    public void ThinkingEndChunk_ShouldReturnNull()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.ThinkingEnd,
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().BeNull();
    }

    [Fact]
    public void ToolCallStartChunk_ShouldMapToToolCallStartEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.ToolCallStart,
            ToolName = "FileRead",
            ToolCallId = "call_123",
            ToolArguments = "{\"path\":\"test.txt\"}",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolCallStart);
        evt.ToolName.Should().Be("FileRead");
        evt.ToolCallId.Should().Be("call_123");
        evt.ToolArguments.Should().Be("{\"path\":\"test.txt\"}");
    }

    [Fact]
    public void ToolCallEndChunk_ShouldMapToToolCallEndEvent()
    {
        var hunks = new StructuredPatchHunk[] { new() { OldStart = 1, OldLines = 1, NewStart = 1, NewLines = 1 } };
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "FileEdit",
            ToolCallId = "call_456",
            ToolResultText = "File updated",
            IsToolError = false,
            StructuredPatch = hunks,
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolCallEnd);
        evt.ToolName.Should().Be("FileEdit");
        evt.ToolCallId.Should().Be("call_456");
        evt.ToolResultText.Should().Be("File updated");
        evt.IsToolError.Should().BeFalse();
        evt.StructuredPatch.Should().BeSameAs(hunks);
    }

    [Fact]
    public void ToolProgressChunk_ShouldMapToToolProgressEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.ToolProgress,
            ToolName = "WebSearch",
            ToolCallId = "call_789",
            ProgressType = "query_update",
            ProgressMessage = "Searching for...",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.ToolProgress);
        evt.ToolName.Should().Be("WebSearch");
        evt.ToolCallId.Should().Be("call_789");
        evt.ProgressType.Should().Be("query_update");
        evt.ProgressMessage.Should().Be("Searching for...");
    }

    [Fact]
    public void LoopDetectedChunk_ShouldMapToLoopDetectedEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.LoopDetected,
            LoopTriggerCount = 3,
            LoopStartIndex = 5,
            Content = "repeated pattern",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.LoopDetected);
        evt.LoopTriggerCount.Should().Be(3);
        evt.LoopStartIndex.Should().Be(5);
        evt.Content.Should().Be("repeated pattern");
    }

    [Fact]
    public void TimingSummaryChunk_ShouldMapToTimingSummaryEvent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.TimingSummary,
            Content = "Total: 1.5s",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.TimingSummary);
        evt.Content.Should().Be("Total: 1.5s");
    }

    [Fact]
    public void CompleteChunk_ShouldMapToCompleteEvent()
    {
        var usage = new TokenUsage(100, 200);
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Usage = usage,
            ModelId = "gpt-4o",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Complete);
        evt.Usage.Should().BeSameAs(usage);
        evt.ModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public void ErrorChunk_ShouldReturnNull()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Error,
            Content = "Something went wrong",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().BeNull();
    }

    [Fact]
    public void ThinkingChunk_WithNullThinkingContent_ShouldFallbackToContent()
    {
        var chunk = new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Thinking,
            Content = "fallback thinking",
            AgentId = TestAgentId
        };

        var evt = AgentStreamChunkAdapter.ToChatStreamEvent(chunk);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("fallback thinking");
    }
}
