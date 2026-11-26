namespace Host.Tests.Cli;

/// <summary>
/// 斜杠命令结构化参数校验 — 授权层（slash.yaml）只管参数名白名单，
/// required/enum/type 必须按 [ChatCommandArg] 生成的 schema 拒绝，
/// 否则 agent 传错类型/漏必填会被命令内部静默吞掉。
/// </summary>
public sealed class SlashCommandArgValidatorTests {
    private static ToolSchemaProperty Prop(string type = "string", string[]? enumValues = null) => new() {
        Type = type,
        Enum = enumValues?.ToList(),
    };

    private static ToolSchema BuildSchema((string Name, ToolSchemaProperty Property)[] properties, string[]? required = null) {
        var schema = new ToolSchema { Required = required?.ToList() ?? [] };
        foreach (var (name, property) in properties) schema.Properties[name] = property;
        return schema;
    }

    [Fact]
    public void MissingRequiredArgument_IsRejectedWithGuidance() {
        var schema = BuildSchema([("description", Prop())], required: ["description"]);

        var error = SlashCommandArgValidator.Validate("generate", "{}", schema);

        var message = error ?? throw new InvalidOperationException("期望校验拒绝，实际通过");
        message.Should().Contain("--description");
        message.Should().Contain("Slash.Schema");
        message.Should().Contain("正确写法");
    }

    [Fact]
    public void EnumViolation_IsRejectedAndListsAllowedValues() {
        var schema = BuildSchema([("action", Prop(enumValues: ["list", "info"]))]);

        var error = SlashCommandArgValidator.Validate("agents", """{"action":"delete"}""", schema);

        var message = error ?? throw new InvalidOperationException("期望校验拒绝，实际通过");
        message.Should().Contain("list, info");
        message.Should().Contain("Slash.Schema");
    }

    [Theory]
    [InlineData("""{"action":"list"}""")]
    [InlineData("""{"action":"info"}""")]
    public void EnumMember_IsAccepted(string argsJson) {
        var schema = BuildSchema([("action", Prop(enumValues: ["list", "info"]))]);

        SlashCommandArgValidator.Validate("agents", argsJson, schema).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"steps":3}""")]
    [InlineData("""{"steps":"3"}""")]
    public void Number_AcceptsJsonNumberAndNumericText(string argsJson) {
        var schema = BuildSchema([("steps", Prop(type: "number"))]);

        SlashCommandArgValidator.Validate("ultraplan", argsJson, schema).Should().BeNull();
    }

    [Fact]
    public void Number_RejectsNonNumericText() {
        var schema = BuildSchema([("steps", Prop(type: "number"))]);

        var error = SlashCommandArgValidator.Validate("ultraplan", """{"steps":"many"}""", schema);

        var message = error ?? throw new InvalidOperationException("期望校验拒绝，实际通过");
        message.Should().Contain("number");
    }

    [Theory]
    [InlineData("""{"execute":true}""")]
    [InlineData("""{"execute":"true"}""")]
    [InlineData("""{"execute":"1"}""")]
    public void Boolean_AcceptsJsonBoolAndCommonTextForms(string argsJson) {
        var schema = BuildSchema([("execute", Prop(type: "boolean"))]);

        SlashCommandArgValidator.Validate("ultraplan", argsJson, schema).Should().BeNull();
    }

    [Fact]
    public void Boolean_RejectsArbitraryText() {
        var schema = BuildSchema([("execute", Prop(type: "boolean"))]);

        var error = SlashCommandArgValidator.Validate("ultraplan", """{"execute":"maybe"}""", schema);

        var message = error ?? throw new InvalidOperationException("期望校验拒绝，实际通过");
        message.Should().Contain("boolean");
    }

    [Fact]
    public void Array_AcceptsJsonArrayText() {
        var schema = BuildSchema([("items", Prop(type: "array"))]);

        SlashCommandArgValidator.Validate("demo", """{"items":"[1,2]"}""", schema).Should().BeNull();
    }

    [Fact]
    public void Object_RejectsScalar() {
        var schema = BuildSchema([("options", Prop(type: "object"))]);

        var error = SlashCommandArgValidator.Validate("demo", """{"options":"x"}""", schema);

        var message = error ?? throw new InvalidOperationException("期望校验拒绝，实际通过");
        message.Should().Contain("object");
    }

    [Fact]
    public void UnknownDeclaredType_IsNotGuessed() {
        var schema = BuildSchema([("payload", Prop(type: "custom"))]);

        SlashCommandArgValidator.Validate("demo", """{"payload":"x"}""", schema).Should().BeNull();
    }

    [Fact]
    public void NullSchema_IsSkippedForLegacyCommands() {
        SlashCommandArgValidator.Validate("legacy", """{"anything":"x"}""", schema: null).Should().BeNull();
    }

    [Fact]
    public void UnknownParameterNames_AreLeftToPolicyLayer() {
        var schema = BuildSchema([("action", Prop())]);

        SlashCommandArgValidator.Validate("agents", """{"level":"2"}""", schema).Should().BeNull();
    }

    [Fact]
    public void GeneratedCatalog_DeclaresRequiredAndTypesFromAttributes() {
        var catalog = new GeneratedSlashCommandSchemaCatalog();

        var generate = catalog.GetSchema("generate");
        var requiredSchema = generate ?? throw new InvalidOperationException("generate 应声明结构化参数");
        requiredSchema.Required.Should().Contain("description");

        var ultraplan = catalog.GetSchema("ultraplan");
        var planSchema = ultraplan ?? throw new InvalidOperationException("ultraplan 应声明结构化参数");
        planSchema.Properties["steps"].Type.Should().Be("number");
        planSchema.Properties["execute"].Type.Should().Be("boolean");
    }
}
