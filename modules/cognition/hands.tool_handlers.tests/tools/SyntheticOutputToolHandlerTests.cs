namespace Core.Tests.Tools;

/// <summary>
/// SyntheticOutputToolHandler 单元测试 — TASK071-C6 结构化输出合成工具体系
/// 覆盖验证通过/验证失败/无效JSON/JsonException四条路径
/// </summary>
public sealed class SyntheticOutputToolHandlerTests {
    private readonly SimpleJsonSchemaValidator _validator = new();

    #region Constructor

    [SuppressMessage("AotSafety", "JCC11003", Justification = "测试 null 参数: 构造函数 null 校验需要传入 null")]
    [Fact]
    public void Constructor_NullValidator_ShouldThrowArgumentNullException() {
        IJsonSchemaValidator? nullValidator = null;
        var act = () => new SyntheticOutputToolHandler(nullValidator!, """{"type": "object"}""");
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validator");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "测试 null 参数: 构造函数 null 校验需要传入 null")]
    [Fact]
    public void Constructor_NullSchemaJson_ShouldThrowArgumentNullException() {
        string? nullSchema = null;
        var act = () => new SyntheticOutputToolHandler(_validator, nullSchema!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("schemaJson");
    }

    #endregion

    #region ProvideStructuredOutputAsync - Validation Pass

    [Fact]
    public async Task ProvideStructuredOutputAsync_ValidInput_ShouldReturnSuccessWithStructuredOutput() {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                }
            }
            """;
        var input = """{"name": "Alice", "age": 30}""";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeFalse();
        result.StructuredOutput.Should().NotBeNull();
        var structuredOutput = result.StructuredOutput;
        if (structuredOutput is null) return;
        structuredOutput.Value.ValueKind.Should().Be(JsonValueKind.Object);
        structuredOutput.Value.GetProperty("name").GetString().Should().Be("Alice");
        structuredOutput.Value.GetProperty("age").GetInt32().Should().Be(30);
        result.GetFirstText().Should().Be("Structured output provided successfully");
    }

    [Fact]
    public async Task ProvideStructuredOutputAsync_ValidSimpleType_ShouldReturnSuccessWithStructuredOutput() {
        var schema = """{"type": "string"}""";
        var input = "\"hello world\"";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeFalse();
        result.StructuredOutput.Should().NotBeNull();
        var structuredOutput = result.StructuredOutput;
        if (structuredOutput is null) return;
        structuredOutput.Value.ValueKind.Should().Be(JsonValueKind.String);
        structuredOutput.Value.GetString().Should().Be("hello world");
    }

    [Fact]
    public async Task ProvideStructuredOutputAsync_ValidArray_ShouldReturnSuccessWithStructuredOutput() {
        var schema = """{"type": "array", "items": {"type": "integer"}}""";
        var input = "[1, 2, 3]";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeFalse();
        result.StructuredOutput.Should().NotBeNull();
        var structuredOutput = result.StructuredOutput;
        if (structuredOutput is null) return;
        structuredOutput.Value.ValueKind.Should().Be(JsonValueKind.Array);
        structuredOutput.Value.GetArrayLength().Should().Be(3);
    }

    #endregion

    #region ProvideStructuredOutputAsync - Validation Fail

    [Fact]
    public async Task ProvideStructuredOutputAsync_MissingRequiredProperty_ShouldReturnErrorWithDiagnostic() {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                }
            }
            """;
        var input = """{"name": "Alice"}""";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeTrue();
        result.StructuredOutput.Should().BeNull();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Reason.Should().Be("Schema validation failed");
        diagnostic.FormattedMessage.Should().Contain("Output does not match required schema");
        diagnostic.FormattedMessage.Should().Contain("age");
        diagnostic.Suggestions.Should().ContainSingle(s => s.Contains("Fix the output to match the JSON Schema"));
    }

    [Fact]
    public async Task ProvideStructuredOutputAsync_WrongType_ShouldReturnErrorWithDiagnostic() {
        var schema = """{"type": "string"}""";
        var input = "42";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeTrue();
        result.StructuredOutput.Should().BeNull();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Reason.Should().Be("Schema validation failed");
        diagnostic.FormattedMessage.Should().Contain("Output does not match required schema");
    }

    [Fact]
    public async Task ProvideStructuredOutputAsync_MultipleValidationErrors_ShouldIncludeAllInDiagnostic() {
        var schema = """
            {
                "type": "object",
                "required": ["name", "age", "email"],
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"},
                    "email": {"type": "string"}
                }
            }
            """;
        var input = """{"name": "Alice"}""";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Details.Should().HaveCount(2);
        diagnostic.Details.Should().Contain(d => d.Key == "$.age");
        diagnostic.Details.Should().Contain(d => d.Key == "$.email");
    }

    #endregion

    #region ProvideStructuredOutputAsync - Invalid JSON Input

    [Fact]
    public async Task ProvideStructuredOutputAsync_InvalidJson_ShouldReturnErrorWithSchemaValidationFailed() {
        // SimpleJsonSchemaValidator.Validate 内部捕获 JsonException 返回 IsValid=false，
        // 所以 ProvideStructuredOutputAsync 走 "Schema validation failed" 路径
        var schema = """{"type": "object"}""";
        var input = "not valid json{";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeTrue();
        result.StructuredOutput.Should().BeNull();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Reason.Should().Be("Schema validation failed");
        diagnostic.FormattedMessage.Should().Contain("Output does not match required schema");
    }

    [Fact]
    public async Task ProvideStructuredOutputAsync_EmptyString_ShouldReturnErrorWithSchemaValidationFailed() {
        var schema = """{"type": "object"}""";
        var input = "";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        var result = await handler.ProvideStructuredOutputAsync(input);

        result.IsError.Should().BeTrue();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Reason.Should().Be("Schema validation failed");
    }

    #endregion

    #region ProvideStructuredOutputAsync - JsonException (via mock validator)

    [Fact]
    public async Task ProvideStructuredOutputAsync_ValidatorThrowsJsonException_ShouldReturnErrorWithInvalidJsonInput() {
        // 当验证器实现抛出 JsonException（而非内部捕获）时，handler 的 catch 块应捕获并返回诊断
        var mockValidator = new Mock<IJsonSchemaValidator>();
        mockValidator
            .Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new JsonException("Unexpected end of data"));
        var handler = new SyntheticOutputToolHandler(mockValidator.Object, """{"type": "object"}""");

        var result = await handler.ProvideStructuredOutputAsync("""{"key": "value"}""");

        result.IsError.Should().BeTrue();
        result.StructuredOutput.Should().BeNull();
        result.Diagnostic.Should().NotBeNull();
        var diagnostic = result.Diagnostic;
        if (diagnostic is null) return;
        diagnostic.Reason.Should().Be("Invalid JSON input");
        diagnostic.FormattedMessage.Should().Contain("Failed to parse input as JSON");
        diagnostic.FormattedMessage.Should().Contain("Unexpected end of data");
        diagnostic.Suggestions.Should().ContainSingle(s => s.Contains("Ensure the input is valid JSON"));
    }

    #endregion

    #region ProvideStructuredOutputAsync - CancellationToken

    [Fact]
    public async Task ProvideStructuredOutputAsync_WithCancellationToken_ShouldRespectToken() {
        var schema = """{"type": "object"}""";
        var input = """{"key": "value"}""";
        var handler = new SyntheticOutputToolHandler(_validator, schema);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // ProvideStructuredOutputAsync 是同步完成的（Task.FromResult），不会检查 CancellationToken
        // 但传入已取消的 token 不应导致未处理异常
        var result = await handler.ProvideStructuredOutputAsync(input, cts.Token);
        result.IsError.Should().BeFalse();
    }

    #endregion
}
