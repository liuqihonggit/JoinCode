namespace Guard.Tests.Security;

/// <summary>
/// SensitiveFilePattern 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / SensitiveFilePatternConstants 常量值 / 大小写不敏感 / RoundTrip
/// 6 个枚举值 (EnvFiles/CryptoKeys/SshKeys/Credentials/PackageRegistries/SecretsConfig)
/// </summary>
public sealed class SensitiveFilePatternExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_EnvFiles_Should_Return_env_files()
    {
        SensitiveFilePattern.EnvFiles.ToValue().Should().Be("env-files");
    }

    [Fact]
    public void ToValue_CryptoKeys_Should_Return_crypto_keys()
    {
        SensitiveFilePattern.CryptoKeys.ToValue().Should().Be("crypto-keys");
    }

    [Fact]
    public void ToValue_SshKeys_Should_Return_ssh_keys()
    {
        SensitiveFilePattern.SshKeys.ToValue().Should().Be("ssh-keys");
    }

    [Fact]
    public void ToValue_Credentials_Should_Return_credentials()
    {
        SensitiveFilePattern.Credentials.ToValue().Should().Be("credentials");
    }

    [Fact]
    public void ToValue_PackageRegistries_Should_Return_package_registries()
    {
        SensitiveFilePattern.PackageRegistries.ToValue().Should().Be("package-registries");
    }

    [Fact]
    public void ToValue_SecretsConfig_Should_Return_secrets_config()
    {
        SensitiveFilePattern.SecretsConfig.ToValue().Should().Be("secrets-config");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("env-files", SensitiveFilePattern.EnvFiles)]
    [InlineData("crypto-keys", SensitiveFilePattern.CryptoKeys)]
    [InlineData("ssh-keys", SensitiveFilePattern.SshKeys)]
    [InlineData("credentials", SensitiveFilePattern.Credentials)]
    [InlineData("package-registries", SensitiveFilePattern.PackageRegistries)]
    [InlineData("secrets-config", SensitiveFilePattern.SecretsConfig)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, SensitiveFilePattern expected)
    {
        SensitiveFilePatternExtensions.FromValue(input).Should().Be(expected);
    }

    // ===== 大小写不敏感测试 =====

    [Theory]
    [InlineData("ENV-FILES", SensitiveFilePattern.EnvFiles)]
    [InlineData("Env-Files", SensitiveFilePattern.EnvFiles)]
    [InlineData("CRYPTO-KEYS", SensitiveFilePattern.CryptoKeys)]
    [InlineData("Credentials", SensitiveFilePattern.Credentials)]
    [InlineData("PACKAGE-REGISTRIES", SensitiveFilePattern.PackageRegistries)]
    [InlineData("Secrets-Config", SensitiveFilePattern.SecretsConfig)]
    [InlineData("VCS-INTERNAL", SensitiveFilePattern.VcsInternal)]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input, SensitiveFilePattern expected)
    {
        SensitiveFilePatternExtensions.FromValue(input).Should().Be(expected);
    }

    // ===== 无效输入测试 =====

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        SensitiveFilePatternExtensions.FromValue("invalid-category").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        SensitiveFilePatternExtensions.FromValue(string.Empty).Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        SensitiveFilePatternExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(SensitiveFilePattern.EnvFiles, true)]
    [InlineData(SensitiveFilePattern.CryptoKeys, true)]
    [InlineData(SensitiveFilePattern.SshKeys, true)]
    [InlineData(SensitiveFilePattern.Credentials, true)]
    [InlineData(SensitiveFilePattern.PackageRegistries, true)]
    [InlineData(SensitiveFilePattern.SecretsConfig, true)]
    [InlineData(SensitiveFilePattern.VcsInternal, true)]
    public void IsDefined_AllValidValues_Should_Return_True(SensitiveFilePattern value, bool expected)
    {
        SensitiveFilePatternExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== SensitiveFilePatternConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_ToValue_For_All_Values()
    {
        SensitiveFilePatternConstants.EnvFiles.Should().Be("env-files");
        SensitiveFilePatternConstants.CryptoKeys.Should().Be("crypto-keys");
        SensitiveFilePatternConstants.SshKeys.Should().Be("ssh-keys");
        SensitiveFilePatternConstants.Credentials.Should().Be("credentials");
        SensitiveFilePatternConstants.PackageRegistries.Should().Be("package-registries");
        SensitiveFilePatternConstants.SecretsConfig.Should().Be("secrets-config");
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(SensitiveFilePattern.EnvFiles)]
    [InlineData(SensitiveFilePattern.CryptoKeys)]
    [InlineData(SensitiveFilePattern.SshKeys)]
    [InlineData(SensitiveFilePattern.Credentials)]
    [InlineData(SensitiveFilePattern.PackageRegistries)]
    [InlineData(SensitiveFilePattern.SecretsConfig)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(SensitiveFilePattern value)
    {
        var str = value.ToValue();
        SensitiveFilePatternExtensions.FromValue(str).Should().Be(value);
    }

    // ===== GetAllValues 计数测试 =====

    [Fact]
    public void AllCategories_Should_Be_7()
    {
        var values = Enum.GetValues<SensitiveFilePattern>();
        values.Should().HaveCount(7);
    }
}
