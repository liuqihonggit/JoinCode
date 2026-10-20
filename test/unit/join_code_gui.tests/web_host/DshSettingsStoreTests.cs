namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 设置命名空间存储测试（ADR 0143 缺陷修复）：
/// ① 首个「预览版说明」确认写（ns=ui-settings-general / path=[welcomeNoticeVersion]）必须落库并能被 describe 读回；
/// ② 路径式 set/unset 的嵌套树构造与删除；
/// ③ expectedRevision 冲突判定；
/// ④ 未知命名空间按需创建。
/// </summary>
public sealed class DshSettingsStoreTests {
    /// <summary>被测设置存储（每个测试类实例独立，避免静态状态串味）</summary>
    private readonly DshSettingsStore _store = new();

    /// <summary>构造一元调用参数（DTO + JsonContext，与 DSH 前端 settings/mutate 请求体同形状）</summary>
    private static JsonElement MutateArgs(string ns, IReadOnlyList<DshSettingsOp> ops, int? expectedRevision = null) =>
        JsonSerializer.SerializeToElement(
            new DshSettingsMutateArgs { Ns = ns, Ops = [.. ops], ExpectedRevision = expectedRevision },
            DshJsonContext.Default.DshSettingsMutateArgs);

    /// <summary>set 操作（值以字面量给出，避免手写整段 JSON）</summary>
    private static DshSettingsOp Set(string path, string valueJson) => new() {
        Op = "set",
        Path = [.. path.Split('/')],
        Value = JsonDocument.Parse(valueJson).RootElement.Clone(),
    };

    /// <summary>unset 操作</summary>
    private static DshSettingsOp Unset(string path) => new() { Op = "unset", Path = [.. path.Split('/')] };

    /// <summary>从成功结果中取出命名空间视图的 value 树</summary>
    private static JsonElement ValueOf(DshRpcResult result) {
        result.Ok.Should().BeTrue(result.Error?.Message);
        return result.Value.GetValueOrDefault().GetProperty("value").Clone();
    }

    [Fact]
    public void Describe_AlwaysContainsWelcomeNamespace() {
        _store.Reset();
        var describe = _store.Describe();
        describe.Writable.Should().BeTrue();
        var welcome = describe.Namespaces.Single(view => view.Ns == "ui-settings-general");
        welcome.Applies.Should().Be("live");
        welcome.Revision.Should().Be(0);
        welcome.Value.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void Mutate_SetWelcomeVersion_PersistsAndDescribeReadsBack() {
        _store.Reset();
        var result = _store.Mutate(MutateArgs(
            "ui-settings-general",
            [Set("welcomeNoticeVersion", "\"2026-09-28.1\"")]));
        ValueOf(result).GetProperty("welcomeNoticeVersion").GetString().Should().Be("2026-09-28.1");

        var welcome = _store.Describe().Namespaces.Single(view => view.Ns == "ui-settings-general");
        welcome.Revision.Should().Be(1);
        welcome.Value.GetProperty("welcomeNoticeVersion").GetString().Should().Be("2026-09-28.1");
    }

    [Fact]
    public void Mutate_NestedPath_BuildsNestedObject() {
        _store.Reset();
        var result = _store.Mutate(
            MutateArgs("demo-nested", [Set("llm/retry/maxRetries", "5")]));
        var inner = ValueOf(result).GetProperty("llm").GetProperty("retry");
        inner.GetProperty("maxRetries").GetInt32().Should().Be(5);
    }

    [Fact]
    public void Mutate_Unset_RemovesFieldAndSubtree() {
        _store.Reset();
        _store.Mutate(MutateArgs("demo-unset", [Set("a/b", "1"), Set("a/c", "2")]));
        var result = _store.Mutate(MutateArgs("demo-unset", [Unset("a/b")]));
        var node = ValueOf(result).GetProperty("a");
        node.TryGetProperty("b", out _).Should().BeFalse();
        node.GetProperty("c").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Mutate_StaleExpectedRevision_FailsWithConflict() {
        _store.Reset();
        _store.Mutate(MutateArgs("demo-conflict", [Set("x", "1")]));
        var result = _store.Mutate(MutateArgs("demo-conflict", [Set("x", "2")], expectedRevision: 0));
        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error.Code.Should().Be("settings/conflict");
    }

    [Fact]
    public void Mutate_UnknownNamespace_MutateCreatesIt() {
        _store.Reset();
        _store.Describe().Namespaces.Should().NotContain(view => view.Ns == "demo-new");
        _store.Mutate(MutateArgs("demo-new", [Set("flag", "true")]));
        _store.Describe().Namespaces.Single(view => view.Ns == "demo-new")
            .Value.GetProperty("flag").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Describe_WelcomeNamespace_CarriesSchemasteryEnvelope() {
        _store.Reset();
        var welcome = _store.Describe().Namespaces.Single(view => view.Ns == "ui-settings-general");
        welcome.AutoGenerate.Should().BeFalse();
        welcome.Schema.ValueKind.Should().Be(JsonValueKind.Object);
        welcome.Schema.GetProperty("uid").GetInt32().Should().Be(4);
        var refs = welcome.Schema.GetProperty("refs");
        refs.GetProperty("4").GetProperty("type").GetString().Should().Be("object");
        refs.GetProperty("4").GetProperty("dict").GetProperty("welcomeNoticeVersion").GetInt32().Should().Be(3);
        refs.GetProperty("3").GetProperty("type").GetString().Should().Be("string");
        refs.GetProperty("3").GetProperty("meta").TryGetProperty("volatile", out _).Should().BeFalse();
    }
}
