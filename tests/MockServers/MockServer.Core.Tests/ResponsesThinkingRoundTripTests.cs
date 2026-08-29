namespace MockServer.Core.Tests;


/// <summary>
/// 思考链回传校验测试 — 模拟真实 DeepSeek Responses 协议行为:
/// thinking 模式下历史含 assistant 消息但缺失 reasoning item 回传时返回 400。
/// </summary>
public sealed class ResponsesThinkingRoundTripTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void GetHttpStatusCode_ThinkingModeMissingReasoning_Returns400()
    {
        var strategy = new ResponsesResponseStrategy(null, "default", enforceThinkingRoundTrip: true);
        var request = Parse("""
        {
            "model": "deepseek-v4-flash",
            "reasoning": { "effort": "high" },
            "input": [
                { "type": "message", "role": "user", "content": [{"type":"input_text","text":"你好"}] },
                { "type": "message", "role": "assistant", "content": [{"type":"output_text","text":"我看看"}] }
            ]
        }
        """);

        strategy.GetHttpStatusCode(request).Should().Be(400);
    }

    [Fact]
    public void GetHttpStatusCode_ThinkingModeWithReasoningRoundTrip_Returns200()
    {
        var strategy = new ResponsesResponseStrategy(null, "default", enforceThinkingRoundTrip: true);
        var request = Parse("""
        {
            "model": "deepseek-v4-flash",
            "reasoning": { "effort": "high" },
            "input": [
                { "type": "message", "role": "user", "content": [{"type":"input_text","text":"你好"}] },
                { "type": "reasoning", "content": [{"type":"reasoning_text","text":"思考中"}] },
                { "type": "message", "role": "assistant", "content": [{"type":"output_text","text":"我看看"}] }
            ]
        }
        """);

        strategy.GetHttpStatusCode(request).Should().Be(200);
    }

    [Fact]
    public void GetHttpStatusCode_NoThinkingMode_Returns200()
    {
        var strategy = new ResponsesResponseStrategy(null, "default", enforceThinkingRoundTrip: true);
        var request = Parse("""
        {
            "model": "deepseek-v4-flash",
            "input": [
                { "type": "message", "role": "assistant", "content": [{"type":"output_text","text":"我看看"}] }
            ]
        }
        """);

        strategy.GetHttpStatusCode(request).Should().Be(200);
    }

    [Fact]
    public void GetHttpStatusCode_EnforceDisabled_Returns200EvenIfMissing()
    {
        var strategy = new ResponsesResponseStrategy(null, "default", enforceThinkingRoundTrip: false);
        var request = Parse("""
        {
            "model": "deepseek-v4-flash",
            "reasoning": { "effort": "high" },
            "input": [
                { "type": "message", "role": "assistant", "content": [{"type":"output_text","text":"我看看"}] }
            ]
        }
        """);

        strategy.GetHttpStatusCode(request).Should().Be(200);
    }

    [Fact]
    public void BuildResponse_When400_ReturnsDeepSeekThinkingRoundTripError()
    {
        var strategy = new ResponsesResponseStrategy(null, "default", enforceThinkingRoundTrip: true);
        var request = Parse("""
        {
            "model": "deepseek-v4-flash",
            "reasoning": { "effort": "high" },
            "input": [
                { "type": "message", "role": "assistant", "content": [{"type":"output_text","text":"我看看"}] }
            ]
        }
        """);

        strategy.GetHttpStatusCode(request).Should().Be(400);
        var body = strategy.BuildResponse(request, new CacheStats { CacheCreationTokens = 0, CacheReadTokens = 0, InputTokens = 0, OutputTokens = 0 });
        body.Should().Contain("invalid_request_error");
        body.Should().Contain("The reasoning_text in the thinking mode must be passed back to the API.");
    }
}