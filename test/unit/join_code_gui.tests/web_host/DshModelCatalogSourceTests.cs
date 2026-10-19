namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 模型目录来源测试（ADR 0143 M2-b2）：启动期从 settings.json 的供应商预设构建 DSH 模型目录，
/// 引擎 attach 后热切换成引擎真值。
/// </summary>
public sealed class DshModelCatalogSourceTests {
    private const string SettingsPath = "/test/settings.json";

    [Fact]
    public async Task Load_WithoutSettingsFile_ReturnsEmptyCatalog() {
        var source = new DshModelCatalogSource();

        await source.LoadAsync(new InMemoryFileSystem(), SettingsPath, CancellationToken.None);

        source.Current.Default.Provider.Should().BeEmpty();
        source.Current.Default.Model.Should().BeEmpty();
        source.Current.Groups.Should().BeEmpty();
        source.Current.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_ProjectsSettingsVendorsIntoCatalog() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync(SettingsPath, SettingsWithTwoVendors);
        var source = new DshModelCatalogSource();

        await source.LoadAsync(fs, SettingsPath, CancellationToken.None);

        source.Current.Default.Provider.Should().Be("deepseek");
        source.Current.Default.Model.Should().Be("deepseek-chat");
        source.Current.Groups.Should().HaveCount(2);
        source.Current.RoutableProviders.Should().BeEquivalentTo(["deepseek", "openai"]);
        var deepseek = source.Current.Groups.Single(group => group.Id == "deepseek");
        deepseek.Models.Select(model => model.Id).Should().Equal("deepseek-chat", "deepseek-reasoner");
    }

    [Fact]
    public void AttachEngine_OverridesSettingsSnapshot() {
        var source = new DshModelCatalogSource();

        source.AttachEngine("agnes", "agnes-pro", new Dictionary<string, IReadOnlyList<string>> {
            ["agnes"] = ["agnes-pro", "agnes-mini"],
        });

        source.Current.Default.Provider.Should().Be("agnes");
        source.Current.Default.Model.Should().Be("agnes-pro");
        var group = source.Current.Groups.Should().ContainSingle().Subject;
        group.Id.Should().Be("agnes");
        group.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Build_WithoutModels_KeepsCurrentProviderGroup() {
        var catalog = DshModelCatalogSource.Build("deepseek", "deepseek-chat", null);

        catalog.Default.Provider.Should().Be("deepseek");
        catalog.Default.Model.Should().Be("deepseek-chat");
        catalog.Groups.Should().ContainSingle().Which.Id.Should().Be("deepseek");
        catalog.Groups[0].Models.Should().BeEmpty();
    }

    /// <summary>两个供应商的 settings.json（vendor 预设 + current.profile 指向 deepseek）</summary>
    private const string SettingsWithTwoVendors = """
        {
          "vendor": {
            "deepseek": {
              "provider": "deepseek",
              "model": "deepseek-chat",
              "models": [{ "id": "deepseek-chat" }, { "id": "deepseek-reasoner" }]
            },
            "openai": {
              "provider": "openai",
              "model": "gpt-5",
              "models": [{ "id": "gpt-5" }]
            }
          },
          "current": { "profile": "deepseek" }
        }
        """;
}
