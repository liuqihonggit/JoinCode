namespace Hands.Tests;

/// <summary>
/// ToolArgumentParser 单元测试 — 验证参数解析、键值对解析、引号感知分割、
/// 值类型推断(bool/int/long/double/string)、JSON 元素类型校验。
/// 纯计算确定性测试。
/// </summary>
public class ToolArgumentParserTest {
    private readonly ToolArgumentParser _parser = new();

    // ===== Parse 参数解析 =====

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDict() {
        var result = _parser.Parse("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyDict() {
        var result = _parser.Parse("   ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_JsonObject_ReturnsParsedDict() {
        var result = _parser.Parse("{\"key\":\"value\",\"num\":123}");
        result.Should().ContainKey("key");
        result["key"].GetString().Should().Be("value");
        result["num"].GetInt32().Should().Be(123);
    }

    [Fact]
    public void Parse_KeyValuePairs_ReturnsParsedDict() {
        var result = _parser.Parse("key=value");
        result.Should().ContainKey("key");
        result["key"].GetString().Should().Be("value");
    }

    [Fact]
    public void Parse_KeyValueInteger_ReturnsInt() {
        var result = _parser.Parse("key=123");
        result["key"].ValueKind.Should().Be(JsonValueKind.Number);
        result["key"].GetInt32().Should().Be(123);
    }

    // ===== ParseKeyValuePairs 键值对解析 =====

    [Fact]
    public void ParseKeyValuePairs_SimplePair_ReturnsString() {
        var result = _parser.ParseKeyValuePairs("key=value");
        result["key"].GetString().Should().Be("value");
    }

    [Fact]
    public void ParseKeyValuePairs_MultiplePairs_ReturnsAll() {
        var result = _parser.ParseKeyValuePairs("a=1 b=2");
        result["a"].GetInt32().Should().Be(1);
        result["b"].GetInt32().Should().Be(2);
    }

    [Fact]
    public void ParseKeyValuePairs_BooleanValue_ReturnsBool() {
        var result = _parser.ParseKeyValuePairs("flag=true");
        result["flag"].ValueKind.Should().Be(JsonValueKind.True);
        result["flag"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ParseKeyValuePairs_DoubleValue_ReturnsDouble() {
        var result = _parser.ParseKeyValuePairs("pi=3.14");
        result["pi"].GetDouble().Should().Be(3.14);
    }

    [Fact]
    public void ParseKeyValuePairs_NoEqualsSign_SkipsPair() {
        var result = _parser.ParseKeyValuePairs("noequals");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseKeyValuePairs_EmptyKey_SkipsPair() {
        // separatorIndex == 0,不 > 0,跳过
        var result = _parser.ParseKeyValuePairs("=value");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseKeyValuePairs_QuotedValue_ParsesCorrectly() {
        var result = _parser.ParseKeyValuePairs("key=\"hello world\"");
        result["key"].GetString().Should().Be("hello world");
    }

    // ===== SplitArguments 引号感知参数分割 =====

    [Fact]
    public void SplitArguments_SimpleSpaceSeparated_ReturnsTokens() {
        _parser.SplitArguments("a b").Should().Equal(["a", "b"]);
    }

    [Fact]
    public void SplitArguments_MultipleSpaces_ReturnsTokens() {
        _parser.SplitArguments("a b c").Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void SplitArguments_DoubleQuotedSpace_PreservesInToken() {
        _parser.SplitArguments("\"a b\"").Should().Equal(["a b"]);
    }

    [Fact]
    public void SplitArguments_SingleQuotedSpace_PreservesInToken() {
        _parser.SplitArguments("'a b'").Should().Equal(["a b"]);
    }

    [Fact]
    public void SplitArguments_EmptyString_ReturnsEmpty() {
        _parser.SplitArguments("").Should().BeEmpty();
    }

    [Fact]
    public void SplitArguments_OnlySpaces_ReturnsEmpty() {
        _parser.SplitArguments("   ").Should().BeEmpty();
    }

    [Fact]
    public void SplitArguments_MixedQuotedAndPlain_ReturnsTokens() {
        _parser.SplitArguments("key=\"a b\" c").Should().Equal(["key=a b", "c"]);
    }

    [Fact]
    public void SplitArguments_LeadingTrailingSpaces_Trimmed() {
        _parser.SplitArguments("  a b  ").Should().Equal(["a", "b"]);
    }

    // ===== ParseValue 值类型推断(bool/int/long/double/string) =====

    [Fact]
    public void ParseValue_BooleanTrue_ReturnsBool() {
        var el = _parser.ParseValue("true");
        el.ValueKind.Should().Be(JsonValueKind.True);
        el.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ParseValue_BooleanFalse_ReturnsBool() {
        var el = _parser.ParseValue("false");
        el.ValueKind.Should().Be(JsonValueKind.False);
        el.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ParseValue_Integer_ReturnsInt() {
        var el = _parser.ParseValue("123");
        el.ValueKind.Should().Be(JsonValueKind.Number);
        el.GetInt32().Should().Be(123);
    }

    [Fact]
    public void ParseValue_LongBeyondIntRange_ReturnsLong() {
        var el = _parser.ParseValue("9999999999");
        el.ValueKind.Should().Be(JsonValueKind.Number);
        el.GetInt64().Should().Be(9999999999L);
    }

    [Fact]
    public void ParseValue_Double_ReturnsDouble() {
        var el = _parser.ParseValue("3.14");
        el.ValueKind.Should().Be(JsonValueKind.Number);
        el.GetDouble().Should().Be(3.14);
    }

    [Fact]
    public void ParseValue_PlainString_ReturnsString() {
        var el = _parser.ParseValue("hello");
        el.ValueKind.Should().Be(JsonValueKind.String);
        el.GetString().Should().Be("hello");
    }

    [Fact]
    public void ParseValue_DoubleQuotedString_StripsQuotes() {
        var el = _parser.ParseValue("\"quoted\"");
        el.ValueKind.Should().Be(JsonValueKind.String);
        el.GetString().Should().Be("quoted");
    }

    [Fact]
    public void ParseValue_SingleQuotedString_StripsQuotes() {
        var el = _parser.ParseValue("'quoted'");
        el.ValueKind.Should().Be(JsonValueKind.String);
        el.GetString().Should().Be("quoted");
    }

    [Fact]
    public void ParseValue_NegativeInteger_ReturnsInt() {
        var el = _parser.ParseValue("-42");
        el.GetInt32().Should().Be(-42);
    }

    // ===== ValidateType JsonElement 类型校验 =====

    [Fact]
    public void ValidateType_StringKind_ReturnsTrueForString() {
        var el = JsonSerializer.SerializeToElement("hello");
        _parser.ValidateType(el, "string").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_StringKind_ReturnsFalseForInteger() {
        var el = JsonSerializer.SerializeToElement("hello");
        _parser.ValidateType(el, "integer").Should().BeFalse();
    }

    [Fact]
    public void ValidateType_IntegerKind_ReturnsTrueForInteger() {
        var el = JsonSerializer.SerializeToElement(42);
        _parser.ValidateType(el, "integer").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_IntegerKind_ReturnsTrueForNumber() {
        var el = JsonSerializer.SerializeToElement(42);
        _parser.ValidateType(el, "number").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_DoubleKind_ReturnsTrueForNumber() {
        var el = JsonSerializer.SerializeToElement(3.14);
        _parser.ValidateType(el, "number").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_DoubleKind_ReturnsFalseForInteger() {
        var el = JsonSerializer.SerializeToElement(3.14);
        _parser.ValidateType(el, "integer").Should().BeFalse();
    }

    [Fact]
    public void ValidateType_BooleanKind_ReturnsTrueForBoolean() {
        var el = JsonSerializer.SerializeToElement(true);
        _parser.ValidateType(el, "boolean").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_ArrayKind_ReturnsTrueForArray() {
        var el = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });
        _parser.ValidateType(el, "array").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_ObjectKind_ReturnsTrueForObject() {
        var el = JsonSerializer.SerializeToElement(new { x = 1 });
        _parser.ValidateType(el, "object").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_UnknownExpectedType_ReturnsTrue() {
        var el = JsonSerializer.SerializeToElement("hello");
        _parser.ValidateType(el, "unknown").Should().BeTrue();
    }

    [Fact]
    public void ValidateType_CaseInsensitiveExpectedType_ReturnsTrue() {
        var el = JsonSerializer.SerializeToElement("hello");
        _parser.ValidateType(el, "STRING").Should().BeTrue();
    }
}
