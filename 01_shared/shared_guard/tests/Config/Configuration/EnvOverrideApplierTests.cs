
namespace Core.Tests.Configuration;

/// <summary>
/// EnvOverrideApplier 推断逻辑单元测试 — 锁定 vendor → protocol/apiKeyEnvVar 映射行为
/// 覆盖: 7 个已知 vendor + null/空/未知 + 大小写不敏感
/// </summary>
public class EnvOverrideApplierTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("unknown", "openai-compatible")]
    [InlineData("openai", "openai-compatible")]
    [InlineData("deepseek", "openai-compatible")]
    [InlineData("agnes", "openai-compatible")]
    [InlineData("sensenova", "openai-compatible")]
    [InlineData("bedrock", "openai-compatible")]
    [InlineData("anthropic", "anthropic")]
    [InlineData("azure", "azure")]
    [InlineData("ANTHROPIC", "anthropic")]
    [InlineData("Azure", "azure")]
    public void InferProtocol_ReturnsExpected(string? vendor, string? expected)
    {
        EnvOverrideApplier.InferProtocol(vendor).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("unknown", null)]
    [InlineData("bedrock", null)]
    [InlineData("openai", "OPENAI_API_KEY")]
    [InlineData("anthropic", "ANTHROPIC_API_KEY")]
    [InlineData("azure", "AZURE_OPENAI_API_KEY")]
    [InlineData("deepseek", "DEEPSEEK_API_KEY")]
    [InlineData("agnes", "AGNES_API_KEY")]
    [InlineData("sensenova", "SENSENOVA_API_KEY")]
    [InlineData("OpenAI", "OPENAI_API_KEY")]
    [InlineData("DeepSeek", "DEEPSEEK_API_KEY")]
    public void InferApiKeyEnvVar_ReturnsExpected(string? vendor, string? expected)
    {
        EnvOverrideApplier.InferApiKeyEnvVar(vendor).Should().Be(expected);
    }
}
