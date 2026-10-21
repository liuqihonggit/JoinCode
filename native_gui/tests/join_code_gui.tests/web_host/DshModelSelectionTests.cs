namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 模型切换路由测试（TASK058 B2）：session/selectModel 参数契约与 llm/listProviders 目录投影。
/// 引擎分支（SetVendorAsync/SetModelAsync/SetEffortLevelAsync）由真机验收覆盖（接口成员过多无法轻量替身）。
/// </summary>
public sealed class DshModelSelectionTests {
    /// <summary>构造 session/selectModel 请求（具名参数 request 信封，与前端 sessions.selectModel 调用同形状）</summary>
    private static JsonElement SelectModelArgs(string? sessionId, string? provider, string? model, string? effort = null) =>
        JsonSerializer.SerializeToElement(
            new DshRequestEnvelope<DshSessionSelectModelArgs> {
                Request = new() {
                    SessionId = sessionId ?? "",
                    Provider = provider ?? "",
                    Model = model ?? "",
                    ReasoningEffort = effort,
                },
            }, DshJsonContext.Default.DshRequestEnvelopeDshSessionSelectModelArgs);

    /// <summary>无引擎的测试分发器（引擎分支走真机）</summary>
    private static DshRpcDispatcher NewDispatcher(DshHostServices? services = null) =>
        new(services ?? new DshHostServices(new IO.FileSystem.InMemoryFileSystem()));

    [Fact]
    public async Task SelectModel_MissingProviderOrModel_RejectsWithGuidance() {
        var dispatcher = NewDispatcher();

        var noProvider = await dispatcher.HandleAsync("session/selectModel", SelectModelArgs("s1", "", "m1"));
        noProvider.Ok.Should().BeFalse();
        noProvider.Error?.Code.Should().Be("session/rejected");

        var noModel = await dispatcher.HandleAsync("session/selectModel", SelectModelArgs("s1", "deepseek", ""));
        noModel.Error?.Code.Should().Be("session/rejected");
    }

    [Fact]
    public async Task SelectModel_EngineNotReady_ReturnsStableNotReadyError() {
        var dispatcher = NewDispatcher();

        var result = await dispatcher.HandleAsync("session/selectModel", SelectModelArgs("s1", "deepseek", "deepseek-v4-pro"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/not-ready");
    }

    [Fact]
    public async Task LlmListProviders_ProjectsModelCatalogGroups() {
        // 引擎未就绪时目录来自 settings.json（DshModelCatalogSource 启动期加载）——llm/listProviders 同源投影
        var services = new DshHostServices(new IO.FileSystem.InMemoryFileSystem());
        services.Models.AttachEngine("deepseek", "deepseek-v4-pro", new Dictionary<string, IReadOnlyList<string>> {
            ["deepseek"] = ["deepseek-v4-pro"],
            ["agnes"] = ["agnes-video-2.5-flash"],
        });
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("llm/listProviders", default(JsonElement));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var providers = result.Value.GetValueOrDefault().EnumerateArray().ToList();
        providers.Count.Should().Be(2);
        providers.Select(p => p.GetProperty("id").GetString()).Should().BeEquivalentTo(["deepseek", "agnes"]);
        providers.All(p => !string.IsNullOrEmpty(p.GetProperty("name").GetString())).Should().BeTrue();
    }

    [Fact]
    public async Task LlmListProviders_WithoutCatalog_ReturnsEmptyArrayStableOk() {
        var dispatcher = NewDispatcher();

        var result = await dispatcher.HandleAsync("llm/listProviders", default(JsonElement));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetArrayLength().Should().Be(0);
    }
}
