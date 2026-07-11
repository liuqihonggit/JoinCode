namespace Integration.Tests.PrefixCache.Unit;

public sealed class ToolCallRepairServiceTests
{
    [Fact]
    public void RepairJson_TrailingCommaInObject_RemovesComma()
    {
        var result = ToolCallRepairService.RepairJson("""{"filePath": "/src/Program.cs",}""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("filePath").GetString().Should().Be("/src/Program.cs");
        result.RepairHint.Should().Contain("trailing comma");
    }

    [Fact]
    public void RepairJson_TrailingCommaInArray_RemovesComma()
    {
        var result = ToolCallRepairService.RepairJson("""{"paths": ["/a", "/b",]}""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("paths").GetArrayLength().Should().Be(2);
        result.RepairHint.Should().Contain("trailing comma");
    }

    [Fact]
    public void RepairJson_UnquotedKeys_AddsQuotes()
    {
        var result = ToolCallRepairService.RepairJson("""{filePath: "/src/Program.cs"}""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("filePath").GetString().Should().Be("/src/Program.cs");
        result.RepairHint.Should().Contain("unquoted key");
    }

    [Fact]
    public void RepairJson_SingleQuotedKeys_ConvertsToDoubleQuotes()
    {
        var result = ToolCallRepairService.RepairJson("""{'filePath': '/src/Program.cs'}""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("filePath").GetString().Should().Be("/src/Program.cs");
        result.RepairHint.Should().Contain("single-quoted");
    }

    [Fact]
    public void RepairJson_ValidJson_ReturnsAsIs()
    {
        var json = """{"filePath": "/src/Program.cs"}""";
        var result = ToolCallRepairService.RepairJson(json);

        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be(json);
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairJson_EmptyString_ReturnsEmptyObject()
    {
        var result = ToolCallRepairService.RepairJson("");

        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("{}");
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairJson_NullInput_ReturnsEmptyObject()
    {
        var result = ToolCallRepairService.RepairJson(null);

        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("{}");
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairJson_UnrepairableJson_ReturnsFailure()
    {
        var result = ToolCallRepairService.RepairJson("{{{{not json at all");

        result.Success.Should().BeFalse();
        result.RepairHint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RepairArguments_WrongParameterName_RenamesToCorrectName()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["filePath"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonElementHelper.FromString("/src/Program.cs")
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments.Should().ContainKey("filePath");
        result.RepairedArguments.Should().NotContainKey("file_path");
        result.RepairHint.Should().Contain("'file_path'");
        result.RepairHint.Should().Contain("'filePath'");
    }

    [Fact]
    public void RepairArguments_ArrayWhereStringExpected_TakesFirstElement()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["filePath"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["filePath"] = JsonElementHelper.FromJson("""["/src/Program.cs", "/src/Other.cs"]""")
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments["filePath"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["filePath"].GetString().Should().Be("/src/Program.cs");
        result.RepairHint.Should().Contain("'filePath'");
    }

    [Fact]
    public void RepairArguments_StringWhereNumberExpected_ConvertsToNumber()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["offset"] = new() { Type = "integer" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["offset"] = JsonElementHelper.FromString("42")
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments["offset"].ValueKind.Should().Be(JsonValueKind.Number);
        result.RepairedArguments["offset"].GetInt32().Should().Be(42);
        result.RepairHint.Should().Contain("'offset'");
    }

    [Fact]
    public void RepairArguments_NumberWhereStringExpected_ConvertsToString()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["lineNumber"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["lineNumber"] = JsonElementHelper.FromInt32(42)
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments["lineNumber"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["lineNumber"].GetString().Should().Be("42");
        result.RepairHint.Should().Contain("'lineNumber'");
    }

    [Fact]
    public void RepairArguments_BooleanWhereStringExpected_ConvertsToString()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["flag"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["flag"] = JsonElementHelper.FromBoolean(true)
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments["flag"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["flag"].GetString().Should().Be("true");
    }

    [Fact]
    public void RepairArguments_MultipleWrongNames_RenamesAll()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["filePath"] = new() { Type = "string" },
                ["old_string"] = new() { Type = "string" },
                ["new_string"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonElementHelper.FromString("/src/a.cs"),
            ["oldString"] = JsonElementHelper.FromString("foo"),
            ["newString"] = JsonElementHelper.FromString("bar")
        };

        var result = ToolCallRepairService.RepairArguments("EditFile", args, schema);

        result.RepairedArguments.Should().ContainKey("filePath");
        result.RepairedArguments.Should().ContainKey("old_string");
        result.RepairedArguments.Should().ContainKey("new_string");
        result.RepairedArguments.Should().NotContainKey("file_path");
        result.RepairedArguments.Should().NotContainKey("oldString");
        result.RepairedArguments.Should().NotContainKey("newString");
    }

    [Fact]
    public void RepairArguments_NoRepairsNeeded_ReturnsOriginal()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["filePath"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["filePath"] = JsonElementHelper.FromString("/src/Program.cs")
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", args, schema);

        result.RepairedArguments.Should().BeSameAs(args);
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairArguments_EmptySchema_ReturnsOriginal()
    {
        var schema = new ToolSchema();
        var args = new Dictionary<string, JsonElement>
        {
            ["anything"] = JsonElementHelper.FromString("value")
        };

        var result = ToolCallRepairService.RepairArguments("Tool", args, schema);

        result.RepairedArguments.Should().BeSameAs(args);
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairArguments_NullArguments_ReturnsEmpty()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["filePath"] = new() { Type = "string" }
            }
        };

        var result = ToolCallRepairService.RepairArguments("ReadFile", null!, schema);

        result.RepairedArguments.Should().BeEmpty();
        result.RepairHint.Should().BeNull();
    }

    [Fact]
    public void RepairJson_MultipleTrailingCommas_FixesAll()
    {
        var result = ToolCallRepairService.RepairJson("""{"a": 1, "b": [1, 2,],}""");

        result.Success.Should().BeTrue();
        result.RepairHint.Should().Contain("trailing comma");
    }

    [Fact]
    public void RepairArguments_ObjectWhereStringExpected_SerializesToString()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["query"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonElementHelper.FromJson("""{"key": "value"}""")
        };

        var result = ToolCallRepairService.RepairArguments("Search", args, schema);

        result.RepairedArguments["query"].ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void RepairArguments_ArrayOfOneWhereStringExpected_Unwraps()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["url"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonElementHelper.FromJson("""["https://example.com"]""")
        };

        var result = ToolCallRepairService.RepairArguments("WebFetch", args, schema);

        result.RepairedArguments["url"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["url"].GetString().Should().Be("https://example.com");
    }

    [Fact]
    public void RepairArguments_EmptyArrayWhereStringExpected_ConvertsToEmptyString()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["pattern"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["pattern"] = JsonElementHelper.FromJson("[]")
        };

        var result = ToolCallRepairService.RepairArguments("Search", args, schema);

        result.RepairedArguments["pattern"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["pattern"].GetString().Should().BeEmpty();
    }

    [Fact]
    public void RepairArguments_StringWhereBooleanExpected_ConvertsToBoolean()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["recursive"] = new() { Type = "boolean" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["recursive"] = JsonElementHelper.FromString("true")
        };

        var result = ToolCallRepairService.RepairArguments("Search", args, schema);

        result.RepairedArguments["recursive"].ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public void RepairArguments_StringWhereBooleanExpected_InvalidValue_KeepsOriginal()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["recursive"] = new() { Type = "boolean" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["recursive"] = JsonElementHelper.FromString("yes")
        };

        var result = ToolCallRepairService.RepairArguments("Search", args, schema);

        result.RepairedArguments["recursive"].ValueKind.Should().Be(JsonValueKind.String);
        result.RepairedArguments["recursive"].GetString().Should().Be("yes");
    }

    [Theory]
    [InlineData("read", "Read")]
    [InlineData("READ", "Read")]
    [InlineData("Read", "Read")]
    [InlineData("write", "Write")]
    [InlineData("WRITE", "Write")]
    [InlineData("edit", "Edit")]
    [InlineData("glob", "Glob")]
    [InlineData("GLOB", "Glob")]
    [InlineData("grep", "Grep")]
    [InlineData("bash", "Bash")]
    [InlineData("BASH", "Bash")]
    [InlineData("Bash", "Bash")]
    [InlineData("webfetch", "WebFetch")]
    [InlineData("WEBFETCH", "WebFetch")]
    [InlineData("WebFetch", "WebFetch")]
    [InlineData("directory_list", "directory_list")]
    [InlineData("DIRECTORY_LIST", "directory_list")]
    public void RepairToolName_NormalizesCase(string input, string expected)
    {
        // LLM 返回的工具名可能是任意大小写，归一化后应返回标准名
        var result = ToolCallRepairService.RepairToolName(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void RepairToolName_UnknownTool_ReturnsOriginal()
    {
        // 未知工具名（如 MCP 工具或自定义工具）应原样返回
        var result = ToolCallRepairService.RepairToolName("custom_mcp_tool");

        result.Should().Be("custom_mcp_tool");
    }

    [Fact]
    public void RepairToolName_NullOrEmpty_ReturnsEmpty()
    {
        ToolCallRepairService.RepairToolName(null).Should().BeEmpty();
        ToolCallRepairService.RepairToolName("").Should().BeEmpty();
    }

    /// <summary>
    /// G2 场景: PascalCase 参数名（如 Pattern/Path）应被修复为 snake_case（pattern/path）
    /// 根因: RepairParameterNames 使用 OrdinalIgnoreCase HashSet，Contains("Pattern") 返回 true，
    ///       但直接用原 key 存储，导致 Grep 工具收到的参数 key 仍是 "Pattern" 而非 "pattern"
    /// </summary>
    [Fact]
    public void RepairArguments_PascalCaseParameter_RenamesToSnakeCase()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["pattern"] = new() { Type = "string" },
                ["path"] = new() { Type = "string" }
            }
        };
        var args = new Dictionary<string, JsonElement>
        {
            ["Pattern"] = JsonElementHelper.FromString("JoinCode"),
            ["Path"] = JsonElementHelper.FromString("/src/README.md")
        };

        var result = ToolCallRepairService.RepairArguments("Grep", args, schema);

        result.RepairedArguments.Should().ContainKey("pattern");
        result.RepairedArguments.Should().ContainKey("path");
        result.RepairedArguments.Should().NotContainKey("Pattern");
        result.RepairedArguments.Should().NotContainKey("Path");
        result.RepairHint.Should().Contain("'Pattern'");
        result.RepairHint.Should().Contain("'pattern'");
    }

    /// <summary>
    /// 别名匹配不应覆盖已由直接匹配设置的参数值。
    /// 场景: schema 有 file_path，LLM 同时发送 file_path(直接匹配) 和 path(别名→filePath→snake_case file_path)。
    /// 修复前: path 的别名匹配会覆盖 file_path 的正确值（数据丢失，且依赖 JSON key 顺序不可重现）。
    /// 修复后: 直接匹配优先，别名不覆盖已占用的 key。
    /// </summary>
    [Fact]
    public void RepairArguments_AliasWouldOverwriteDirectMatch_PreservesDirectMatchValue()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["file_path"] = new() { Type = "string" }
            }
        };
        // file_path 直接匹配 schema，path 经别名→filePath→snake_case 也映射到 file_path
        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonElementHelper.FromString("/src/README.md"),
            ["path"] = JsonElementHelper.FromString("/other/path")
        };

        var result = ToolCallRepairService.RepairArguments("Read", args, schema);

        // 直接匹配的值必须保留，不被别名覆盖
        result.RepairedArguments["file_path"].GetString().Should().Be("/src/README.md");
    }

    /// <summary>
    /// 反向 JSON key 顺序也应保留直接匹配的值（验证修复不依赖 key 顺序）。
    /// </summary>
    [Fact]
    public void RepairArguments_AliasBeforeDirectMatch_StillPreservesDirectMatchValue()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["file_path"] = new() { Type = "string" }
            }
        };
        // path(别名)在前，file_path(直接匹配)在后
        var args = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonElementHelper.FromString("/other/path"),
            ["file_path"] = JsonElementHelper.FromString("/src/README.md")
        };

        var result = ToolCallRepairService.RepairArguments("Read", args, schema);

        // 无论 key 顺序如何，直接匹配的值必须保留
        result.RepairedArguments["file_path"].GetString().Should().Be("/src/README.md");
    }

    [Fact]
    public void RepairJson_TruncatedObject_ClosesBraces()
    {
        var result = ToolCallRepairService.RepairJson("""{"filePath": "/src/Program.cs""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("filePath").GetString().Should().Be("/src/Program.cs");
        result.RepairHint.Should().Contain("truncated");
    }

    [Fact]
    public void RepairJson_TruncatedNestedObject_ClosesAllBraces()
    {
        var result = ToolCallRepairService.RepairJson("""{"outer": {"inner": "value""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("outer").GetProperty("inner").GetString().Should().Be("value");
    }

    [Fact]
    public void RepairJson_TruncatedArray_ClosesBrackets()
    {
        var result = ToolCallRepairService.RepairJson("""{"paths": ["/a", "/b""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("paths").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void RepairJson_TruncatedString_ClosesQuote()
    {
        var result = ToolCallRepairService.RepairJson("""{"filePath": "/src/Prog""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("filePath").GetString().Should().Be("/src/Prog");
        result.RepairHint.Should().Contain("truncated string");
    }

    [Fact]
    public void RepairJson_TruncatedMixedStructure_ClosesAll()
    {
        var result = ToolCallRepairService.RepairJson("""{"items": [{"name": "test""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("test");
    }

    [Fact]
    public void RepairJson_TrailingCommaAndTruncation_FixesBoth()
    {
        var result = ToolCallRepairService.RepairJson("""{"a": 1, "b": 2,}""");

        result.Success.Should().BeTrue();
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("a").GetInt32().Should().Be(1);
        parsed.RootElement.GetProperty("b").GetInt32().Should().Be(2);
    }

    [Fact]
    public void RepairJson_TruncatedWithTrailingComma_ClosesAndRemovesComma()
    {
        var input = """{"a": 1, "b": 2,""";
        var result = ToolCallRepairService.RepairJson(input);

        result.Success.Should().BeTrue($"Input: '{input}', RepairedJson: '{result.RepairedJson}', Hint: {result.RepairHint}");
        var parsed = JsonDocument.Parse(result.RepairedJson);
        parsed.RootElement.GetProperty("a").GetInt32().Should().Be(1);
        parsed.RootElement.GetProperty("b").GetInt32().Should().Be(2);
    }
}
