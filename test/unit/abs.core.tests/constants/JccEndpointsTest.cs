namespace Abs.Tests.Constants;

/// <summary>
/// JccEndpoints 供应商端点常量确定性测试 — 验证新增常量值与原硬编码一致
/// <para>TASK031 阶段B4:常量委托统一(OpenAiApiBase/AnthropicApiBase/OpenAiTranscriptionsEndpoint)</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class JccEndpointsTest {
    [Fact]
    public void OpenAiApiBase_ShouldBeExpectedValue() {
        JccEndpoints.OpenAiApiBase.Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void AnthropicApiBase_ShouldBeExpectedValue() {
        JccEndpoints.AnthropicApiBase.Should().Be("https://api.anthropic.com/");
    }

    [Fact]
    public void OpenAiTranscriptionsEndpoint_ShouldBeExpectedValue() {
        JccEndpoints.OpenAiTranscriptionsEndpoint.Should().Be("https://api.openai.com/v1/audio/transcriptions");
    }

    [Fact]
    public void GitHubApiBase_ShouldBeExpectedValue() {
        JccEndpoints.GitHubApiBase.Should().Be("https://api.github.com");
    }

    [Fact]
    public void OpenAiTranscriptionsEndpoint_ShouldStartWithOpenAiApiBase() {
        JccEndpoints.OpenAiTranscriptionsEndpoint.Should().StartWith(JccEndpoints.OpenAiApiBase);
    }

    [Fact]
    public void AnthropicApiBase_ShouldEndWithSlash() {
        JccEndpoints.AnthropicApiBase.Should().EndWith("/");
    }

    [Fact]
    public void OpenAiApiBase_ShouldEndWithSlash() {
        JccEndpoints.OpenAiApiBase.Should().EndWith("/");
    }

    [Fact]
    public void GitHubApiBase_ShouldNotEndWithSlash() {
        JccEndpoints.GitHubApiBase.Should().NotEndWith("/");
    }

    [Fact]
    public void AnthropicApiBase_TrimEnd_ShouldBeNoSlash() {
        JccEndpoints.AnthropicApiBase.TrimEnd('/').Should().Be("https://api.anthropic.com");
    }
}
