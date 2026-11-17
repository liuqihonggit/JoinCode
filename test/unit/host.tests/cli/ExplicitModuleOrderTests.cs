namespace Host.Tests.Cli;

/// <summary>
/// 显式模块顺序契约 — 生成的模块目录必须与 resources/app/modules.yaml 完全一致。
/// <para>真机顺序由 Verify-ExplicitOrder.py 启动 jcc 校验；这里锁的是生成物的数据映射，
/// 防止「YAML 改了但生成器忘读」或 profile 名写错。</para>
/// <para>CLI 与 GUI 各由自己的程序集生成一份目录（<c>CliModuleCatalog</c> / <c>GuiModuleCatalog</c>），
/// 两边都被本测试覆盖 —— gui 那条路径此前从未被真机验证过。</para>
/// </summary>
public sealed class ExplicitModuleOrderTests {
    [Fact]
    public async Task CliModuleCatalog_FollowsYamlExactly() {
        var (assembly, expected) = await LoadProfileAsync("cli");

        assembly.Should().Be("jcc", "cli profile 归 CLI 程序集（AssemblyName=jcc）");
        expected.Should().NotBeEmpty();

        var actual = CliModuleCatalog.Create().Select(module => module.GetType().Name).ToArray();
        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task GuiModuleCatalog_FollowsYamlExactly() {
        var (assembly, expected) = await LoadProfileAsync("gui");

        assembly.Should().Be("JoinCode.Engine", "gui profile 归引擎程序集（CLI 与 native_gui / native_tui 共用这条装配路径）");
        expected.Should().NotBeEmpty();

        var actual = GuiModuleCatalog.Create().Select(module => module.GetType().Name).ToArray();
        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task CliAndGuiProfiles_AreDistinctAssemblies() {
        var (cliAssembly, cliModules) = await LoadProfileAsync("cli");
        var (guiAssembly, guiModules) = await LoadProfileAsync("gui");

        cliAssembly.Should().NotBe(guiAssembly, "两套装配分属不同程序集 —— 否则类型名会撞（CS0436）");
        cliModules.Intersect(guiModules).Should().Contain("CoreModule",
            "核心模块两边都要有：CLI 在引擎之上再加自己的模块");
        cliModules.Should().Contain("CliModule").And.Contain("PipeModule");
        guiModules.Should().Contain("SlashCommandModule").And.NotContain("CliModule");
    }

    private static async Task<(string Assembly, string[] Modules)> LoadProfileAsync(string profile) {
        var root = FindRepoRoot();
        if (root is null)
            throw new InvalidOperationException("测试必须从 jcc 仓库目录树启动");
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var yaml = await fs.ReadAllText(fs.CombinePath(root, "resources", "app", "modules.yaml"));
        return ParseProfile(yaml, profile);
    }

    /// <summary>解析 profiles: → &lt;profile&gt;: → assembly: / modules: → - 名称。</summary>
    private static (string Assembly, string[] Modules) ParseProfile(string yaml, string profile) {
        var modules = new List<string>();
        var resultAssembly = string.Empty;
        string? current = null;
        var inModules = false;

        foreach (var raw in yaml.Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (!line.StartsWith("- ", StringComparison.Ordinal)) {
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (key == "assembly") {
                    // 只认目标 profile 的归属程序集 —— 遍历会经过其它 profile，它们的值不能覆盖
                    resultAssembly = current == profile ? value : resultAssembly;
                    continue;
                }
                if (key == "modules") {
                    inModules = true;
                    continue;
                }
                current = key;
                inModules = false;
                continue;
            }

            if (current == profile && inModules) modules.Add(line[2..].Trim());
        }

        return (resultAssembly, modules.ToArray());
    }

    private static string? FindRepoRoot() {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (fs.FileExists(fs.CombinePath(dir.FullName, "resources", "app", "modules.yaml")))
                return dir.FullName;
        }
        return null;
    }
}
