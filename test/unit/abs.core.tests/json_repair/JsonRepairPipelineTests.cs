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

    // ── AppendQuotedValue: 从 FixUnquotedValues 拆出的纯计算子方法 ──
    // 给定 (sb, s, start, end) → 加引号 + 裸反斜杠转义为 \\,无时序/IO 依赖

    [Fact]
    public void AppendQuotedValue_EmptyRange_ProducesEmptyQuotedString() {
        // start == end → 空内容加引号
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "abc", 1, 1);
        sb.ToString().Should().Be("\"\"");
    }

    [Fact]
    public void AppendQuotedValue_PlainText_WrappedInQuotes() {
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "hello", 0, 5);
        sb.ToString().Should().Be("\"hello\"");
    }

    [Fact]
    public void AppendQuotedValue_Subrange_OnlySpecifiedRangeQuoted() {
        // 只取 [1,4) = "ell"
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "hello", 1, 4);
        sb.ToString().Should().Be("\"ell\"");
    }

    [Fact]
    public void AppendQuotedValue_BareBackslash_Doubled() {
        // 裸反斜杠 → \\ (JSON 合法转义)。输入 "a\b" (3字符) → 输出 "a\\b" (6字符)
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "a\\b", 0, 3);
        sb.ToString().Should().Be("\"a\\\\b\"");
    }

    [Fact]
    public void AppendQuotedValue_MultipleBackslashes_AllDoubled() {
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "a\\b\\c", 0, 5);
        sb.ToString().Should().Be("\"a\\\\b\\\\c\"");
    }

    [Fact]
    public void AppendQuotedValue_BackslashAtStart_Doubled() {
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "\\ab", 0, 3);
        sb.ToString().Should().Be("\"\\\\ab\"");
    }

    [Fact]
    public void AppendQuotedValue_BackslashAtEnd_Doubled() {
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "ab\\", 0, 3);
        sb.ToString().Should().Be("\"ab\\\\\"");
    }

    [Fact]
    public void AppendQuotedValue_DoubleQuote_PreservedAsIs() {
        // 双引号不转义(此方法只转义反斜杠,双引号由其他阶段处理)
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "a\"b", 0, 3);
        sb.ToString().Should().Be("\"a\"b\"");
    }

    [Fact]
    public void AppendQuotedValue_AppendsToExistingBuilder() {
        // 追加到已有内容的 StringBuilder,不重置
        var sb = new StringBuilder("prefix:");
        JsonRepairPipeline.AppendQuotedValue(sb, "x", 0, 1);
        sb.ToString().Should().Be("prefix:\"x\"");
    }

    [Fact]
    public void AppendQuotedValue_OnlyBackslash_ProducesQuotedDoubleBackslash() {
        // 单个反斜杠 → "\\"
        var sb = new StringBuilder();
        JsonRepairPipeline.AppendQuotedValue(sb, "\\", 0, 1);
        sb.ToString().Should().Be("\"\\\\\"");
    }

    // ── ShouldQuoteValueStart: 从 FixUnquotedValues 拆出的纯计算子方法 ──
    // 给定 (c, json, i) → bool,判断冒号后 value 起点是否需要加引号

    [Fact]
    public void ShouldQuoteValueStart_DoubleQuote_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('"', """{"a":"x"}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_SingleQuote_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('\'', """{"a":'x'}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_ObjectOpenBrace_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('{', """{"a":{"b":1}}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_ArrayOpenBracket_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('[', """{"a":[1]}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_Digit_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('1', """{"a":1}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_MinusSign_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('-', """{"a":-1}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_PlusSign_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('+', """{"a":+1}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_TrueLiteral_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('t', """{"a":true}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_FalseLiteral_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('f', """{"a":false}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_NullLiteral_ReturnsFalse() {
        JsonRepairPipeline.ShouldQuoteValueStart('n', """{"a":null}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_TrueCaseInsensitive_ReturnsFalse() {
        // IsLiteralAt 用 ToLowerInvariant,大写 True 也识别为字面量
        JsonRepairPipeline.ShouldQuoteValueStart('T', """{"a":True}""", 5).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralFollowedByLetter_ReturnsTrue() {
        // truex 后跟字母 → 不是合法字面量边界 → 需要加引号
        JsonRepairPipeline.ShouldQuoteValueStart('t', """{"a":truex}""", 5).Should().BeTrue();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralFollowedByDigit_ReturnsTrue() {
        // null1 后跟数字 → 不是字面量
        JsonRepairPipeline.ShouldQuoteValueStart('n', """{"a":null1}""", 5).Should().BeTrue();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralFollowedByUnderscore_ReturnsTrue() {
        // true_ 后跟下划线 → 不是字面量
        JsonRepairPipeline.ShouldQuoteValueStart('t', """{"a":true_}""", 5).Should().BeTrue();
    }

    [Fact]
    public void ShouldQuoteValueStart_BareWord_ReturnsTrue() {
        // 裸词 hello → 需要加引号
        JsonRepairPipeline.ShouldQuoteValueStart('h', """{"a":hello}""", 5).Should().BeTrue();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralAtEndOfInput_ReturnsFalse() {
        // 字面量在字符串末尾(无后续字符) → 是合法字面量。":true" i=1 指向 t
        JsonRepairPipeline.ShouldQuoteValueStart('t', ":true", 1).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralFollowedByComma_ReturnsFalse() {
        // true, 后跟逗号 → 是合法字面量边界。构造 ":true," i=1
        JsonRepairPipeline.ShouldQuoteValueStart('t', ":true,", 1).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuoteValueStart_LiteralFollowedByCloseBrace_ReturnsFalse() {
        // true} 后跟 } → 是合法字面量边界
        JsonRepairPipeline.ShouldQuoteValueStart('t', ":true}", 1).Should().BeFalse();
    }
}
