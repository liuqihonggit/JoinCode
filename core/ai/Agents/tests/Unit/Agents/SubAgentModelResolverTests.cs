namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;

/// <summary>
/// SubAgentModelResolver 单元测试 — 对齐 TS 原版 src/utils/model/agent.ts
/// <para>覆盖: IsInheritKeyword、GetAgentModelDisplay、AliasMatchesParentTier、ResolveModel</para>
/// </summary>
public sealed class SubAgentModelResolverTests
{
    #region IsInheritKeyword

    [Theory]
    [InlineData("inherit", true)]
    [InlineData("Inherit", true)]
    [InlineData("INHERIT", true)]
    [InlineData("  inherit  ", false)]
    [InlineData("inherits", false)]
    [InlineData("opus", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsInheritKeyword_VariousInputs(string? model, bool expected)
    {
        SubAgentModelResolver.IsInheritKeyword(model).Should().Be(expected);
    }

    #endregion

    #region GetAgentModelDisplay

    [Fact]
    public void GetAgentModelDisplay_Null_ReturnsDefaultInherit()
    {
        SubAgentModelResolver.GetAgentModelDisplay(null).Should().Be("Inherit from parent (default)");
    }

    [Fact]
    public void GetAgentModelDisplay_Empty_ReturnsDefaultInherit()
    {
        SubAgentModelResolver.GetAgentModelDisplay("").Should().Be("Inherit from parent (default)");
    }

    [Theory]
    [InlineData("inherit", "Inherit from parent")]
    [InlineData("Inherit", "Inherit from parent")]
    [InlineData("INHERIT", "Inherit from parent")]
    public void GetAgentModelDisplay_InheritKeyword_ReturnsInheritFromParent(string model, string expected)
    {
        SubAgentModelResolver.GetAgentModelDisplay(model).Should().Be(expected);
    }

    [Theory]
    [InlineData("opus", "Opus")]
    [InlineData("sonnet", "Sonnet")]
    [InlineData("haiku", "Haiku")]
    [InlineData("gpt-4o", "Gpt-4o")]
    [InlineData("g", "G")]
    public void GetAgentModelDisplay_OtherModels_ReturnsCapitalized(string model, string expected)
    {
        SubAgentModelResolver.GetAgentModelDisplay(model).Should().Be(expected);
    }

    #endregion

    #region AliasMatchesParentTier

    [Theory]
    [InlineData("opus", "claude-opus-4-6", true)]
    [InlineData("opus", "claude-sonnet-4-6", false)]
    [InlineData("sonnet", "claude-sonnet-4-6", true)]
    [InlineData("sonnet", "claude-opus-4-6", false)]
    [InlineData("haiku", "claude-3-5-haiku-20241022", true)]
    [InlineData("haiku", "claude-opus-4-6", false)]
    [InlineData("inherit", "claude-opus-4-6", false)]
    [InlineData("opus[1m]", "claude-opus-4-6", false)]
    [InlineData("best", "claude-opus-4-6", false)]
    [InlineData(null, "claude-opus-4-6", false)]
    [InlineData("opus", null, false)]
    public void AliasMatchesParentTier_VariousInputs(string? alias, string? parentModel, bool expected)
    {
        SubAgentModelResolver.AliasMatchesParentTier(alias, parentModel!).Should().Be(expected);
    }

    #endregion

    #region ResolveModel

    [Fact]
    public void ResolveModel_SpawnModelTakesPrecedence()
    {
        SubAgentModelResolver.ResolveModel("spawn-model", "definition-model", "parent-model")
            .Should().Be("spawn-model");
    }

    [Fact]
    public void ResolveModel_FallsBackToDefinitionModel()
    {
        SubAgentModelResolver.ResolveModel(null, "definition-model", "parent-model")
            .Should().Be("definition-model");
    }

    [Fact]
    public void ResolveModel_BothNull_ReturnsParentModel()
    {
        SubAgentModelResolver.ResolveModel(null, null, "parent-model")
            .Should().Be("parent-model");
    }

    [Theory]
    [InlineData("inherit")]
    [InlineData("Inherit")]
    [InlineData("INHERIT")]
    public void ResolveModel_SpawnModelIsInherit_ReturnsParentModel(string inheritKeyword)
    {
        SubAgentModelResolver.ResolveModel(inheritKeyword, "definition-model", "parent-model")
            .Should().Be("parent-model");
    }

    [Theory]
    [InlineData("inherit")]
    [InlineData("Inherit")]
    public void ResolveModel_DefinitionModelIsInherit_ReturnsParentModel(string inheritKeyword)
    {
        SubAgentModelResolver.ResolveModel(null, inheritKeyword, "parent-model")
            .Should().Be("parent-model");
    }

    [Fact]
    public void ResolveModel_InheritWithNullParent_ReturnsNull()
    {
        SubAgentModelResolver.ResolveModel("inherit", null, null)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveModel_AliasMatchesParentTier_ReturnsParentModel()
    {
        SubAgentModelResolver.ResolveModel("opus", null, "claude-opus-4-6")
            .Should().Be("claude-opus-4-6");
    }

    [Fact]
    public void ResolveModel_AliasDoesNotMatchParentTier_ReturnsAlias()
    {
        SubAgentModelResolver.ResolveModel("opus", null, "claude-sonnet-4-6")
            .Should().Be("opus");
    }

    [Fact]
    public void ResolveModel_SpawnModelAliasMatchesParentTier_ReturnsParentModel()
    {
        SubAgentModelResolver.ResolveModel("sonnet", "opus", "claude-sonnet-4-6")
            .Should().Be("claude-sonnet-4-6");
    }

    #endregion

    #region ResolveModelWithBedrock

    [Fact]
    public void ResolveModelWithBedrock_Inherit_ReturnsParentModel_NoPrefixApplied()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("inherit", null, "eu.anthropic.claude-opus-4-6-v1", "eu", true)
            .Should().Be("eu.anthropic.claude-opus-4-6-v1");
    }

    [Fact]
    public void ResolveModelWithBedrock_AliasMatchesParentTier_ReturnsParentModel_NoPrefixApplied()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("opus", null, "eu.anthropic.claude-opus-4-6-v1", "eu", true)
            .Should().Be("eu.anthropic.claude-opus-4-6-v1");
    }

    [Fact]
    public void ResolveModelWithBedrock_BedrockProvider_AppliesParentPrefix()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("anthropic.claude-sonnet-4-5-v1:0", null, "eu.anthropic.claude-opus-4-6-v1", "eu", true)
            .Should().Be("eu.anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ResolveModelWithBedrock_NonBedrockProvider_NoPrefixApplied()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("anthropic.claude-sonnet-4-5-v1:0", null, "eu.anthropic.claude-opus-4-6-v1", "eu", false)
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ResolveModelWithBedrock_NoParentPrefix_NoPrefixApplied()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("anthropic.claude-sonnet-4-5-v1:0", null, "anthropic.claude-opus-4-6-v1", null, true)
            .Should().Be("anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ResolveModelWithBedrock_OriginalSpecHasOwnPrefix_PreservesOriginal()
    {
        SubAgentModelResolver.ResolveModelWithBedrock("us.anthropic.claude-sonnet-4-5-v1:0", null, "eu.anthropic.claude-opus-4-6-v1", "eu", true)
            .Should().Be("us.anthropic.claude-sonnet-4-5-v1:0");
    }

    [Fact]
    public void ResolveModelWithBedrock_NullSpawnAndDefinition_ReturnsParentModel()
    {
        SubAgentModelResolver.ResolveModelWithBedrock(null, null, "eu.anthropic.claude-opus-4-6-v1", "eu", true)
            .Should().Be("eu.anthropic.claude-opus-4-6-v1");
    }

    #endregion
}
