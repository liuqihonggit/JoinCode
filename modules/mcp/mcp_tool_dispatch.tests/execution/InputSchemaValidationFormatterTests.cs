namespace McpToolRegistry.Tests;

public class InputSchemaValidationFormatterTests {
    [Fact]
    public void FormatErrors_EmptyErrors_ReturnsEmpty() {
        var result = InputSchemaValidationFormatter.FormatErrors("Tool", []);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatErrors_SingleError_UsesSingularIssue() {
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
    public void FormatErrors_MultipleErrors_UsesPluralIssues() {
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
    public void FormatErrors_MissingRequired_FormatsAsMissingParam() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.command", Message = "Required property 'command' is missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Bash", errors);
        result.Should().Contain("command");
        result.Should().Contain("missing");
    }

    [Fact]
    public void FormatErrors_MissingRequired_ExtractsParamFromPath() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.filePath", Message = "is missing and required" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("FileRead", errors);
        result.Should().Contain("filePath");
    }

    [Fact]
    public void FormatErrors_UnexpectedKey_FormatsAsUnexpectedParam() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unexpected property 'foo' found" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("foo");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_UnrecognizedKey_FormatsAsUnexpectedParam() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unrecognized property 'bar'" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("bar");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_AdditionalProperty_FormatsAsUnexpectedParam() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Additional property 'extra' not allowed" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("extra");
        result.Should().Contain("unexpected");
    }

    [Fact]
    public void FormatErrors_TypeMismatch_FormatsAsTypeMismatch() {
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
    public void FormatErrors_PathWithDollarPrefix_CleansPath() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.nested.field", Message = "some generic error" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("nested");
    }

    [Fact]
    public void FormatErrors_PathIsDollarOnly_UsesMessageDirectly() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "generic root error" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("generic root error");
    }

    [Fact]
    public void FormatErrors_EmptyPath_UsesMessageDirectly() {
        var errors = new List<ValidationError>
        {
            new() { Path = "", Message = "plain error message" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("plain error message");
    }

    [Fact]
    public void FormatErrors_MissingWithNoPath_ExtractsFromMessage() {
        var errors = new List<ValidationError>
        {
            new() { Path = "", Message = "Required property 'timeout' is missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("timeout");
    }

    [Fact]
    public void FormatErrors_MissingWithNestedPath_ExtractsTopLevelParam() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$.config.depth", Message = "required property missing" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("config");
    }

    [Fact]
    public void FormatErrors_TypeMismatchWithExpectedButGot_ExtractsTypes() {
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
    public void FormatErrors_UnexpectedKeyNoQuotes_FallsBackToGenericFormat() {
        var errors = new List<ValidationError>
        {
            new() { Path = "$", Message = "Unexpected property found without quotes" }
        };

        var result = InputSchemaValidationFormatter.FormatErrors("Tool", errors);
        result.Should().Contain("Tool");
        result.Should().Contain("Unexpected");
    }

    // === TryParseMissingRequired (internal static) ===

    [Fact]
    public void TryParseMissingRequired_ContainsMissing_ReturnsTrueAndExtractsParam() {
        var ok = InputSchemaValidationFormatter.TryParseMissingRequired(
            "Required property 'command' is missing", "$.command", out var paramName);
        ok.Should().BeTrue();
        paramName.Should().Be("command");
    }

    [Fact]
    public void TryParseMissingRequired_ContainsRequired_ReturnsTrue() {
        var ok = InputSchemaValidationFormatter.TryParseMissingRequired(
            "is required", "$.timeout", out var paramName);
        ok.Should().BeTrue();
        paramName.Should().Be("timeout");
    }

    [Fact]
    public void TryParseMissingRequired_NoKeyword_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseMissingRequired(
            "some other error", "$.x", out var paramName);
        ok.Should().BeFalse();
        paramName.Should().BeEmpty();
    }

    [Fact]
    public void TryParseMissingRequired_CaseInsensitive_ReturnsTrue() {
        var ok = InputSchemaValidationFormatter.TryParseMissingRequired(
            "MISSING property", "$.x", out _);
        ok.Should().BeTrue();
    }

    // === TryParseUnexpectedKey (internal static) ===

    [Fact]
    public void TryParseUnexpectedKey_UnexpectedWithQuotes_ReturnsTrueAndExtracts() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "Unexpected property 'foo' found", out var paramName);
        ok.Should().BeTrue();
        paramName.Should().Be("foo");
    }

    [Fact]
    public void TryParseUnexpectedKey_UnrecognizedWithQuotes_ReturnsTrue() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "Unrecognized property 'bar'", out var paramName);
        ok.Should().BeTrue();
        paramName.Should().Be("bar");
    }

    [Fact]
    public void TryParseUnexpectedKey_AdditionalWithQuotes_ReturnsTrue() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "Additional property 'extra' not allowed", out var paramName);
        ok.Should().BeTrue();
        paramName.Should().Be("extra");
    }

    [Fact]
    public void TryParseUnexpectedKey_NoKeyword_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "some error", out var paramName);
        ok.Should().BeFalse();
        paramName.Should().BeEmpty();
    }

    [Fact]
    public void TryParseUnexpectedKey_KeywordButNoQuotes_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "Unexpected property found without quotes", out var paramName);
        ok.Should().BeFalse();
        paramName.Should().BeEmpty();
    }

    [Fact]
    public void TryParseUnexpectedKey_OnlyOpeningQuote_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseUnexpectedKey(
            "Unexpected 'foo", out var paramName);
        ok.Should().BeFalse();
        paramName.Should().BeEmpty();
    }

    // === TryParseTypeMismatch (internal static) ===

    [Fact]
    public void TryParseTypeMismatch_ExpectedButGot_ReturnsTrueAndExtractsTypes() {
        var ok = InputSchemaValidationFormatter.TryParseTypeMismatch(
            "Expected type integer but got string", "$.count",
            out var paramName, out var expected, out var received);
        ok.Should().BeTrue();
        paramName.Should().Be("count");
        expected.Should().Contain("integer");
        received.Should().Contain("string");
    }

    [Fact]
    public void TryParseTypeMismatch_TypeMismatchExpectedButGot_ReturnsTrue() {
        var ok = InputSchemaValidationFormatter.TryParseTypeMismatch(
            "type mismatch - expected number but got string", "$.size",
            out var paramName, out var expected, out var received);
        ok.Should().BeTrue();
        paramName.Should().Be("size");
        expected.Should().Contain("number");
        received.Should().Contain("string");
    }

    [Fact]
    public void TryParseTypeMismatch_NoTypeKeyword_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseTypeMismatch(
            "expected value but got nothing", "$.x",
            out var paramName, out var expected, out var received);
        ok.Should().BeFalse();
        paramName.Should().BeEmpty();
        expected.Should().BeEmpty();
        received.Should().BeEmpty();
    }

    [Fact]
    public void TryParseTypeMismatch_TypeButNoExpected_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseTypeMismatch(
            "type error occurred", "$.x",
            out var paramName, out var expected, out var received);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParseTypeMismatch_TypeExpectedButNoBut_ReturnsFalse() {
        var ok = InputSchemaValidationFormatter.TryParseTypeMismatch(
            "type expected integer", "$.x",
            out var paramName, out var expected, out var received);
        ok.Should().BeFalse();
    }

    // === ExtractParamName (internal static) ===

    [Fact]
    public void ExtractParamName_PathWithDollarDot_ReturnsCleanPath() {
        InputSchemaValidationFormatter.ExtractParamName("err", "$.command").Should().Be("command");
    }

    [Fact]
    public void ExtractParamName_NestedPath_ReturnsTopLevelParam() {
        InputSchemaValidationFormatter.ExtractParamName("err", "$.config.depth").Should().Be("config");
    }

    [Fact]
    public void ExtractParamName_PathWithoutDollar_ReturnsPath() {
        InputSchemaValidationFormatter.ExtractParamName("err", "filePath").Should().Be("filePath");
    }

    [Fact]
    public void ExtractParamName_DollarOnlyPath_ExtractsFromMessage() {
        InputSchemaValidationFormatter.ExtractParamName("Required 'timeout' is missing", "$").Should().Be("timeout");
    }

    [Fact]
    public void ExtractParamName_EmptyPath_ExtractsFromMessage() {
        InputSchemaValidationFormatter.ExtractParamName("property 'foo' invalid", "").Should().Be("foo");
    }

    [Fact]
    public void ExtractParamName_NoPathNoQuotes_ReturnsUnknown() {
        InputSchemaValidationFormatter.ExtractParamName("generic error", "").Should().Be("unknown");
    }

    [Fact]
    public void ExtractParamName_PathWithOnlyOpeningQuoteInMessage_ReturnsUnknown() {
        InputSchemaValidationFormatter.ExtractParamName("error 'foo", "").Should().Be("unknown");
    }
}