namespace Infra.Tests.Text;

/// <summary>
/// JsonArgumentParser 单元测试 — 验证正常 JSON / 损坏 JSON 修复 / 空输入
/// </summary>
public sealed class JsonArgumentParserTest
{
    // === 空与 null 输入 ===

    [Fact]
    public void Parse_NullInput_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse(null);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("   ");

        result.Should().BeEmpty();
    }

    // === 正常 JSON ===

    [Fact]
    public void Parse_ValidJson_ReturnsParsedDictionary()
    {
        var result = JsonArgumentParser.Parse("""{"key":"value","num":42}""");

        result.Should().HaveCount(2);
        result["key"].GetString().Should().Be("value");
        result["num"].GetInt32().Should().Be(42);
    }

    [Fact]
    public void Parse_EmptyJsonObject_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("{}");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NestedJson_ReturnsNestedElement()
    {
        var result = JsonArgumentParser.Parse("""{"outer":{"inner":"value"}}""");

        result.Should().HaveCount(1);
        result["outer"].GetProperty("inner").GetString().Should().Be("value");
    }

    [Fact]
    public void Parse_JsonArrayValue_ReturnsArrayElement()
    {
        var result = JsonArgumentParser.Parse("""{"items":["a","b","c"]}""");

        result["items"].GetArrayLength().Should().Be(3);
        result["items"][0].GetString().Should().Be("a");
        result["items"][2].GetString().Should().Be("c");
    }

    [Fact]
    public void Parse_BooleanValue_ReturnsBoolean()
    {
        var result = JsonArgumentParser.Parse("""{"flag":true}""");

        result["flag"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Parse_NullValue_ReturnsNullElement()
    {
        var result = JsonArgumentParser.Parse("""{"key":null}""");

        result["key"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    // === 宽容 JSON（第1层：ContractsJsonContext 配置） ===

    [Fact]
    public void Parse_JsonWithTrailingCommas_DirectlyParsed()
    {
        var result = JsonArgumentParser.Parse("""{"key":"value","num":42,}""");

        result.Should().HaveCount(2);
        result["key"].GetString().Should().Be("value");
        result["num"].GetInt32().Should().Be(42);
    }

    [Fact]
    public void Parse_JsonWithComments_DirectlyParsed()
    {
        var result = JsonArgumentParser.Parse("""{"key":"value" /* comment */}""");

        result.Should().HaveCount(1);
        result["key"].GetString().Should().Be("value");
    }

    [Fact]
    public void Parse_JsonCaseInsensitiveKeys_ParsedCorrectly()
    {
        var result = JsonArgumentParser.Parse("""{"KEY":"value"}""");

        result.Should().HaveCount(1);
        result["KEY"].GetString().Should().Be("value");
    }

    // === 损坏 JSON 修复（第2层：LlmJsonHelper.RepairJson） ===

    [Fact]
    public void Parse_SingleQuoteJson_RepairedAndParsed()
    {
        var result = JsonArgumentParser.Parse("{'key':'value'}");

        result.Should().HaveCount(1);
        result["key"].GetString().Should().Be("value");
    }

    [Fact]
    public void Parse_UnquotedKeys_RepairedAndParsed()
    {
        var result = JsonArgumentParser.Parse("""{key:"value"}""");

        result.Should().HaveCount(1);
        result["key"].GetString().Should().Be("value");
    }

    // === 内联 JSON 提取（第3层：ExtractInlineJson） ===

    [Fact]
    public void Parse_JsonWrappedInText_ExtractsInlineJson()
    {
        var result = JsonArgumentParser.Parse("Here is the result: {\"key\":\"value\"} done");

        result.Should().HaveCount(1);
        result["key"].GetString().Should().Be("value");
    }

    [Fact]
    public void Parse_JsonWithMarkdownCodeBlock_ExtractsInlineJson()
    {
        var result = JsonArgumentParser.Parse("```json\n{\"key\":\"value\"}\n```");

        result.Should().HaveCount(1);
        result["key"].GetString().Should().Be("value");
    }

    // === 完全无法解析 ===

    [Fact]
    public void Parse_CompletelyBroken_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("not json at all");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_OnlyOpeningBrace_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("{");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsEmptyDictionary()
    {
        var result = JsonArgumentParser.Parse("""{"key":}""");

        result.Should().BeEmpty();
    }
}
