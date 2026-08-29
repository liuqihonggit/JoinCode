namespace Core.Agents.Tests.Unit.Agents;


/// <summary>
/// BedrockModelHelper 单元测试 — 对齐 TS 原版 src/utils/model/bedrock.ts
/// <para>覆盖: IsFoundationModel、ExtractModelIdFromArn、GetBedrockRegionPrefix、ApplyBedrockRegionPrefix、ApplyParentRegionPrefix</para>
/// </summary>
public sealed class BedrockModelHelperTests
{
    #region IsFoundationModel

    [Theory]
    [InlineData("anthropic.claude-sonnet-4-5-20250929-v1:0", true)]
    [InlineData("anthropic.claude-opus-4-6-v1", true)]
    [InlineData("eu.anthropic.claude-sonnet-4-5-v1:0", false)]
    [InlineData("claude-sonnet-4-5-20250929", false)]
    [InlineData("gpt-4o", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsFoundationModel_VariousInputs(string? modelId, bool expected)
    {
        BedrockModelHelper.IsFoundationModel(modelId!).Should().Be(expected);
    }

    #endregion

    #region ExtractModelIdFromArn

    [Fact]
    public void ExtractModelIdFromArn_NonArn_ReturnsOriginal()
    {
        BedrockModelHelper.ExtractModelIdFromArn("anthropic.claude-sonnet-4-5-v1:0")
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ExtractModelIdFromArn_InferenceProfileArn_ReturnsProfileId()
    {
        BedrockModelHelper.ExtractModelIdFromArn("arn:aws:bedrock:us-east-1:123:inference-profile/eu.anthropic.claude-opus-4-6-v1")
            .Should().Be("eu.anthropic.claude-opus-4-6-v1");
    }

    [Fact]
    public void ExtractModelIdFromArn_ApplicationInferenceProfileArn_ReturnsProfileId()
    {
        BedrockModelHelper.ExtractModelIdFromArn("arn:aws:bedrock:us-east-1:123:application-inference-profile/my-profile")
            .Should().Be("my-profile");
    }

    [Fact]
    public void ExtractModelIdFromArn_FoundationModelArn_ReturnsModelId()
    {
        BedrockModelHelper.ExtractModelIdFromArn("arn:aws:bedrock:us-east-1::foundation-model/anthropic.claude-sonnet-4-5-v1:0")
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ExtractModelIdFromArn_ArnWithoutSlash_ReturnsOriginal()
    {
        BedrockModelHelper.ExtractModelIdFromArn("arn:aws:bedrock:us-east-1:123")
            .Should().Be("arn:aws:bedrock:us-east-1:123");
    }

    #endregion

    #region GetBedrockRegionPrefix

    [Theory]
    [InlineData("eu.anthropic.claude-sonnet-4-5-20250929-v1:0", "eu")]
    [InlineData("us.anthropic.claude-3-7-sonnet-20250219-v1:0", "us")]
    [InlineData("apac.anthropic.claude-opus-4-6-v1", "apac")]
    [InlineData("global.anthropic.claude-opus-4-6-v1", "global")]
    public void GetBedrockRegionPrefix_WithPrefix_ReturnsPrefix(string modelId, string expected)
    {
        BedrockModelHelper.GetBedrockRegionPrefix(modelId).Should().Be(expected);
    }

    [Fact]
    public void GetBedrockRegionPrefix_FromArn_ReturnsPrefix()
    {
        BedrockModelHelper.GetBedrockRegionPrefix("arn:aws:bedrock:ap-northeast-2:123:inference-profile/global.anthropic.claude-opus-4-6-v1")
            .Should().Be("global");
    }

    [Theory]
    [InlineData("anthropic.claude-3-5-sonnet-20241022-v2:0", null)]
    [InlineData("claude-sonnet-4-5-20250929", null)]
    [InlineData("gpt-4o", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetBedrockRegionPrefix_NoPrefix_ReturnsNull(string? modelId, string? expected)
    {
        BedrockModelHelper.GetBedrockRegionPrefix(modelId!).Should().Be(expected);
    }

    #endregion

    #region ApplyBedrockRegionPrefix

    [Fact]
    public void ApplyBedrockRegionPrefix_ReplaceExistingPrefix()
    {
        BedrockModelHelper.ApplyBedrockRegionPrefix("us.anthropic.claude-sonnet-4-5-v1:0", "eu")
            .Should().Be("eu.anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyBedrockRegionPrefix_AddPrefixToFoundationModel()
    {
        BedrockModelHelper.ApplyBedrockRegionPrefix("anthropic.claude-sonnet-4-5-v1:0", "eu")
            .Should().Be("eu.anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyBedrockRegionPrefix_NonBedrockModel_ReturnsOriginal()
    {
        BedrockModelHelper.ApplyBedrockRegionPrefix("claude-sonnet-4-5-20250929", "eu")
            .Should().Be("claude-sonnet-4-5-20250929");
    }

    [Fact]
    public void ApplyBedrockRegionPrefix_EmptyPrefix_ReturnsOriginal()
    {
        BedrockModelHelper.ApplyBedrockRegionPrefix("anthropic.claude-sonnet-4-5-v1:0", "")
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyBedrockRegionPrefix_EmptyModelId_ReturnsOriginal()
    {
        BedrockModelHelper.ApplyBedrockRegionPrefix("", "eu").Should().Be("");
    }

    #endregion

    #region ApplyParentRegionPrefix

    [Fact]
    public void ApplyParentRegionPrefix_NoParentPrefix_ReturnsResolved()
    {
        BedrockModelHelper.ApplyParentRegionPrefix("anthropic.claude-sonnet-4-5-v1:0", "sonnet", null, true)
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyParentRegionPrefix_NotBedrockProvider_ReturnsResolved()
    {
        BedrockModelHelper.ApplyParentRegionPrefix("anthropic.claude-sonnet-4-5-v1:0", "sonnet", "eu", false)
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyParentRegionPrefix_BedrockWithParentPrefix_AppliesPrefix()
    {
        BedrockModelHelper.ApplyParentRegionPrefix("anthropic.claude-sonnet-4-5-v1:0", "sonnet", "eu", true)
            .Should().Be("eu.anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ApplyParentRegionPrefix_OriginalSpecHasOwnPrefix_PreservesOriginal()
    {
        BedrockModelHelper.ApplyParentRegionPrefix("us.anthropic.claude-sonnet-4-5-v1:0", "us.anthropic.claude-sonnet-4-5-v1:0", "eu", true)
            .Should().Be("us.anthropic.claude-sonnet-4-5-v1:0");
    }

    #endregion
}
