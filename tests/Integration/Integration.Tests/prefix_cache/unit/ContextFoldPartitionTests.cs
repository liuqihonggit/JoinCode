namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldPartitionTests
{
    [Fact]
    public void PartitionFold_UserMessageUnder500_Pinned()
    {
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, "short user message"),
            new(MessageRole.Assistant, "assistant response")
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().ContainSingle(m => m.Content == "short user message");
        foldable.Should().ContainSingle(m => m.Content == "assistant response");
    }

    [Fact]
    public void PartitionFold_UserMessageOver500_Foldable()
    {
        var longContent = new string('x', 501);
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, longContent),
            new(MessageRole.Assistant, "assistant response")
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().BeEmpty("long user messages should be foldable");
        foldable.Should().HaveCount(2);
    }

    [Fact]
    public void PartitionFold_CompactSummary_Pinned()
    {
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, "summary content", new Dictionary<string, JsonElement>
            {
                ["isCompactSummary"] = JsonElementHelper.FromBoolean(true)
            }),
            new(MessageRole.Assistant, "assistant response")
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().ContainSingle(m => m.Metadata != null && m.Metadata.ContainsKey("isCompactSummary"));
        foldable.Should().ContainSingle(m => m.Content == "assistant response");
    }

    [Fact]
    public void PartitionFold_ToolResult_Foldable()
    {
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, "do something"),
            new(MessageRole.Assistant, "calling tool"),
            new(MessageRole.Tool, "tool result content", new Dictionary<string, JsonElement>
            {
                ["tool_call_id"] = JsonElementHelper.FromString("call_1")
            })
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().ContainSingle(m => m.Role == MessageRole.User);
        foldable.Should().HaveCount(2, "assistant and tool messages are foldable");
    }

    [Fact]
    public void PartitionFold_MultipleUserMessages_OnlyShortOnesPinned()
    {
        var longContent = new string('y', 600);
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, "short"),
            new(MessageRole.Assistant, "response1"),
            new(MessageRole.User, longContent),
            new(MessageRole.Assistant, "response2")
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().ContainSingle(m => m.Content == "short");
        foldable.Should().HaveCount(3, "long user message + 2 assistant messages are foldable");
    }

    [Fact]
    public void PartitionFold_CompactSummaryNeverReSummarized()
    {
        var head = new List<ApiMessage>
        {
            new(MessageRole.User, "prev summary", new Dictionary<string, JsonElement>
            {
                ["isCompactSummary"] = JsonElementHelper.FromBoolean(true)
            }),
            new(MessageRole.Assistant, "after summary"),
            new(MessageRole.User, "new question")
        };

        var (kept, foldable) = InvokePartitionFold(head);

        kept.Should().HaveCount(2, "compact summary and short user message are both pinned");
        foldable.Should().ContainSingle(m => m.Content == "after summary");
    }

    private static (List<ApiMessage> Kept, List<ApiMessage> Foldable) InvokePartitionFold(IReadOnlyList<ApiMessage> head)
    {
        var executor = new ContextFoldExecutor(new StubFoldSummarizer());
        var method = typeof(ContextFoldExecutor).GetMethod("PartitionFold",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = ((List<ApiMessage> Kept, List<ApiMessage> Foldable))method!.Invoke(null, [head])!;
        return result;
    }

    private sealed class StubFoldSummarizer : IFoldSummarizer
    {
        public Task<string> SummarizeForFoldAsync(IReadOnlyList<ApiMessage> messages, CancellationToken ct = default)
            => Task.FromResult("stub summary");
    }
}
