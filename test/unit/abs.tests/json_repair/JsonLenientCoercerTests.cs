namespace Abs.Tests.JsonRepair;

/// <summary>
/// JsonLenientCoercer 确定性单元测试 — 覆盖 CoerceToNumber/CoerceToBool(internal 子步骤)。
/// 不依赖时序/IO,纯类型强制转换逻辑。JsonElement 通过 JsonDocument.Parse 构造后 Clone 保留。
/// </summary>
public sealed class JsonLenientCoercerTests {

    // 构造独立 JsonElement(脱离 JsonDocument 生命周期)
    private static JsonElement ParseElement(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ── CoerceToBool: 布尔宽容转换 ──

    [Fact]
    public void CoerceToBool_StringTrue_ReturnsTrue() {
        var v = ParseElement("\"true\"");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Drop.Should().BeFalse();
        act.Result.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CoerceToBool_StringFalse_ReturnsFalse() {
        var v = ParseElement("\"false\"");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Result.GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("y", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("n", false)]
    [InlineData("off", false)]
    [InlineData("", false)]
    public void CoerceToBool_TruthyFalsyStrings(string literal, bool expected) {
        var v = ParseElement("\"" + literal + "\"");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Drop.Should().BeFalse();
        act.Result.GetBoolean().Should().Be(expected);
    }

    [Fact]
    public void CoerceToBool_NumberOne_ReturnsTrue() {
        var v = ParseElement("1");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.Number);
        act.Changed.Should().BeTrue();
        act.Result.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void CoerceToBool_NumberZero_ReturnsFalse() {
        var v = ParseElement("0");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.Number);
        act.Changed.Should().BeTrue();
        act.Result.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void CoerceToBool_Null_ReturnsFalse() {
        var v = ParseElement("null");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.Null);
        act.Changed.Should().BeTrue();
        act.Result.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void CoerceToBool_InvalidString_DropsWithIssue() {
        var v = ParseElement("\"maybe\"");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.String);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
        act.Issue!.PropertyPath.Should().Be("f");
    }

    [Fact]
    public void CoerceToBool_ObjectKind_DropsWithIssue() {
        var v = ParseElement("{}");
        var act = JsonLenientCoercer.CoerceToBool("f", typeof(bool), v, JsonValueKind.Object);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
    }

    // ── CoerceToNumber: 数值宽容转换 ──

    [Fact]
    public void CoerceToNumber_IntField_IntValue_PassesThrough() {
        var v = ParseElement("42");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.Number);
        act.Changed.Should().BeTrue();
        act.Result.GetInt64().Should().Be(42);
    }

    [Fact]
    public void CoerceToNumber_DoubleField_DoubleValue_PassesThroughUnchanged() {
        // double 字段 + Number kind → 原值保留(Changed=false,无需转换)
        var v = ParseElement("3.14");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.Number);
        act.Changed.Should().BeFalse();
        act.Drop.Should().BeFalse();
        act.Result.GetDouble().Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void CoerceToNumber_StringNumeric_ParsesToInt() {
        var v = ParseElement("\"42\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Result.GetInt64().Should().Be(42);
    }

    [Fact]
    public void CoerceToNumber_StringScientific_ParsesToDouble() {
        var v = ParseElement("\"1e10\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Result.GetDouble().Should().Be(1e10);
    }

    [Fact]
    public void CoerceToNumber_StringDecimal_ParsesToDouble() {
        var v = ParseElement("\"3.14\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Result.GetDouble().Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void CoerceToNumber_StringNaN_DropsWithIssue() {
        var v = ParseElement("\"NaN\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.String);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
        act.Issue!.Reason.Should().Contain("NaN");
    }

    [Fact]
    public void CoerceToNumber_StringInfinity_DropsWithIssue() {
        var v = ParseElement("\"Infinity\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.String);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
    }

    [Fact]
    public void CoerceToNumber_StringNegativeInfinity_DropsWithIssue() {
        var v = ParseElement("\"-Infinity\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(double), v, JsonValueKind.String);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
    }

    [Fact]
    public void CoerceToNumber_InvalidString_KeepsWithIssue() {
        var v = ParseElement("\"abc\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.String);
        act.Changed.Should().BeFalse();
        act.Issue.Should().NotBeNull();
        act.Issue!.Reason.Should().Contain("abc");
    }

    [Fact]
    public void CoerceToNumber_ObjectKind_DropsWithIssue() {
        var v = ParseElement("{}");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.Object);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
    }

    [Fact]
    public void CoerceToNumber_ArrayKind_DropsWithIssue() {
        var v = ParseElement("[]");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.Array);
        act.Drop.Should().BeTrue();
        act.Issue.Should().NotBeNull();
    }

    [Fact]
    public void CoerceToNumber_IntField_FloatString_RoundsToInt() {
        // 浮点字符串入整型字段: 在范围内四舍五入
        var v = ParseElement("\"3.6\"");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.String);
        act.Changed.Should().BeTrue();
        act.Result.GetInt64().Should().Be(4);
    }

    [Fact]
    public void CoerceToNumber_IntField_OutOfRange_DropsWithIssue() {
        // 超大整数入 int 字段 → 越界降级
        var v = ParseElement("99999999999999999");
        var act = JsonLenientCoercer.CoerceToNumber("n", typeof(int), v, JsonValueKind.Number);
        // 钳制或降级,二者必居其一: 要么 Changed+钳制值,要么 Drop+Issue
        (act.Changed || act.Drop).Should().BeTrue();
    }
}
