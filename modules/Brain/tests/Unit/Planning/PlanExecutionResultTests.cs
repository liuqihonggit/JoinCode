
namespace Core.Tests.Models;

public class PlanExecutionResultTests {
    [Fact]
    public void PlanExecutionResult_DefaultValues_ShouldBeSet() {
        // Arrange & Act
        var result = new PlanExecutionResult {
            Prompt = "Test prompt"
        };

        // Assert
        Assert.Equal("Test prompt", result.Prompt);
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Result);
        Assert.Equal(0, result.ExecutionTimeMs);
        Assert.NotEqual(default, result.Timestamp);
        Assert.NotNull(result.TokenUsage);
        Assert.NotNull(result.FunctionCalls);
    }

    [Fact]
    public void PlanExecutionResult_TokenUsage_ShouldHaveDefaultValues() {
        // Arrange & Act
        var result = new PlanExecutionResult();

        // Assert
        Assert.Equal(0, result.TokenUsage.PromptTokens);
        Assert.Equal(0, result.TokenUsage.CompletionTokens);
        Assert.Equal(0, result.TokenUsage.TotalTokens);
    }

    [Fact]
    public void PlanExecutionResult_SetValues_ShouldBeStored() {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var result = new PlanExecutionResult {
            Prompt = "Test prompt",
            Success = true,
            Result = "Test result",
            ExecutionTimeMs = 1500,
            Timestamp = timestamp,
            TokenUsage = new TokenUsage {
                PromptTokens = 100,
                CompletionTokens = 50
            }
        };

        // Assert
        Assert.Equal("Test prompt", result.Prompt);
        Assert.True(result.Success);
        Assert.Equal("Test result", result.Result);
        Assert.Equal(1500, result.ExecutionTimeMs);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(100, result.TokenUsage.PromptTokens);
        Assert.Equal(50, result.TokenUsage.CompletionTokens);
        Assert.Equal(150, result.TokenUsage.TotalTokens);
    }

    [Fact]
    public void TokenUsage_TotalTokens_ShouldBeSumOfPromptAndCompletion() {
        // Arrange
        var tokenUsage = new TokenUsage {
            PromptTokens = 100,
            CompletionTokens = 50
        };

        // Act & Assert
        Assert.Equal(150, tokenUsage.TotalTokens);
        Assert.Equal(tokenUsage.PromptTokens + tokenUsage.CompletionTokens, tokenUsage.TotalTokens);
    }

    [Fact]
    public void FunctionCallInfo_DefaultValues_ShouldBeSet() {
        // Arrange & Act
        var functionCall = new FunctionCallInfo();

        // Assert
        Assert.Equal(string.Empty, functionCall.PluginName);
        Assert.Equal(string.Empty, functionCall.FunctionName);
        Assert.NotNull(functionCall.Arguments);
        Assert.Equal(string.Empty, functionCall.Result);
        Assert.Equal(0, functionCall.ExecutionTimeMs);
    }

    [Fact]
    public void FunctionCallInfo_SetValues_ShouldBeStored() {
        // Arrange
        var arguments = new Dictionary<string, JsonElement> { { "key", JsonElementHelper.FromString("value") } };

        // Act
        var functionCall = new FunctionCallInfo {
            PluginName = "TestPlugin",
            FunctionName = "TestFunction",
            Arguments = arguments,
            Result = "Test result",
            ExecutionTimeMs = 100
        };

        // Assert
        Assert.Equal("TestPlugin", functionCall.PluginName);
        Assert.Equal("TestFunction", functionCall.FunctionName);
        Assert.Equal(arguments, functionCall.Arguments);
        Assert.Equal("Test result", functionCall.Result);
        Assert.Equal(100, functionCall.ExecutionTimeMs);
    }

    [Fact]
    public void PlanExecutionResult_WithFunctionCalls_ShouldStoreCalls() {
        // Arrange
        var result = new PlanExecutionResult();
        var functionCall = new FunctionCallInfo {
            PluginName = "CodeGeneration",
            FunctionName = "generate_csharp_code",
            Result = "generated code"
        };

        // Act
        result.FunctionCalls.Add(functionCall);

        // Assert
        Assert.Single(result.FunctionCalls);
        Assert.Equal("CodeGeneration", result.FunctionCalls[0].PluginName);
    }
}
