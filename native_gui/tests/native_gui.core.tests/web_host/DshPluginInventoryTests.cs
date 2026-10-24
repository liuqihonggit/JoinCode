namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 插件清单（TASK066 C7）：宿主没有可管理的 profile，前端插件页必须收到
/// <c>managementAvailable=false</c> 并显示「本部署没有可管理的 profile」，
/// 而不是因为方法未实现而报「网络问题」。
/// </summary>
public sealed class DshPluginInventoryTests {
    [Fact]
    public async Task PluginInventory_List_ReportsNoManagementProfile() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("pluginInventory/list", JsonSerializer.SerializeToElement(new { }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("managementAvailable").GetBoolean().Should().BeFalse();
        value.GetProperty("entries").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PluginManager_ReadPathsReturnEmptyShapesWhenManagementUnavailable() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);
        var empty = JsonSerializer.SerializeToElement(new { });

        var bundles = await dispatcher.HandleAsync("pluginManager/listBundles", empty);
        bundles.Ok.Should().BeTrue(bundles.Error?.Message);
        bundles.Value.GetValueOrDefault().GetArrayLength().Should().Be(0);

        var plugins = await dispatcher.HandleAsync("pluginManager/listPlugins", empty);
        plugins.Ok.Should().BeTrue(plugins.Error?.Message);
        plugins.Value.GetValueOrDefault().GetArrayLength().Should().Be(0);

        var registries = await dispatcher.HandleAsync("pluginManager/registries", empty);
        registries.Ok.Should().BeTrue(registries.Error?.Message);
        var registryView = registries.Value.GetValueOrDefault();
        registryView.GetProperty("registry").ValueKind.Should().Be(JsonValueKind.Null);
        registryView.GetProperty("fallbackRegistries").GetArrayLength().Should().Be(0);
        registryView.GetProperty("resolved").ValueKind.Should().Be(JsonValueKind.Null);

        var exemptions = await dispatcher.HandleAsync("pluginManager/listVersionExemptions", empty);
        exemptions.Ok.Should().BeTrue(exemptions.Error?.Message);
        var exemptionView = exemptions.Value.GetValueOrDefault();
        exemptionView.GetProperty("exemptions").EnumerateObject().Should().BeEmpty();
        exemptionView.GetProperty("warnings").GetArrayLength().Should().Be(0);

        var fastest = await dispatcher.HandleAsync("pluginRegistryProbe/fastest", empty);
        fastest.Ok.Should().BeTrue(fastest.Error?.Message);
        fastest.Value.GetValueOrDefault().ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PluginManager_WritesRejectWithManagementGuidance() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var install = await dispatcher.HandleAsync("pluginManager/installBundle",
            JsonSerializer.SerializeToElement(new { spec = "x", registry = (string?)null }));

        install.Ok.Should().BeFalse();
        install.Error?.Code.Should().Be("plugin-manager/rejected");
        install.Error?.Message.Should().Contain("插件管理");
    }

    [Fact]
    public async Task CordisPanelActionsReportPluginMissingInsteadOfUnknownMethod() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);
        var stopArgs = JsonSerializer.SerializeToElement(new { sessionId = "s", pluginId = "p" });

        var stop = await dispatcher.HandleAsync("dynamicCordisRunner/stopFromPanel", stopArgs);
        stop.Ok.Should().BeTrue(stop.Error?.Message);
        var stopValue = stop.Value.GetValueOrDefault();
        stopValue.GetProperty("ok").GetBoolean().Should().BeFalse();
        stopValue.GetProperty("reason").GetString().Should().Be("plugin-missing");
        stopValue.GetProperty("message").GetString().Should().Contain("Cordis");

        var undefine = await dispatcher.HandleAsync("dynamicCordisRunner/undefineFromPanel", stopArgs);
        undefine.Ok.Should().BeTrue(undefine.Error?.Message);
        undefine.Value.GetValueOrDefault().GetProperty("reason").GetString().Should().Be("plugin-missing");
    }

    [Fact]
    public async Task OfficeToPdf_ReportsServiceUnavailableInsteadOfUnknownMethod() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var generation = await dispatcher.HandleAsync("officeToPdf/generation", JsonSerializer.SerializeToElement(new { }));
        generation.Ok.Should().BeTrue(generation.Error?.Message);
        generation.Value.GetValueOrDefault().ValueKind.Should().Be(JsonValueKind.String);

        var render = await dispatcher.HandleAsync("officeToPdf/render",
            JsonSerializer.SerializeToElement(new { workspaceFileScopeId = "s", path = "a.docx", priority = "foreground" }));
        render.Ok.Should().BeFalse();
        render.Error?.Code.Should().Be("gateway/service-unavailable");
        render.Error?.Message.Should().Contain("Office");
    }

    [Fact]
    public async Task AccountMethodsRejectWithUnsupportedGuidance() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var state = await dispatcher.HandleAsync("account/getState", JsonSerializer.SerializeToElement(new { }));

        state.Ok.Should().BeFalse();
        state.Error?.Code.Should().Be("account/unsupported");
        state.Error?.Message.Should().Contain("账号");
    }
}
