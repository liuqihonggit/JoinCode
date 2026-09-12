namespace Hands.Tests.ToolHandlers;

/// <summary>
/// ToolCreationToolHandlers 诊断方法单元测试
/// </summary>
public class ToolCreationToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildEmptyNameOrDescriptionDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ToolCreationToolHandlers.BuildEmptyNameOrDescriptionDiagnostic();
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("工具名称和描述不能为空");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildInvalidToolNameDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ToolCreationToolHandlers.BuildInvalidToolNameDiagnostic("bad name!");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "tool_name" && d.Value == "bad name!");
    }

    [Fact]
    public void BuildTemplateNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ToolCreationToolHandlers.BuildTemplateNotFoundDiagnostic("my_template");
        diagnostic.Reason.Should().Be("模板未找到");
        diagnostic.Details.Should().Contain(d => d.Key == "template_id" && d.Value == "my_template");
    }
}

/// <summary>
/// StructuredOutputToolHandler 诊断方法单元测试
/// </summary>
public class StructuredOutputToolHandlerDiagnosticTests
{
    [Fact]
    public void BuildValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = StructuredOutputToolHandler.BuildValidationErrorDiagnostic("schema_name is required");
        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("schema_name is required");
    }

    [Fact]
    public void BuildInvalidSchemaDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = StructuredOutputToolHandler.BuildInvalidSchemaDiagnostic("type must be string");
        diagnostic.Reason.Should().Be("Schema验证失败");
        diagnostic.FormattedMessage.Should().Contain("Invalid JSON Schema");
    }

    [Fact]
    public void BuildSchemaNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = StructuredOutputToolHandler.BuildSchemaNotFoundDiagnostic("my_schema");
        diagnostic.Reason.Should().Be("Schema未找到");
        diagnostic.Details.Should().Contain(d => d.Key == "schema_name" && d.Value == "my_schema");
    }

    [Fact]
    public void BuildValidationFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = StructuredOutputToolHandler.BuildValidationFailedDiagnostic("my_schema", 3);
        diagnostic.Reason.Should().Be("内容验证失败");
        diagnostic.Details.Should().Contain(d => d.Key == "error_count" && d.Value == "3");
    }
}

/// <summary>
/// TimeoutRecoveryToolHandlers 诊断方法单元测试
/// </summary>
public class TimeoutRecoveryToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildTaskFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = TimeoutRecoveryToolHandlers.BuildTaskFailedDiagnostic("dotnet build", 1);
        diagnostic.Reason.Should().Be("任务失败");
        diagnostic.Details.Should().Contain(d => d.Key == "exit_code" && d.Value == "1");
    }

    [Fact]
    public void BuildTimedOutDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = TimeoutRecoveryToolHandlers.BuildTimedOutDiagnostic("dotnet test", "task-001", 2);
        diagnostic.Reason.Should().Be("任务超时");
        diagnostic.Details.Should().Contain(d => d.Key == "task_id" && d.Value == "task-001");
        diagnostic.Details.Should().Contain(d => d.Key == "retry_count" && d.Value == "2");
        diagnostic.Suggestions.Should().HaveCount(3);
    }

    [Fact]
    public void BuildTaskNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = TimeoutRecoveryToolHandlers.BuildTaskNotFoundDiagnostic();
        diagnostic.Reason.Should().Be("任务不存在");
    }

    [Fact]
    public void BuildMaxRetriesExceededDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = TimeoutRecoveryToolHandlers.BuildMaxRetriesExceededDiagnostic("dotnet test", 5);
        diagnostic.Reason.Should().Be("最大重试次数超出");
        diagnostic.Details.Should().Contain(d => d.Key == "retry_count" && d.Value == "5");
        diagnostic.Suggestions.Should().HaveCount(3);
    }

    [Fact]
    public void BuildUnknownStateDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = TimeoutRecoveryToolHandlers.BuildUnknownStateDiagnostic("UnknownState");
        diagnostic.Reason.Should().Be("未知状态");
        diagnostic.Details.Should().Contain(d => d.Key == "state" && d.Value == "UnknownState");
    }
}
