
namespace Core.Tests.Configuration;

public class WorkflowConfigTests {

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
