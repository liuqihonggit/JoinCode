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
}
