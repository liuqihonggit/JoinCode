namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 通用设置页「打开配置文件」：<c>settings/describe.hasDocument</c> 决定入口是否渲染，
/// <c>settings/openSettingsDocument</c> 打开宿主持有的 <c>dsh-settings.json</c>。
/// </summary>
public sealed class DshSettingsDocumentTests {
    private const string AppDataFolder = "C:/test/.jcc";
    private static readonly string SettingsPath = Path.Combine(AppDataFolder, "dsh-settings.json");

    private static DshHostServices CreateServices(IFileSystem fs) =>
        new(
            fs,
            "/test/dsh-workspaces.json",
            "/test/dsh_journal",
            appDataPaths: AppDataPaths.CreateForTest(appDataFolder: AppDataFolder));

    private static TValue ReadValue<TValue>(DshRpcResult result, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> typeInfo) {
        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.HasValue.Should().BeTrue("成功结果必须带 value");
        return JsonSerializer.Deserialize(result.Value.GetValueOrDefault(), typeInfo)
            ?? throw new InvalidOperationException($"无法反序列化 {typeof(TValue).Name}");
    }

    [Fact]
    public async Task Describe_ReportsDocumentAndOpenPersistsThenLaunchesIt() {
        await using var fs = new InMemoryFileSystem();
        var services = CreateServices(fs);
        await services.Settings.InitializeAsync(CancellationToken.None);
        var dispatcher = new DshRpcDispatcher(services);
        string? openedPath = null;
        services.Settings.OpenFile = path => {
            openedPath = path;
            return (true, null);
        };

        var describe = await dispatcher.HandleAsync("settings/describe", JsonSerializer.SerializeToElement(new { }));
        var view = ReadValue(describe, DshJsonContext.Default.DshSettingsDescribe);
        view.HasDocument.Should().BeTrue("本地宿主始终持有 dsh-settings.json 文档");

        var open = await dispatcher.HandleAsync("settings/openSettingsDocument", JsonSerializer.SerializeToElement(new { }));
        var value = ReadValue(open, DshJsonContext.Default.DshSettingsDocumentOpenValue);
        value.Opened.Should().BeTrue();
        openedPath.Should().Be(SettingsPath);
        (await fs.ReadAllTextAsync(SettingsPath)).Should().NotBeNullOrEmpty("打开前先落盘，编辑器不应看到空文件");
    }

    [Fact]
    public async Task Open_LaunchFailureReturnsActionableGuidance() {
        await using var fs = new InMemoryFileSystem();
        var services = CreateServices(fs);
        await services.Settings.InitializeAsync(CancellationToken.None);
        var dispatcher = new DshRpcDispatcher(services);
        services.Settings.OpenFile = _ => (false, "没有可用的默认程序");

        var open = await dispatcher.HandleAsync("settings/openSettingsDocument", JsonSerializer.SerializeToElement(new { }));

        open.Ok.Should().BeFalse();
        open.Error?.Code.Should().Be("settings/rejected");
        open.Error?.Message.Should().Contain("没有可用的默认程序");
        open.Error?.Message.Should().Contain(SettingsPath, "引导必须指出要打开的文件位置");
    }
}
