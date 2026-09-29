namespace Core.Context;

/// <summary>
/// InformationEntropyGuardian.BuildArgsFingerprint 内部纯函数测试
/// 改 internal static 后直接测指纹构造逻辑:空参数/关键参数/长值截断/fallback
/// </summary>
public sealed class InformationEntropyGuardianBuildArgsFingerprintTests {
    [Fact]
    public void BuildArgsFingerprint_NullArguments_ReturnsNull() {
        Assert.Null(InformationEntropyGuardian.BuildArgsFingerprint("Read", null));
    }

    [Fact]
    public void BuildArgsFingerprint_EmptyDictionary_ReturnsNull() {
        Assert.Null(InformationEntropyGuardian.BuildArgsFingerprint("Read", new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void BuildArgsFingerprint_SingleKeyParam_Included() {
        var args = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonStringValue("/src/Program.cs")
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("Read", args);

        Assert.NotNull(fp);
        Assert.Equal("Read(file_path=/src/Program.cs)", fp);
    }

    [Fact]
    public void BuildArgsFingerprint_MultipleKeyParams_InKnownOrder() {
        var args = new Dictionary<string, JsonElement> {
            ["pattern"] = JsonStringValue("TODO"),
            ["file_path"] = JsonStringValue("/src/Program.cs"),
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("Grep", args);

        // keys 数组顺序:file_path 在 pattern 之前
        Assert.NotNull(fp);
        Assert.Equal("Grep(file_path=/src/Program.cs,pattern=TODO)", fp);
    }

    [Fact]
    public void BuildArgsFingerprint_LongStringValue_TruncatedTo50Chars() {
        var longValue = new string('x', 100);
        var args = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonStringValue(longValue)
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("Read", args);

        Assert.NotNull(fp);
        Assert.Contains("...", fp);
        // 截断后值 = 50 字符 + "..."
        Assert.Contains(new string('x', 50) + "...", fp!);
    }

    [Fact]
    public void BuildArgsFingerprint_NoKeyParams_FallbackToFirstTwoArbitrary() {
        var args = new Dictionary<string, JsonElement> {
            ["custom_key1"] = JsonStringValue("val1"),
            ["custom_key2"] = JsonStringValue("val2"),
            ["custom_key3"] = JsonStringValue("val3"),
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("MyTool", args);

        // fallback 取前 2 个任意参数
        Assert.NotNull(fp);
        Assert.StartsWith("MyTool(", fp);
        Assert.Contains("custom_key1=val1", fp!);
        Assert.Contains("custom_key2=val2", fp!);
        Assert.DoesNotContain("custom_key3", fp!);
    }

    [Fact]
    public void BuildArgsFingerprint_NonStringValue_UsesGetRawText() {
        var args = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonDocument.Parse("42").RootElement.Clone()
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("Read", args);

        Assert.NotNull(fp);
        Assert.Contains("file_path=42", fp!);
    }

    [Fact]
    public void BuildArgsFingerprint_KeyParamPresent_FallbackNotUsed() {
        var args = new Dictionary<string, JsonElement> {
            ["file_path"] = JsonStringValue("/a"),
            ["unknown_key"] = JsonStringValue("should_not_appear"),
        };

        var fp = InformationEntropyGuardian.BuildArgsFingerprint("Read", args);

        Assert.NotNull(fp);
        Assert.DoesNotContain("unknown_key", fp!);
    }

    private static JsonElement JsonStringValue(string s) {
        using var doc = JsonDocument.Parse($"\"{s.Replace("\"", "\\\"")}\"");
        return doc.RootElement.Clone();
    }
}
