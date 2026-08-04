namespace Integration.Tests.PrefixCache.Unit;

/// <summary>
/// 类型转换宽容层测试 — 覆盖 LlmJsonHelper 统一门控的纵深防御第三层（类型强制转换）
/// 当前测试为 RED：在未实现类型强制转换前，number→bool、number→string 等会失败并落入第4层报错
/// </summary>
public sealed class LenientCoercionTests
{
    [Fact]
    public void Coerce_NumberToBool_1_IsTrue()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": 1, "reason": "done"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Completed.Should().BeTrue();
        result.Reason.Should().Be("done");
    }

    [Fact]
    public void Coerce_NumberToBool_0_IsFalse()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": 0, "reason": "not done"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Completed.Should().BeFalse();
    }

    [Fact]
    public void Coerce_NumberToString_123_Becomes_String()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "reason": 123}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Reason.Should().Be("123");
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public void Coerce_BoolToString_True_Becomes_String()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "reason": true}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Reason.Should().Be("true");
    }

    [Fact]
    public void Coerce_StringToNumber_42_Becomes_Int()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": false, "count": "42"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Count.Should().Be(42);
    }

    [Fact]
    public void Coerce_StringToNullableNumber_Parses()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "ratio": "0.75"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Ratio.Should().Be(0.75);
    }

    [Fact]
    public void Coerce_StringTrim_RemovesWhitespace()
    {
        // completed=1 触发第3层类型转换，转换过程中对 string 字段执行 Trim
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": 1, "reason": "  done  "}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Reason.Should().Be("done");
        result.Completed.Should().BeTrue();
    }

    [Fact]
    public void Coerce_UncoerceableField_ReportsPreciseIssue()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": "definitely-not-a-bool", "reason": "break"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        // 坏字段被降级为默认值（false），不再导致整条解析崩溃
        result.Should().NotBeNull();
        result!.Completed.Should().BeFalse();

        // 同时面向 LLM 输出精确错误：哪个字段崩坏了
        report.CoercionIssues.Should().ContainSingle();
        report.CoercionIssues[0].PropertyPath.Should().Be("completed");
        report.CoercionIssues[0].ExpectedType.Should().Be("Boolean");
        report.CoercionIssues[0].ActualValueKind.Should().Be("String");
    }

    [Fact]
    public void Coerce_SyntaxRepairHint_IsSurfaced()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": 1, "reason": "ok",}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        result.Should().NotBeNull();
        result!.Completed.Should().BeTrue();
        report.RepairHint.Should().NotBeNull();
    }

    [Fact]
    public void BOM_Header_Is_Stripped()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            "\uFEFF{\"completed\": true, \"reason\": \"bom\"}",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Completed.Should().BeTrue();
        result.Reason.Should().Be("bom");
    }

    [Fact]
    public void RepairJson_HexNumber_ConvertsToDecimal()
    {
        var repair = LlmJsonHelper.RepairJson("""{"mask": 0xFF}""");

        repair.Success.Should().BeTrue();
        repair.RepairedJson.Should().Contain("255");
    }

    [Fact]
    public void RepairJson_LeadingZeroNumber_StripsZeros()
    {
        var repair = LlmJsonHelper.RepairJson("""{"count": 0123}""");

        repair.Success.Should().BeTrue();
        repair.RepairedJson.Should().Contain("\"count\": 123");
    }

    [Fact]
    public void RepairJson_HexInsideString_IsNotTouched()
    {
        var repair = LlmJsonHelper.RepairJson("""{"mask": "0xFF"}""");

        repair.Success.Should().BeTrue();
        repair.RepairedJson.Should().Contain("0xFF");
    }

    [Fact]
    public void Coerce_OutOfRangeNumber_IsClamped()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "score": 3000000000}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        result.Should().NotBeNull();
        result!.Score.Should().Be(int.MaxValue);
        report.CoercionIssues.Should().ContainSingle(i => i.PropertyPath == "score");
    }

    [Fact]
    public void Coerce_UndefinedEnum_Defaults_And_Reports()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "level": "UNDEFINED_VALUE"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        result.Should().NotBeNull();
        result!.Level.Should().Be(default);
        report.CoercionIssues.Should().ContainSingle(i => i.PropertyPath == "level");
    }

    [Fact]
    public void Coerce_ValidEnum_IsKept()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "level": "High"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out _);

        result.Should().NotBeNull();
        result!.Level.Should().Be(LenientLevel.High);
    }

    [Fact]
    public void Coerce_ObjectIntoBool_DefaultsAndReportsPreciseIssue()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": {"nested": true}, "reason": "x"}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        result.Should().NotBeNull();
        result!.Completed.Should().BeFalse();
        report.CoercionIssues.Should().Contain(i => i.PropertyPath == "completed");
        report.FormatForLlm().Should().Contain("completed");
    }

    [Fact]
    public void Coerce_ArrayIntoNumber_DefaultsAndReportsPreciseIssue()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "count": [1, 2]}""",
            CoercionTestJsonContext.Default.LenientDto,
            out var report);

        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
        report.CoercionIssues.Should().Contain(i => i.PropertyPath == "count");
        report.FormatForLlm().Should().Contain("count");
    }

    #region P1: null 字符串宽容

    [Theory]
    [InlineData("null")]
    [InlineData("none")]
    [InlineData("nil")]
    [InlineData("")]
    public void Coerce_NullLikeString_ToNullableBool_IsNull(string nullLike)
    {
        var json = $$"""{"completed": true, "nullableFlag": "{{nullLike}}"}""";
        var result = LlmJsonHelper.DeserializeWithReport(
            json, CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.NullableFlag.Should().BeNull();
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("None")]
    [InlineData("Nil")]
    public void Coerce_NullLikeString_CaseInsensitive_ToNullableBool_IsNull(string nullLike)
    {
        var json = $$"""{"completed": true, "nullableFlag": "{{nullLike}}"}""";
        var result = LlmJsonHelper.DeserializeWithReport(
            json, CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.NullableFlag.Should().BeNull();
    }

    #endregion

    #region P3: 日期时间宽容

    [Fact]
    public void Coerce_DateString_ToDateTime_Parses()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "createdAt": "2025-01-15T10:30:00"}""",
            CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.CreatedAt.Should().NotBeNull();
        result.CreatedAt!.Value.Year.Should().Be(2025);
        result.CreatedAt.Value.Month.Should().Be(1);
        result.CreatedAt.Value.Day.Should().Be(15);
    }

    [Fact]
    public void Coerce_DateStringSlash_ToDateTime_Parses()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "createdAt": "2025/01/15"}""",
            CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.CreatedAt.Should().NotBeNull();
        result.CreatedAt!.Value.Year.Should().Be(2025);
    }

    [Fact]
    public void Coerce_EpochNumber_ToDateTime_Parses()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "createdAt": 1735689600000}""",
            CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.CreatedAt.Should().NotBeNull();
        result.CreatedAt!.Value.Year.Should().Be(2025);
    }

    [Fact]
    public void Coerce_NullLikeString_ToNullableDateTime_IsNull()
    {
        var result = LlmJsonHelper.DeserializeWithReport(
            """{"completed": true, "createdAt": "none"}""",
            CoercionTestJsonContext.Default.LenientDto, out _);

        result.Should().NotBeNull();
        result!.CreatedAt.Should().BeNull();
    }

    #endregion
}

/// <summary>
/// 类型强制转换测试专用 DTO
/// </summary>
public sealed class LenientDto
{
    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("ratio")]
    public double? Ratio { get; set; }

    [JsonPropertyName("level")]
    public LenientLevel Level { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("nullableFlag")]
    public bool? NullableFlag { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// 枚举宽容测试专用枚举
/// </summary>
public enum LenientLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// 类型强制转换测试专用 Json 上下文
/// </summary>
[JsonSerializable(typeof(LenientDto))]
[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNameCaseInsensitive = true)]
public partial class CoercionTestJsonContext : JsonSerializerContext;