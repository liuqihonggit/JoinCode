// JCC11003 抑制: 底层实现/测试代码, ! 用于可空抑制, 是必要用法
#pragma warning disable JCC11003
namespace Structura.Tests;

/// <summary>
/// ImmutableHamTStringStringConverter 往返测试 — Read/Write 往返、null 处理、畸形 JSON。
/// </summary>
public class ImmutableHamTStringStringConverterTests {
    private static JsonSerializerOptions Options() {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new ImmutableHamTStringStringConverter());
        return opts;
    }

    private static ImmutableHamT<string, string> Build(params (string k, string v)[] entries) {
        var hamt = ImmutableHamT.Create<string, string>();
        foreach (var (k, v) in entries) hamt = hamt.Add(k, v);
        return hamt;
    }

    [Fact]
    public void RoundTrip_Empty() {
        var hamt = Build();
        var json = JsonSerializer.Serialize(hamt, Options());
        var roundtrip = JsonSerializer.Deserialize<ImmutableHamT<string, string>>(json, Options());
        roundtrip.Should().NotBeNull();
        roundtrip!.Count.Should().Be(0);
    }

    [Fact]
    public void RoundTrip_SingleEntry() {
        var hamt = Build(("a", "1"));
        var json = JsonSerializer.Serialize(hamt, Options());
        var roundtrip = JsonSerializer.Deserialize<ImmutableHamT<string, string>>(json, Options());
        roundtrip!.Count.Should().Be(1);
        roundtrip["a"].Should().Be("1");
    }

    [Fact]
    public void RoundTrip_MultipleEntries() {
        var hamt = Build(("a", "1"), ("b", "2"), ("c", "3"));
        var json = JsonSerializer.Serialize(hamt, Options());
        var roundtrip = JsonSerializer.Deserialize<ImmutableHamT<string, string>>(json, Options());
        roundtrip!.Count.Should().Be(3);
        roundtrip["a"].Should().Be("1");
        roundtrip["b"].Should().Be("2");
        roundtrip["c"].Should().Be("3");
    }

    [Fact]
    public void Write_ProducesJsonObject() {
        var hamt = Build(("a", "1"), ("b", "2"));
        var json = JsonSerializer.Serialize(hamt, Options());
        json.Should().StartWith("{");
        json.Should().EndWith("}");
        json.Should().Contain("\"a\":\"1\"");
        json.Should().Contain("\"b\":\"2\"");
    }

    [Fact]
    public void Read_Null_ReturnsNull() {
        var result = JsonSerializer.Deserialize<ImmutableHamT<string, string>>("null", Options());
        result.Should().BeNull();
    }

    [Fact]
    public void Read_NotStartObject_ThrowsJsonException() {
        var act = () => JsonSerializer.Deserialize<ImmutableHamT<string, string>>("[1,2,3]", Options());
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void RoundTrip_PreservesEntries() {
        var hamt = Build(("alpha", "x"), ("beta", "y"), ("gamma", "z"));
        var json = JsonSerializer.Serialize(hamt, Options());
        var roundtrip = JsonSerializer.Deserialize<ImmutableHamT<string, string>>(json, Options());
        roundtrip!.TryGetValue("alpha", out var v1).Should().BeTrue();
        v1.Should().Be("x");
        roundtrip.TryGetValue("beta", out var v2).Should().BeTrue();
        v2.Should().Be("y");
        roundtrip.TryGetValue("gamma", out var v3).Should().BeTrue();
        v3.Should().Be("z");
        roundtrip.TryGetValue("delta", out _).Should().BeFalse();
    }
}
