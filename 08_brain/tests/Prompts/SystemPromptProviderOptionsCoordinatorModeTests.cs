namespace Core.Prompts;

public sealed class SystemPromptProviderOptionsCoordinatorModeTests
{
    [Fact]
    public void IsCoordinatorModeEnabledFromEnv_WhenEnvVarIs1_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "1");
        try
        {
            SystemPromptProviderOptions.IsCoordinatorModeEnabledFromEnv().Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [Fact]
    public void IsCoordinatorModeEnabledFromEnv_WhenEnvVarIsTrue_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "true");
        try
        {
            SystemPromptProviderOptions.IsCoordinatorModeEnabledFromEnv().Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [Fact]
    public void IsCoordinatorModeEnabledFromEnv_WhenEnvVarIsTrueUpperCase_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "TRUE");
        try
        {
            SystemPromptProviderOptions.IsCoordinatorModeEnabledFromEnv().Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [Fact]
    public void IsCoordinatorModeEnabledFromEnv_WhenEnvVarNotSet_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        SystemPromptProviderOptions.IsCoordinatorModeEnabledFromEnv().Should().BeFalse();
    }

    [Fact]
    public void IsCoordinatorModeEnabledFromEnv_WhenEnvVarIsZero_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", "0");
        try
        {
            SystemPromptProviderOptions.IsCoordinatorModeEnabledFromEnv().Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_COORDINATOR_MODE", null);
        }
    }

    [Fact]
    public void GetSubagentModelFromEnv_WhenEnvVarSet_ReturnsValue()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", "gpt-4o-mini");
        try
        {
            SystemPromptProviderOptions.GetSubagentModelFromEnv().Should().Be("gpt-4o-mini");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        }
    }

    [Fact]
    public void GetSubagentModelFromEnv_WhenEnvVarNotSet_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        SystemPromptProviderOptions.GetSubagentModelFromEnv().Should().BeNull();
    }

    [Theory]
    [InlineData("opus", "claude-opus-4-6", true)]
    [InlineData("opus", "gpt-4o", false)]
    [InlineData("sonnet", "claude-sonnet-4", true)]
    [InlineData("haiku", "claude-haiku-3", true)]
    [InlineData(null, "claude-opus-4", false)]
    [InlineData("opus", "", false)]
    public void ModelAliasMatchesParentTier(string? alias, string parentModel, bool expected)
    {
        SystemPromptProviderOptions.ModelAliasMatchesParentTier(alias, parentModel).Should().Be(expected);
    }
}
