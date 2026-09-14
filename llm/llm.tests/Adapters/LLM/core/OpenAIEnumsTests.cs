namespace Llm.Tests.Adapters.LLM;

public class OpenAIEnumsTests
{
    [Theory]
    [InlineData(OpenAIFinishReason.Stop, "stop")]
    [InlineData(OpenAIFinishReason.Length, "length")]
    [InlineData(OpenAIFinishReason.ToolCalls, "tool_calls")]
    [InlineData(OpenAIFinishReason.ContentFilter, "content_filter")]
    public void OpenAIFinishReason_ToValue_MapsToExpectedString(OpenAIFinishReason reason, string expected)
    {
        reason.ToValue().Should().Be(expected);
    }

    [Theory]
    [InlineData("stop", OpenAIFinishReason.Stop)]
    [InlineData("length", OpenAIFinishReason.Length)]
    [InlineData("tool_calls", OpenAIFinishReason.ToolCalls)]
    [InlineData("content_filter", OpenAIFinishReason.ContentFilter)]
    public void OpenAIFinishReason_FromValue_MapsToExpectedEnum(string value, OpenAIFinishReason expected)
    {
        OpenAIFinishReasonExtensions.FromValue(value).Should().Be(expected);
    }

    [Fact]
    public void OpenAIFinishReason_FromValue_UnknownReturnsNull()
    {
        OpenAIFinishReasonExtensions.FromValue("unknown").Should().BeNull();
    }

    [Fact]
    public void OpenAIFinishReasonEnumConstants_MatchEnumValues()
    {
        OpenAIFinishReasonEnumConstants.Stop.Should().Be("stop");
        OpenAIFinishReasonEnumConstants.Length.Should().Be("length");
        OpenAIFinishReasonEnumConstants.ToolCalls.Should().Be("tool_calls");
        OpenAIFinishReasonEnumConstants.ContentFilter.Should().Be("content_filter");
    }
}
