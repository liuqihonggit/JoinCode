namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolTemplateService 单元测试 — 验证模板加载、Schema构建、占位符替换
/// </summary>
public sealed class ToolTemplateServiceTest : IAsyncLifetime {
    private InMemoryFileSystem _fs = null!;
    private ToolTemplateService _service = null!;

    public Task InitializeAsync() {
        _fs = new InMemoryFileSystem();
        _service = new ToolTemplateService(_fs);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() {
        _service.DisposeSafe();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadTemplatesAsync_NoTemplatesDir_ReturnsEmptyList() {
        var templates = await _service.LoadTemplatesAsync();
        templates.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveTemplateAsync_And_LoadTemplatesAsync_RoundTrip() {
        var template = new ToolTemplate {
            Id = "test_tool",
            ToolName = "test_tool",
            Description = "A test tool",
            Kind = ToolKind.Mcp,
            GroupName = "test_group",
            Parameters =
            [
                new ToolTemplateParameter
                {
                    Name = "input",
                    Description = "Input parameter",
                    Type = "string",
                    Required = true
                }
            ],
            Execution = new ToolTemplateExecution {
                Type = "shell",
                Command = "echo",
                Args = ["{{input}}"],
                TimeoutSeconds = 10
            }
        };

        await _service.SaveTemplateAsync(template);
        var loaded = await _service.LoadTemplatesAsync();

        loaded.Should().HaveCount(1);
        loaded[0].ToolName.Should().Be("test_tool");
        loaded[0].Description.Should().Be("A test tool");
        loaded[0].Parameters.Should().HaveCount(1);
        loaded[0].Execution.Type.Should().Be("shell");
    }

    [Fact]
    public async Task ListTemplatesAsync_ReturnsCachedTemplates() {
        var template = new ToolTemplate {
            Id = "cached_tool",
            ToolName = "cached_tool",
            Description = "Cached tool",
            Parameters = [],
            Execution = new ToolTemplateExecution { Type = "shell", Command = "ls" }
        };

        await _service.SaveTemplateAsync(template);
        await _service.LoadTemplatesAsync();

        var listed = await _service.ListTemplatesAsync();
        listed.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadTemplatesAsync_InvalidJson_SkipsFile() {
        var templatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "tool-templates");
        _fs.CreateDirectory(templatesDir!);
        await _fs.WriteAllText(Path.Combine(templatesDir, "bad.json"), "not valid json {{{");

        var templates = await _service.LoadTemplatesAsync();
        templates.Should().BeEmpty();
    }

    // === ReplacePlaceholders (internal static) ===

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ReplacePlaceholders_StringValue_ReplacesPlaceholder() {
        var args = new Dictionary<string, JsonElement> { ["input"] = J("\"hello\"") };
        ToolTemplateService.ReplacePlaceholders("echo {{input}}", args).Should().Be("echo hello");
    }

    [Fact]
    public void ReplacePlaceholders_MultiplePlaceholders_AllReplaced() {
        var args = new Dictionary<string, JsonElement> {
            ["a"] = J("\"1\""), ["b"] = J("\"2\"")
        };
        ToolTemplateService.ReplacePlaceholders("{{a}} and {{b}}", args).Should().Be("1 and 2");
    }

    [Fact]
    public void ReplacePlaceholders_NumberValue_UsesRawText() {
        var args = new Dictionary<string, JsonElement> { ["count"] = J("42") };
        ToolTemplateService.ReplacePlaceholders("count={{count}}", args).Should().Be("count=42");
    }

    [Fact]
    public void ReplacePlaceholders_BooleanTrue_UsesLowercaseTrue() {
        var args = new Dictionary<string, JsonElement> { ["flag"] = J("true") };
        ToolTemplateService.ReplacePlaceholders("--flag={{flag}}", args).Should().Be("--flag=true");
    }

    [Fact]
    public void ReplacePlaceholders_BooleanFalse_UsesLowercaseFalse() {
        var args = new Dictionary<string, JsonElement> { ["flag"] = J("false") };
        ToolTemplateService.ReplacePlaceholders("--flag={{flag}}", args).Should().Be("--flag=false");
    }

    [Fact]
    public void ReplacePlaceholders_UnmatchedPlaceholder_Remains() {
        var args = new Dictionary<string, JsonElement> { ["a"] = J("\"1\"") };
        ToolTemplateService.ReplacePlaceholders("{{a}} {{missing}}", args).Should().Be("1 {{missing}}");
    }

    [Fact]
    public void ReplacePlaceholders_CaseInsensitiveKey_Matches() {
        var args = new Dictionary<string, JsonElement> { ["input"] = J("\"v\"") };
        ToolTemplateService.ReplacePlaceholders("{{INPUT}}", args).Should().Be("v");
    }

    [Fact]
    public void ReplacePlaceholders_NoPlaceholders_ReturnsOriginal() {
        var args = new Dictionary<string, JsonElement> { ["x"] = J("\"1\"") };
        ToolTemplateService.ReplacePlaceholders("plain text", args).Should().Be("plain text");
    }

    [Fact]
    public void ReplacePlaceholders_EmptyArgs_ReturnsOriginal() {
        ToolTemplateService.ReplacePlaceholders("{{a}} text", []).Should().Be("{{a}} text");
    }

    [Fact]
    public void ReplacePlaceholders_NullJsonValue_UsesRawTextNull() {
        var args = new Dictionary<string, JsonElement> { ["x"] = J("null") };
        ToolTemplateService.ReplacePlaceholders("[{{x}}]", args).Should().Be("[null]");
    }

    [Fact]
    public void ReplacePlaceholders_ObjectValue_UsesRawText() {
        var args = new Dictionary<string, JsonElement> { ["o"] = J("{\"k\":1}") };
        ToolTemplateService.ReplacePlaceholders("{{o}}", args).Should().Be("{\"k\":1}");
    }

    // === BuildSchema (internal static) ===

    private static ToolTemplate T(params ToolTemplateParameter[] parameters) => new() {
        Id = "t", ToolName = "t", Description = "d",
        Parameters = parameters,
        Execution = new ToolTemplateExecution { Type = "shell" }
    };

    private static ToolTemplateParameter P(string name, bool required = true, string type = "string",
        string? defaultValue = null, string[]? enumValues = null) => new() {
        Name = name, Description = name, Type = type, Required = required,
        DefaultValue = defaultValue, EnumValues = enumValues
    };

    [Fact]
    public void BuildSchema_RequiredParam_CollectedIntoRequired() {
        var schema = ToolTemplateService.BuildSchema(T(P("cmd", required: true), P("opt", required: false)));
        schema.Required.Should().ContainSingle().Which.Should().Be("cmd");
        schema.Properties.Should().ContainKey("cmd");
        schema.Properties.Should().ContainKey("opt");
    }

    [Fact]
    public void BuildSchema_NoRequiredParams_RequiredEmpty() {
        var schema = ToolTemplateService.BuildSchema(T(P("opt", required: false)));
        schema.Required.Should().BeEmpty();
    }

    [Fact]
    public void BuildSchema_NoParams_PropertiesAndRequiredEmpty() {
        var schema = ToolTemplateService.BuildSchema(T());
        schema.Properties.Should().BeEmpty();
        schema.Required.Should().BeEmpty();
    }

    [Fact]
    public void BuildSchema_EnumValues_CopiedToProperty() {
        var schema = ToolTemplateService.BuildSchema(T(P("mode", enumValues: ["fast", "slow"])));
        schema.Properties["mode"].Enum.Should().BeEquivalentTo(["fast", "slow"]);
    }

    [Fact]
    public void BuildSchema_DefaultValue_CopiedToProperty() {
        var schema = ToolTemplateService.BuildSchema(T(P("timeout", type: "integer", defaultValue: "30")));
        schema.Properties["timeout"].Default.Should().Be("30");
    }

    [Fact]
    public void BuildSchema_PropertyTypeAndDescription_Copied() {
        var schema = ToolTemplateService.BuildSchema(T(P("path", type: "string")));
        schema.Properties["path"].Type.Should().Be("string");
        schema.Properties["path"].Description.Should().Be("path");
    }

    [Fact]
    public void BuildSchema_AllParamsRequired_RequiredHasAll() {
        var schema = ToolTemplateService.BuildSchema(T(P("a"), P("b"), P("c")));
        schema.Required.Should().BeEquivalentTo(["a", "b", "c"]);
    }
}