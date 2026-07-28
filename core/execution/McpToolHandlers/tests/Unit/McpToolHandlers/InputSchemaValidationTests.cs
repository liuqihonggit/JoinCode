namespace McpToolRegistry.Tests;

public class InputSchemaValidationTests
{
    private readonly SimpleJsonSchemaValidator _validator = new();

    [Fact]
    public void ValidateInput_MissingRequiredParameter_ReturnsError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["command"] = new() { Type = "string", Description = "Command to execute" }
            },
            Required = ["command"]
        };

        var arguments = new Dictionary<string, JsonElement>();
        var result = ValidateToolInput(schema, arguments);

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("command");
    }

    [Fact]
    public void ValidateInput_AllRequiredPresent_NoError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["command"] = new() { Type = "string", Description = "Command to execute" }
            },
            Required = ["command"]
        };

        var arguments = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement("echo hello")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateInput_WrongType_ReturnsError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["count"] = new() { Type = "integer", Description = "Count" }
            }
        };

        var arguments = new Dictionary<string, JsonElement>
        {
            ["count"] = JsonSerializer.SerializeToElement("not_a_number")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("count");
    }

    [Fact]
    public void ValidateInput_InvalidEnumValue_ReturnsError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["mode"] = new() { Type = "string", Enum = ["read", "write", "append"] }
            }
        };

        var arguments = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonSerializer.SerializeToElement("delete")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
    }

    [Fact]
    public void ValidateInput_ValidEnumValue_NoError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["mode"] = new() { Type = "string", Enum = ["read", "write", "append"] }
            }
        };

        var arguments = new Dictionary<string, JsonElement>
        {
            ["mode"] = JsonSerializer.SerializeToElement("read")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateInput_EmptySchema_NoError()
    {
        var schema = new ToolSchema();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["anything"] = JsonSerializer.SerializeToElement("value")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateInput_MultipleMissingRequired_ListsAll()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["command"] = new() { Type = "string", Description = "Command" },
                ["timeout"] = new() { Type = "integer", Description = "Timeout" }
            },
            Required = ["command", "timeout"]
        };

        var arguments = new Dictionary<string, JsonElement>();
        var result = ValidateToolInput(schema, arguments);

        result.Should().NotBeNull();
        var text = result!.GetTextContent();
        text.Should().Contain("command");
        text.Should().Contain("timeout");
    }

    [Fact]
    public void ValidateInput_OptionalParameterMissing_NoError()
    {
        var schema = new ToolSchema
        {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["command"] = new() { Type = "string", Description = "Command" },
                ["timeout"] = new() { Type = "integer", Description = "Timeout", Default = "30000" }
            },
            Required = ["command"]
        };

        var arguments = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement("echo hello")
        };

        var result = ValidateToolInput(schema, arguments);
        result.Should().BeNull();
    }

    [Fact]
    public void FormatValidationError_MissingRequired_MatchesTsFormat()
    {
        var toolName = "Bash";
        var errors = new List<ValidationError>
        {
            new() { Path = "$.command", Message = "Required property 'command' is missing" }
        };

        var formatted = InputSchemaValidationFormatter.FormatErrors(toolName, errors);
        formatted.Should().Contain("Bash");
        formatted.Should().Contain("command");
        formatted.Should().Contain("missing");
    }

    [Fact]
    public void FormatValidationError_WrongType_MatchesTsFormat()
    {
        var toolName = "FileRead";
        var errors = new List<ValidationError>
        {
            new() { Path = "$.offset", Message = "Expected type integer but got string" }
        };

        var formatted = InputSchemaValidationFormatter.FormatErrors(toolName, errors);
        formatted.Should().Contain("FileRead");
        formatted.Should().Contain("offset");
    }

    private ToolResult? ValidateToolInput(ToolSchema schema, Dictionary<string, JsonElement> arguments)
    {
        var schemaJson = JsonSerializer.Serialize(schema);
        var argsJson = JsonSerializer.Serialize(arguments);

        var validation = _validator.Validate(argsJson, schemaJson);
        if (validation.IsValid) return null;

        var toolName = "TestTool";
        var formatted = InputSchemaValidationFormatter.FormatErrors(toolName, validation.Errors);

        return new ToolResult
        {
            Content = [new ToolContent { Type = ToolContentType.Text, Text = formatted }],
            IsError = true
        };
    }
}
