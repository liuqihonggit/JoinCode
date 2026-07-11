
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Core.Tests.Configuration;

public class WorkflowConfigTests {
    [Fact]
    public void WorkflowConfig_DefaultValues_ShouldBeSet() {
        // Act
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                ApiKey = TestConfiguration.GetRealApiKey()
            }
        };

        // Assert
        Assert.Equal("gpt-4o", config.Provider.ModelId);
        Assert.Equal("workflow_state.json", config.StateFilePath);
        Assert.NotNull(config.Bridge);
    }

    [Fact]
    public void WorkflowConfig_CodeExecutionConfig_ShouldNotBeNull() {
        // Act
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                ApiKey = TestConfiguration.GetRealApiKey()
            }
        };

        // Assert
        Assert.NotNull(config.CodeExecution);
    }

    [Fact]
    public void WorkflowConfig_BridgeConfig_ShouldNotBeNull() {
        // Act
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                ApiKey = TestConfiguration.GetRealApiKey()
            }
        };

        // Assert
        Assert.NotNull(config.Bridge);
    }
}
