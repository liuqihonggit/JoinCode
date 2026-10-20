namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 设置持久化测试（TASK058 B3）：宿主设置从进程内内存退役为
/// <c>~/.jcc/dsh-settings.json</c> 真实落盘（RelaxedJsonSerializer + JsonContext DTO，ADR 0042/0133）；
/// permission 命名空间接入引擎 <see cref="PermissionMode"/> 枚举词汇（唯一数据源），
/// defaultPreset 写入触发引擎路由回调。
/// </summary>
public sealed class DshSettingsStorePersistenceTests {
    /// <summary>构造绑定文件系统的设置存储</summary>
    private static DshSettingsStore NewStore(IFileSystem fs, string path = "mem/dsh-settings.json") =>
        new(fs, path);

    /// <summary>构造 mutate 参数（DTO + JsonContext）</summary>
    private static JsonElement MutateArgs(string ns, string path, string valueJson, int? expectedRevision = null) =>
        JsonSerializer.SerializeToElement(
            new DshSettingsMutateArgs {
                Ns = ns,
                Ops = [new DshSettingsOp { Op = "set", Path = [.. path.Split('/')], Value = JsonDocument.Parse(valueJson).RootElement.Clone() }],
                ExpectedRevision = expectedRevision,
            }, DshJsonContext.Default.DshSettingsMutateArgs);

    /// <summary>从成功结果中取出 value 树</summary>
    private static JsonElement ValueOf(DshRpcResult result) {
        result.Ok.Should().BeTrue(result.Error?.Message);
        return result.Value.GetValueOrDefault().GetProperty("value").Clone();
    }

    [Fact]
    public async Task Mutate_PersistsAcrossRestart_SameFileSystem() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var first = NewStore(fs);
        first.Mutate(MutateArgs("ui-settings-general", "welcomeNoticeVersion", "\"2026-09-28.1\""));

        // 重启：新实例从同一文件恢复（进程内内存假数据退役）
        var second = NewStore(fs);
        await second.InitializeAsync();
        var view = second.Describe().Namespaces.Single(ns => ns.Ns == "ui-settings-general");
        view.Value.GetProperty("welcomeNoticeVersion").GetString().Should().Be("2026-09-28.1");
    }

    [Fact]
    public void Describe_AlwaysContainsPermissionNamespace_WithEngineDefaultPreset() {
        var store = NewStore(new IO.FileSystem.InMemoryFileSystem());

        var view = store.Describe().Namespaces.SingleOrDefault(ns => ns.Ns == "permission")
            ?? throw new Xunit.Sdk.XunitException("permission 命名空间必须出现在 describe（前端权限预设行依赖）");

        view.Value.GetProperty("defaultPreset").GetString().Should().Be("auto", "引擎 PermissionMode 枚举默认值（Auto → auto）");
        view.Schema.ValueKind.Should().Be(JsonValueKind.Object, "前端 configForms 需要 schema 信封（ADR 0143 M1.5 教训）");
    }

    [Fact]
    public async Task Mutate_PermissionDefaultPreset_PersistsAndRaisesEngineRoute() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var store = NewStore(fs);
        var routed = new List<string>();
        store.PermissionPresetChanged += preset => routed.Add(preset);

        var result = store.Mutate(MutateArgs("permission", "defaultPreset", "\"ask\""));

        ValueOf(result).GetProperty("defaultPreset").GetString().Should().Be("ask");
        routed.Should().BeEquivalentTo(["ask"], "defaultPreset 写入必须触发引擎 SetPermissionModeAsync 路由回调");
        // 持久化读回
        var reopened = NewStore(fs);
        await reopened.InitializeAsync();
        reopened.Describe().Namespaces.Single(ns => ns.Ns == "permission")
            .Value.GetProperty("defaultPreset").GetString().Should().Be("ask");
    }

    [Fact]
    public void Mutate_PermissionUnknownPreset_RejectedFailClosed() {
        var store = NewStore(new IO.FileSystem.InMemoryFileSystem());

        // 值不在引擎 PermissionMode 枚举词汇内 → 拒绝写（防脏值落盘，fail-closed）
        var result = store.Mutate(MutateArgs("permission", "defaultPreset", "\"danger-full-access\""));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/rejected");
        result.Error?.Message.Should().Contain("plan/auto/ask/bypass/unattended");
    }

    [Fact]
    public async Task Mutate_ConcurrentWrite_PersistsBothNamespaces() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        var store = NewStore(fs);

        store.Mutate(MutateArgs("ui-settings-general", "welcomeNoticeVersion", "\"v1\""));
        store.Mutate(MutateArgs("permission", "defaultPreset", "\"plan\""));

        var reopened = NewStore(fs);
        await reopened.InitializeAsync();
        var describe = reopened.Describe();
        describe.Namespaces.Single(ns => ns.Ns == "ui-settings-general").Value.GetProperty("welcomeNoticeVersion").GetString().Should().Be("v1");
        describe.Namespaces.Single(ns => ns.Ns == "permission").Value.GetProperty("defaultPreset").GetString().Should().Be("plan");
    }
}
