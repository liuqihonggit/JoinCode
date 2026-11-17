namespace Host.Tests.Cli;

/// <summary>
/// 显式模块顺序契约 — 生成的 AppModuleCatalog 必须与 resources/app/modules.yaml 完全一致。
/// <para>真机顺序由 Verify-ExplicitOrder.py 启动 jcc 校验；这里锁的是生成物的数据映射，
/// 防止「YAML 改了但生成器忘读」或 profile 名写错。</para>
/// </summary>
public sealed class ExplicitModuleOrderTests {
    [Fact]
    public async Task AppModuleCatalog_CliProfile_FollowsYamlExactly() {
        var root = FindRepoRoot();
        if (root is null)
            throw new InvalidOperationException("测试必须从 jcc 仓库目录树启动");
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var yaml = await fs.ReadAllText(fs.CombinePath(root, "resources", "app", "modules.yaml"));

        var expected = ParseProfile(yaml, "cli");
        expected.Should().NotBeEmpty();

        var actual = AppModuleCatalog.Create("cli").Select(module => module.GetType().Name).ToArray();
        actual.Should().Equal(expected);
    }

    [Fact]
    public void AppModuleCatalog_UnknownProfile_ThrowsWithAvailableNames() {
        var ex = Assert.Throws<ArgumentException>(() => AppModuleCatalog.Create("no_such_profile"));
        ex.Message.Should().Contain("cli").And.Contain("gui");
    }

    private static string[] ParseProfile(string yaml, string profile) {
        var result = new List<string>();
        var matched = false;
        foreach (var raw in yaml.Split('\n')) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!line.StartsWith("- ")) {
                matched = string.Equals(line, profile + ":", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (matched) result.Add(line[2..].Trim());
        }
        return result.ToArray();
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
