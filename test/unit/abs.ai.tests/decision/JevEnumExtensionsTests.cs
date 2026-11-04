namespace Abs.Tests.Decision;

/// <summary>
/// Jev provider 枚举扩展测试 — ProtocolKind/VendorKind/ProviderEnvVar 的 Jev 值往返
/// </summary>
public class JevEnumExtensionsTests {
    [Fact]
    public void ProtocolKind_Jev_ToValue_ReturnsJev() {
        ProtocolKind.Jev.ToValue().Should().Be("jev");
    }

    [Fact]
    public void ProtocolKind_FromValue_Jev_ReturnsJevEnum() {
        ProtocolKindExtensions.FromValue("jev").Should().Be(ProtocolKind.Jev);
    }

    [Fact]
    public void ProtocolKindEnumConstants_Jev_HasCorrectValue() {
        ProtocolKindEnumConstants.Jev.Should().Be("jev");
    }

    [Fact]
    public void VendorKind_Jev_ToValue_ReturnsJev() {
        VendorKind.Jev.ToValue().Should().Be("jev");
    }

    [Fact]
    public void VendorKind_FromValue_Jev_ReturnsJevEnum() {
        VendorKindExtensions.FromValue("jev").Should().Be(VendorKind.Jev);
    }

    [Fact]
    public void VendorKindEnumConstants_Jev_HasCorrectValue() {
        VendorKindEnumConstants.Jev.Should().Be("jev");
    }

    [Fact]
    public void ProviderEnvVar_JevApiKey_ToValue_ReturnsCorrectEnvVar() {
        ProviderEnvVar.JevApiKey.ToValue().Should().Be("JEV_API_KEY");
    }

    [Fact]
    public void ProviderEnvVar_FromValue_JevApiKey_ReturnsCorrectEnum() {
        ProviderEnvVarExtensions.FromValue("JEV_API_KEY").Should().Be(ProviderEnvVar.JevApiKey);
    }
}
