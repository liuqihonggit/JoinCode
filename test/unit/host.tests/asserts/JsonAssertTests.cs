namespace Host.Tests.Asserts;

/// <summary>JsonAssert 语义断言帮助器 — 覆盖解析/路径/转义/失败信息。</summary>
public sealed class JsonAssertTests {
    private const string Sample = """
        {"scene_id":"sc","elements":[{"type":"button","label":"你好"}],"suggested_next":[{"tool":"desktop_click","reason":"\u8BC6\u522B\u5230\u6309\u94AE"}]}
        """;

    [Fact]
    public void Parse_InvalidJson_ThrowsWithSnippet() {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonAssert.Parse("{not json"));
        Assert.Contains("不是合法 JSON", ex.Message);
    }

    [Fact]
    public void Get_NestedPath_WithArrayIndex_ReturnsValue() {
        using var doc = JsonAssert.Parse(Sample);
        Assert.Equal("button", JsonAssert.Get(doc.RootElement, "elements[0].type").GetString());
    }

    [Fact]
    public void Get_MissingSegment_ThrowsWithPath() {
        using var doc = JsonAssert.Parse(Sample);
        var ex = Assert.Throws<InvalidOperationException>(() => JsonAssert.Get(doc.RootElement, "elements[5].type"));
        Assert.Contains("elements[5].type", ex.Message);
    }

    [Fact]
    public void AssertString_NonAsciiEscaped_ComparesDecodedValue() {
        JsonAssert.AssertString(Sample, "elements[0].label", "你好");
    }

    [Fact]
    public void AssertString_Mismatch_Throws() {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonAssert.AssertString(Sample, "scene_id", "other", "场景编号应一致"));
        Assert.Contains("原因: 场景编号应一致", ex.Message);
    }

    [Fact]
    public void AssertBool_And_AssertInt_Work() {
        const string json = """{"succeeded":true,"x":500}""";
        JsonAssert.AssertBool(json, "succeeded", true);
        JsonAssert.AssertInt(json, "x", 500);
        Assert.Throws<InvalidOperationException>(() => JsonAssert.AssertBool(json, "succeeded", false));
        Assert.Throws<InvalidOperationException>(() => JsonAssert.AssertInt(json, "x", 501));
    }

    [Fact]
    public void AssertArrayContains_Predicate_FoundAndNotFound() {
        JsonAssert.AssertArrayContains(Sample, "suggested_next", e => e.GetProperty("tool").GetString() == "desktop_click");
        Assert.Throws<InvalidOperationException>(() =>
            JsonAssert.AssertArrayContains(Sample, "suggested_next", e => e.GetProperty("tool").GetString() == "desktop_look"));
    }

    [Fact]
    public void AssertAnyStringContains_EscapedChinese_Matches() {
        JsonAssert.AssertAnyStringContains(Sample, "suggested_next", "reason", "识别到按钮");
    }
}
