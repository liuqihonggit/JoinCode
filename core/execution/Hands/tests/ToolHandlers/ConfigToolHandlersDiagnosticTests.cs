namespace Hands.Tests.ToolHandlers;

/// <summary>
/// ConfigToolHandlers 诊断方法单元测试
/// </summary>
public class ConfigToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildInvalidBooleanValueDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ConfigToolHandlers.BuildInvalidBooleanValueDiagnostic("theme", "maybe");
        diagnostic.Reason.Should().Be("InvalidBooleanValue");
        diagnostic.FormattedMessage.Should().Contain("theme requires true or false.");
        diagnostic.FormattedMessage.Should().Contain("[诊断] 提供的值: \"maybe\"");
        diagnostic.Details.Should().Contain(d => d.Key == "setting" && d.Value == "theme");
        diagnostic.Details.Should().Contain(d => d.Key == "providedValue" && d.Value == "maybe");
        diagnostic.Details.Should().Contain(d => d.Key == "expectedType" && d.Value == "boolean");
        diagnostic.Suggestions.Should().ContainSingle(s => s.Contains("true") && s.Contains("false"));
    }

    [Fact]
    public void BuildInvalidOptionValueDiagnostic_ReturnsCorrectStructure()
    {
        var options = new[] { "dark", "light" };
        var diagnostic = ConfigToolHandlers.BuildInvalidOptionValueDiagnostic("theme", "purple", options);
        diagnostic.Reason.Should().Be("InvalidOptionValue");
        diagnostic.FormattedMessage.Should().Contain("Invalid value \"purple\"");
        diagnostic.FormattedMessage.Should().Contain("dark, light");
        diagnostic.Details.Should().Contain(d => d.Key == "setting" && d.Value == "theme");
        diagnostic.Details.Should().Contain(d => d.Key == "providedValue" && d.Value == "purple");
        diagnostic.Details.Should().Contain(d => d.Key == "allowedOptions");
        diagnostic.Suggestions.Should().ContainSingle(s => s.Contains("dark") && s.Contains("light"));
    }

    [Fact]
    public void BuildValidateOnWriteFailedDiagnostic_WithError_ReturnsCorrectStructure()
    {
        var diagnostic = ConfigToolHandlers.BuildValidateOnWriteFailedDiagnostic("model", "gpt-999", "Model not found");
        diagnostic.Reason.Should().Be("ValidateOnWriteFailed");
        diagnostic.FormattedMessage.Should().Contain("Model not found");
        diagnostic.FormattedMessage.Should().Contain("[诊断] 设置项: model");
        diagnostic.Details.Should().Contain(d => d.Key == "setting" && d.Value == "model");
        diagnostic.Details.Should().Contain(d => d.Key == "value" && d.Value == "gpt-999");
        diagnostic.Details.Should().Contain(d => d.Key == "validationError" && d.Value == "Model not found");
    }

    [Fact]
    public void BuildValidateOnWriteFailedDiagnostic_WithNullError_DefaultsToValidationFailed()
    {
        var diagnostic = ConfigToolHandlers.BuildValidateOnWriteFailedDiagnostic("model", "gpt-999", null);
        diagnostic.FormattedMessage.Should().Contain("Validation failed");
        diagnostic.Details.Should().Contain(d => d.Key == "validationError" && d.Value == "Validation failed");
    }

    [Fact]
    public void BuildSetFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ConfigToolHandlers.BuildSetFailedDiagnostic("theme", "dark");
        diagnostic.Reason.Should().Be("SetFailed");
        diagnostic.FormattedMessage.Should().Contain("Failed to set theme");
        diagnostic.FormattedMessage.Should().Contain("[诊断] 设置项: theme");
        diagnostic.FormattedMessage.Should().Contain("[诊断] 尝试写入的值: \"dark\"");
        diagnostic.Details.Should().Contain(d => d.Key == "setting" && d.Value == "theme");
        diagnostic.Details.Should().Contain(d => d.Key == "value" && d.Value == "dark");
        diagnostic.Suggestions.Should().HaveCount(2);
    }
}
