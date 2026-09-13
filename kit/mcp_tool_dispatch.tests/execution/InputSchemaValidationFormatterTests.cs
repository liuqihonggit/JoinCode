namespace McpToolRegistry.Tests;

public class InputSchemaValidationFormatterTests
{
    [Fact]
    public void FormatErrors_EmptyErrors_ReturnsEmpty()
    {
        var result = InputSchemaValidationFormatter.FormatErrors("Tool", []);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatErrors_SingleError_UsesSingularIssue()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.param", Message = "some error" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("MyTool", errors);
        result.Should().Contain("issue");
        result.Should().NotContain("issues");
        result.Should().StartWith("MyTool");
    }

    [Fact]
    public void FormatErrors_MultipleErrors_UsesPluralIssues()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.a", Message = "error1" },
            new() { Path = "$.b", Message = "error2" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("MyTool", errors);
        result.Should().Contain("issues");
        result.Should().NotMatch("*1 issue*");
    }

    [Fact]
    public void FormatErrors_MissingRequired_FormatsAsMissingParam()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.command", Message = "Required property 'command' is missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Bash", errors);
        result.Should().Contain("command");
        result.Should().Contain("missing");
    }

    [Fact]
    public void FormatErrors_MissingRequired_ExtractsParamFromPath()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.filePath", Message = "is missing and required" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("FileRead", errors);
        result.Should().Contain("filePath");
    }

    [Fact]
    public void FormatErrors_UnexpectedKey_FormatsAsUnexpectedParam()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unexpected property 'foo' found" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("foo");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_UnrecognizedKey_FormatsAsUnexpectedParam()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unrecognized property 'bar'" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("bar");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_AdditionalProperty_FormatsAsUnexpectedParam()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Additional property 'extra' not allowed" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("extra");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_TypeMismatch_FormatsAsTypeMismatch()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.count", Message = "Expected type integer but got string" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("count");
        result.Should().Contain("integer");
        result.Should().Contain("string");
    }

    [Fact]
    public void FormatErrors_PathWithDollarPrefix_CleansPath()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.nested.field", Message = "some generic error" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("nested");
    }

    [Fact]
    public void FormatErrors_PathIsDollarOnly_UsesMessageDirectly()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "generic root error" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("generic root error");
    }

    [Fact]
    public void FormatErrors_EmptyPath_UsesMessageDirectly()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "", Message = "plain error message" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("plain error message");
    }

    [Fact]
    public void FormatErrors_MissingWithNoPath_ExtractsFromMessage()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "", Message = "Required property 'timeout' is missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("timeout");
    }

    [Fact]
    public void FormatErrors_MissingWithNestedPath_ExtractsTopLevelParam()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.config.depth", Message = "required property missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("config");
    }

    [Fact]
    public void FormatErrors_TypeMismatchWithExpectedButGot_ExtractsTypes()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.size", Message = "type mismatch - expected number but got string" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("size");
        result.Should().Contain("number");
        result.Should().Contain("string");
    }

    [Fact]
    public void FormatErrors_UnexpectedKeyNoQuotes_FallsBackToGenericFormat()
    {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unexpected property found without quotes" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("Tool");
        result.Should().Contain("Unexpected");
    }
}
