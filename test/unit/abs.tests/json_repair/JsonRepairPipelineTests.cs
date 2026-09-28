namespace Abs.Tests.JsonRepair;

/// <summary>
/// JsonRepairPipeline 确定性单元测试 — 不依赖时序/IO,纯字符串修复逻辑。
/// 覆盖 RepairJson(公共入口) + StripOuterQuotes/FixUnquotedValues/FixUnquotedKeys(internal 子步骤)。
/// </summary>
public sealed class JsonRepairPipelineTests {

    // ── RepairJson: 公共多阶段管道入口 ──

    [Fact]
    public void RepairJson_ValidJson_ReturnsAsIs() {
        var result = JsonRepairPipeline.RepairJson("""{"a":1}""");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("""{"a":1}""");
    }

    [Fact]
    public void RepairJson_NullOrWhitespace_ReturnsEmptyObject() {
        JsonRepairPipeline.RepairJson(null).RepairedJson.Should().Be("{}");
        JsonRepairPipeline.RepairJson("").RepairedJson.Should().Be("{}");
        JsonRepairPipeline.RepairJson("   ").RepairedJson.Should().Be("{}");
    }

    [Fact]
    public void RepairJson_NestedValidJson_ReturnsAsIs() {
        var json = """{"a":{"b":[1,2,3]}}""";
        var result = JsonRepairPipeline.RepairJson(json);
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be(json);
    }

    [Fact]
    public void RepairJson_TrailingComma_RemovedAndParses() {
        var result = JsonRepairPipeline.RepairJson("""{"a":1,}""");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Contain("""{"a":1}""");
    }

    [Fact]
    public void RepairJson_SingleQuotedKeys_ConvertedToDoubleQuotes() {
        var result = JsonRepairPipeline.RepairJson("""{'a':1}""");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Contain("\"a\"");
        result.RepairedJson.Should().NotContain("'a'");
    }

    [Fact]
    public void RepairJson_UnquotedKey_Quoted() {
        var result = JsonRepairPipeline.RepairJson("""{a:1}""");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Contain("\"a\"");
    }

    [Fact]
    public void RepairJson_BomStripped() {
        var json = "\uFEFF" + """{"a":1}""";
        var result = JsonRepairPipeline.RepairJson(json);
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("""{"a":1}""");
    }

    [Fact]
    public void RepairJson_TrailingSemicolon_Stripped() {
        var result = JsonRepairPipeline.RepairJson("""{"a":1};""");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("""{"a":1}""");
    }

    [Fact]
    public void RepairJson_OuterQuotes_Stripped() {
        var result = JsonRepairPipeline.RepairJson("\"" + """{"a":1}""" + "\"");
        result.Success.Should().BeTrue();
        result.RepairedJson.Should().Be("""{"a":1}""");
    }

    [Fact]
    public void RepairJson_Unrepairable_ReturnsFailureWithHint() {
        var result = JsonRepairPipeline.RepairJson("not json at all !!!");
        result.Success.Should().BeFalse();
        result.RepairHint.Should().NotBeNull();
    }

    // ── StripOuterQuotes: 内部子步骤 ──

    [Fact]
    public void StripOuterQuotes_SingleQuotedObjectJson_Stripped() {
        JsonRepairPipeline.StripOuterQuotes("'{" + "\"a\":1}'").Should().Be("""{"a":1}""");
    }

    [Fact]
    public void StripOuterQuotes_DoubleQuotedObjectJson_Stripped() {
        // 输入: "{"a":1}" (外层双引号包裹 JSON 对象)
        var input = "\"" + """{"a":1}""" + "\"";
        JsonRepairPipeline.StripOuterQuotes(input).Should().Be("""{"a":1}""");
    }

    [Fact]
    public void StripOuterQuotes_DoubleQuotedArrayJson_Stripped() {
        var input = "\"[1,2,3]\"";
        JsonRepairPipeline.StripOuterQuotes(input).Should().Be("[1,2,3]");
    }

    [Fact]
    public void StripOuterQuotes_NoQuotes_ReturnsAsIs() {
        JsonRepairPipeline.StripOuterQuotes("""{"a":1}""").Should().Be("""{"a":1}""");
    }

    [Fact]
    public void StripOuterQuotes_Backtick_NotTreatedAsQuote() {
        // 反引号不在处理范围,原样返回
        var input = "`{\"a\":1}`";
        JsonRepairPipeline.StripOuterQuotes(input).Should().Be(input);
    }

    [Fact]
    public void StripOuterQuotes_InnerNotStartingWithBraceOrBracket_Kept() {
        // 外层双引号但 inner 不以 { 或 [ 开头 → 保留原样
        var input = "\"hello\"";
        JsonRepairPipeline.StripOuterQuotes(input).Should().Be("\"hello\"");
    }

    [Fact]
    public void StripOuterQuotes_TooShort_ReturnsAsIs() {
        JsonRepairPipeline.StripOuterQuotes("\"").Should().Be("\"");
        JsonRepairPipeline.StripOuterQuotes("").Should().Be("");
    }

    [Fact]
    public void StripOuterQuotes_EmptyQuotedString_ReturnsAsIs() {
        // 长度 2 的 "" → inner 为空,不进 if 分支,原样返回
        JsonRepairPipeline.StripOuterQuotes("\"\"").Should().Be("\"\"");
    }

    [Fact]
    public void StripOuterQuotes_MismatchedQuotes_ReturnsAsIs() {
        // 首尾引号不同 → 不处理
        JsonRepairPipeline.StripOuterQuotes("\"{'a':1}'").Should().Be("\"{'a':1}'");
    }

    // ── FixUnquotedValues: 内部子步骤 ──

    [Fact]
    public void FixUnquotedValues_BareWordValue_Quoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":hello}""", hints);
        result.Should().Contain("\"hello\"");
        hints.Should().NotBeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_NumericValue_NotQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":1}""", hints);
        result.Should().Be("""{"a":1}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_TrueLiteral_NotQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":true}""", hints);
        result.Should().Be("""{"a":true}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_NullLiteral_NotQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":null}""", hints);
        result.Should().Be("""{"a":null}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_AlreadyQuotedString_NotChanged() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":"str"}""", hints);
        result.Should().Be("""{"a":"str"}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_NestedObject_NotQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":{"b":1}}""", hints);
        result.Should().Be("""{"a":{"b":1}}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_NestedArray_NotQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":[1,2]}""", hints);
        result.Should().Be("""{"a":[1,2]}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedValues_MultipleBareWords_AllQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedValues("""{"a":hello,"b":world}""", hints);
        result.Should().Contain("\"hello\"");
        result.Should().Contain("\"world\"");
    }

    // ── FixUnquotedKeys: 内部子步骤 ──

    [Fact]
    public void FixUnquotedKeys_BareKey_Quoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{a:1}""", hints);
        result.Should().Be("""{"a":1}""");
        hints.Should().NotBeEmpty();
    }

    [Fact]
    public void FixUnquotedKeys_AlreadyQuotedKey_NotChanged() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{"a":1}""", hints);
        result.Should().Be("""{"a":1}""");
        hints.Should().BeEmpty();
    }

    [Fact]
    public void FixUnquotedKeys_MultipleBareKeys_AllQuoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{a:1,b:2}""", hints);
        result.Should().Contain("\"a\"");
        result.Should().Contain("\"b\"");
    }

    [Fact]
    public void FixUnquotedKeys_UnderscoreKey_Quoted() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{my_key:1}""", hints);
        result.Should().Contain("\"my_key\"");
    }

    [Fact]
    public void FixUnquotedKeys_NestedObjectKeys_NotMangled() {
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{"a":{"b":1}}""", hints);
        result.Should().Be("""{"a":{"b":1}}""");
    }

    [Fact]
    public void FixUnquotedKeys_StringValueWithBraceNotTreatedAsKey() {
        // 字符串值内的 { 不应被误识别为对象边界
        var hints = new List<string>();
        var result = JsonRepairPipeline.FixUnquotedKeys("""{"a":"x{y}z"}""", hints);
        result.Should().Be("""{"a":"x{y}z"}""");
    }
}
