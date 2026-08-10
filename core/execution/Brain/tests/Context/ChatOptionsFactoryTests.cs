namespace Brain.Tests.Context;

/// <summary>
/// ChatOptionsFactory 执行设置测试 — 验证 Temperature/MaxTokens 从
/// IExecutionSettingsProvider 覆盖默认 LlmParameters.Chat（GUI 滑块接入引擎的契约）。
/// </summary>
public sealed class ChatOptionsFactoryTests
{
    private static ChatOptionsFactory CreateFactory(IExecutionSettingsProvider? provider = null)
    {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetDiscoveredTools()).Returns(new DiscoveredToolSet());
        contextManager.Setup(c => c.GetDeferredTools()).Returns(Array.Empty<DeferredToolInfo>());
        return new ChatOptionsFactory(contextManager.Object, provider);
    }

    [Fact]
    public void Create_WithProviderTemperatureAndMaxTokens_OverridesLlmParameters()
    {
        var provider = new Mock<IExecutionSettingsProvider>();
        provider.Setup(p => p.Temperature).Returns(1.2f);
        provider.Setup(p => p.MaxTokens).Returns(5000);

        var options = CreateFactory(provider.Object).Create();

        options.Temperature.Should().Be(1.2f);
        options.MaxTokens.Should().Be(5000);
    }

    [Fact]
    public void Create_WithoutProviderTemperatureAndMaxTokens_FallsBackToLlmParameters()
    {
        var provider = new Mock<IExecutionSettingsProvider>();
        provider.Setup(p => p.Temperature).Returns((float?)null);
        provider.Setup(p => p.MaxTokens).Returns((int?)null);

        var options = CreateFactory(provider.Object).Create();

        options.Temperature.Should().Be(LlmParameters.Chat.Temperature);
        options.MaxTokens.Should().Be(LlmParameters.Chat.MaxTokens);
    }

    [Fact]
    public void Create_WithoutProvider_FallsBackToLlmParameters()
    {
        var options = CreateFactory().Create();

        options.Temperature.Should().Be(LlmParameters.Chat.Temperature);
        options.MaxTokens.Should().Be(LlmParameters.Chat.MaxTokens);
    }
}
