using JoinCode.Abstractions.Configuration.Providers;

namespace Llm.Tests.Adapters;

public class QueryServiceBaseTests
{
    [Fact]
    public void ProviderConfig_HasRequiredProperties()
    {
        var config = new ProviderConfig
        {
            Vendor = VendorKind.OpenAi.ToValue(),
            ModelId = "gpt-4o",
            ApiKey = "sk-test"
        };

        config.VendorKind.Should().Be(VendorKind.OpenAi);
        config.ModelId.Should().Be("gpt-4o");
        config.ApiKey.Should().Be("sk-test");
    }

    [Fact]
    public void VendorKind_EnumValues_MatchExpected()
    {
        var values = Enum.GetValues<VendorKind>();
        values.Should().Contain(VendorKind.OpenAi);
        values.Should().Contain(VendorKind.Anthropic);
        values.Should().Contain(VendorKind.Azure);
    }
}
