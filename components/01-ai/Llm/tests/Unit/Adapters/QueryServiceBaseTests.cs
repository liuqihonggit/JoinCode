using JoinCode.Abstractions.Configuration.Providers;

namespace Llm.Tests.Adapters;

public class QueryServiceBaseTests
{
    [Fact]
    public void ProviderConfig_HasRequiredProperties()
    {
        var config = new ProviderConfig
        {
            Provider = ProviderKind.OpenAI.ToValue(),
            ModelId = "gpt-4o",
            ApiKey = "sk-test"
        };

        config.Kind.Should().Be(ProviderKind.OpenAI);
        config.ModelId.Should().Be("gpt-4o");
        config.ApiKey.Should().Be("sk-test");
    }

    [Fact]
    public void ProviderKind_EnumValues_MatchExpected()
    {
        var values = Enum.GetValues<ProviderKind>();
        values.Should().Contain(ProviderKind.OpenAI);
        values.Should().Contain(ProviderKind.Anthropic);
        values.Should().Contain(ProviderKind.Azure);
    }
}
