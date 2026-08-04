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