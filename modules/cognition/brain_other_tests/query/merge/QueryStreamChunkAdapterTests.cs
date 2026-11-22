namespace Core.Tests.Query.Merge;

/// <summary>
/// TASK103 循环合并 — 查询流块到聊天流事件的全类型映射契约。
/// </summary>
public sealed class QueryStreamChunkAdapterTests {
    private static ChatStreamEvent Map(QueryStreamChunk chunk) =>
        QueryStreamChunkAdapter.ToChatStreamEvent(chunk)
            ?? throw new InvalidOperationException($"chunk type {chunk.Type} 应映射为聊天事件");

    [Fact]
    public void Content_MapsToText() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.Content,
            Content = "hello"
        });

        evt.Type.Should().Be(ChatStreamEventType.Content);
        evt.Content.Should().Be("hello");
    }

    [Fact]
    public void Content_WithNullText_ProducesEmptyText() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.Content
        });

        evt.Content.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AgentStreamChunkType.ThinkingStart)]
    [InlineData(AgentStreamChunkType.Thinking)]
    public void ThinkingChunks_MapToThinking(AgentStreamChunkType type) {
        var evt = Map(new QueryStreamChunk {
            Type = type,
            ThinkingContent = "推理"
        });

        evt.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("推理");
    }

    [Fact]
    public void Thinking_FallsBackToContentWhenThinkingContentMissing() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.Thinking,
            Content = "fallback"
        });

        evt.ThinkingContent.Should().Be("fallback");
    }

    [Fact]
    public void ThinkingEnd_HasNoChatEvent() {
        QueryStreamChunkAdapter.ToChatStreamEvent(new QueryStreamChunk {
            Type = AgentStreamChunkType.ThinkingEnd
        }).Should().BeNull();
    }

    [Fact]
    public void ToolCallStart_MapsToToolStart() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.ToolCallStart,
            ToolName = "Read",
            ToolCallId = "call-1",
            ToolArguments = """{"path":"a.cs"}"""
        });

        evt.Type.Should().Be(ChatStreamEventType.ToolCallStart);
        evt.ToolName.Should().Be("Read");
        evt.ToolCallId.Should().Be("call-1");
        evt.ToolArguments.Should().Be("""{"path":"a.cs"}""");
    }

    [Fact]
    public void ToolCallEnd_MapsErrorAndPatch() {
        StructuredPatchHunk[] patch = [
            new() { OldStart = 1, OldLines = 1, NewStart = 1, NewLines = 2, Header = "@@ -1 +1,2 @@" }
        ];

        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.ToolCallEnd,
            ToolName = "Edit",
            ToolCallId = "call-2",
            ToolResultText = "done",
            IsToolError = true,
            StructuredPatch = patch
        });

        evt.Type.Should().Be(ChatStreamEventType.ToolCallEnd);
        evt.ToolResultText.Should().Be("done");
        evt.IsToolError.Should().BeTrue();
        evt.StructuredPatch.Should().BeSameAs(patch);
    }

    [Fact]
    public void ToolProgress_MapsAndNormalizesNulls() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.ToolProgress,
            ToolName = "WebSearch",
            ToolCallId = "call-3"
        });

        evt.Type.Should().Be(ChatStreamEventType.ToolProgress);
        evt.ToolName.Should().Be("WebSearch");
        evt.ProgressType.Should().BeEmpty();
        evt.ProgressMessage.Should().BeEmpty();
    }

    [Fact]
    public void LoopDetected_MapsCountsAndPattern() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.LoopDetected,
            LoopTriggerCount = 3,
            LoopStartIndex = 7,
            Content = "重复"
        });

        evt.Type.Should().Be(ChatStreamEventType.LoopDetected);
        evt.LoopTriggerCount.Should().Be(3);
        evt.LoopStartIndex.Should().Be(7);
        evt.Content.Should().Be("重复");
    }

    [Fact]
    public void TimingSummary_MapsToTimingSummary() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.TimingSummary,
            Content = "Total: 42ms"
        });

        evt.Type.Should().Be(ChatStreamEventType.TimingSummary);
        evt.Content.Should().Be("Total: 42ms");
    }

    [Fact]
    public void Complete_MapsUsageModelAndStopReason() {
        var usage = new TokenUsage(10, 20);

        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.Complete,
            Usage = usage,
            ModelId = "gpt-4o",
            StopReason = "end_turn"
        });

        evt.Type.Should().Be(ChatStreamEventType.Complete);
        evt.Usage.Should().BeSameAs(usage);
        evt.ModelId.Should().Be("gpt-4o");
        evt.StopReason.Should().Be("end_turn");
    }

    [Fact]
    public void Error_MapsToSystemNoticeSoFailuresRemainVisible() {
        var evt = Map(new QueryStreamChunk {
            Type = AgentStreamChunkType.Error,
            Content = "达到最大工具调用次数限制 (128)",
            StopReason = "error_max_turns"
        });

        evt.Type.Should().Be(ChatStreamEventType.SystemNotice);
        evt.Content.Should().Be("达到最大工具调用次数限制 (128)");
    }
}
