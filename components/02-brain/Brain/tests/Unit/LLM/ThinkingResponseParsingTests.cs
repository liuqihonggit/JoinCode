namespace Core.Tests.LLM;

using Api.LLM.QueryServices.Anthropic;
using Api.LLM.QueryServices.OpenAI;
using JoinCode.Abstractions.LLM.Chat;

public sealed class ThinkingResponseParsingTests
{
    [Fact]
    public void ChatStreamEventType_ShouldHaveThinking()
    {
        var thinkingType = (ChatStreamEventType)Enum.Parse(typeof(ChatStreamEventType), "Thinking");
        thinkingType.Should().BeDefined();
    }

    [Fact]
    public void ChatStreamEvent_Thinking_ShouldCarryThinkingContent()
    {
        var evt = ChatStreamEvent.Thinking("Let me analyze this step by step...");

        evt.Type.Should().Be(ChatStreamEventType.Thinking);
        evt.ThinkingContent.Should().Be("Let me analyze this step by step...");
    }

    [Fact]
    public void ChatStreamEvent_Match_ShouldHandleThinkingType()
    {
        var evt = ChatStreamEvent.Thinking("reasoning text");

        var result = evt.Match(
            onText: t => $"text:{t}",
            onThinking: t => $"thinking:{t}",
            onToolStart: (name, id, args) => $"tool:{name}",
            onToolEnd: (name, result, id, err, patch) => $"toolend:{name}",
            onToolProgress: (name, type, msg) => $"progress:{name}:{type}",
            onLoopDetected: (count, idx, pattern) => $"loop:{count}",
            onTimingSummary: s => $"timing:{s}",
            onDone: (usage, model) => "done");

        result.Should().Be("thinking:reasoning text");
    }

    [Fact]
    public void ChatStreamEvent_Switch_ShouldHandleThinkingType()
    {
        var evt = ChatStreamEvent.Thinking("reasoning text");
        string? captured = null;

        evt.Switch(
            onText: t => captured = $"text:{t}",
            onThinking: t => captured = $"thinking:{t}",
            onToolStart: (name, id, args) => captured = $"tool:{name}",
            onToolEnd: (name, result, id, err, patch) => captured = $"toolend:{name}",
            onToolProgress: (name, type, msg) => captured = $"progress:{name}:{type}",
            onLoopDetected: (count, idx, pattern) => captured = $"loop:{count}",
            onTimingSummary: s => captured = $"timing:{s}",
            onDone: (usage, model) => captured = "done");

        captured.Should().Be("thinking:reasoning text");
    }

    [Fact]
    public void AnthropicResponseContentBlock_ThinkingType_ShouldDeserialize()
    {
        var json = """{"type":"thinking","thinking":"I need to think about this carefully"}""";
        var block = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicResponseContentBlock);

        block.Should().NotBeNull();
        block!.Type.Should().Be(AnthropicContentBlockType.Thinking);
        block.Thinking.Should().Be("I need to think about this carefully");
    }

    [Fact]
    public void AnthropicStreamingDelta_ThinkingDelta_ShouldDeserialize()
    {
        var json = """{"type":"thinking_delta","thinking":" step by step"}""";
        var delta = JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicStreamingDelta);

        delta.Should().NotBeNull();
        delta!.Type.Should().Be(AnthropicDeltaType.ThinkingDelta);
        delta.Thinking.Should().Be(" step by step");
    }

    [Fact]
    public void ConvertAnthropicResponseToApiMessages_WithThinkingBlock_ShouldIncludeThinkingInMetadata()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Type = "message",
            Role = "assistant",
            Model = "claude-3-7-sonnet",
            Content =
            [
                new AnthropicResponseContentBlock { Type = AnthropicContentBlockType.Thinking, Thinking = "Let me reason about this..." },
                new AnthropicResponseContentBlock { Type = AnthropicContentBlockType.Text, Text = "Here is my answer." }
            ],
            StopReason = AnthropicStopReason.EndTurn
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Be("Here is my answer.");
        messages[0].Metadata.Should().ContainKey("thinking_content");
        messages[0].Metadata!["thinking_content"].GetString().Should().Be("Let me reason about this...");
    }

    [Fact]
    public void OpenAIApiMessage_ReasoningContent_ShouldDeserialize()
    {
        var json = """{"role":"assistant","content":"The answer is 42","reasoning_content":"I calculated this by..."}""";
        var msg = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIApiMessage);

        msg.Should().NotBeNull();
        msg!.Content.Should().Be("The answer is 42");
        msg.ReasoningContent.Should().Be("I calculated this by...");
    }

    [Fact]
    public void ConvertToApiMessage_WithReasoningContent_ShouldIncludeInMetadata()
    {
        var choice = new OpenAIChoice
        {
            Index = 0,
            Message = new OpenAIApiMessage
            {
                Role = "assistant",
                Content = "Final answer",
                ReasoningContent = "My reasoning process..."
            },
            FinishReason = OpenAIFinishReasonConstants.Stop
        };

        var msg = OpenAIQueryService.ConvertToApiMessage(choice, null);

        msg.Content.Should().Be("Final answer");
        msg.Metadata.Should().ContainKey("reasoning_content");
        msg.Metadata!["reasoning_content"].GetString().Should().Be("My reasoning process...");
    }
}
