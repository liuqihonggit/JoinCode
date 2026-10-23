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
}
