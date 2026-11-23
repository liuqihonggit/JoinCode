namespace Core.Tests.Permission;

/// <summary>
/// AgentPermissionManager 外部写入拾取测试。
/// <para>规则文件 {gitRoot}/.jcc/permission/rules.json 是唯一真相源（ADR 0157）。
/// 独立工具 exe（jcc-tool-system）直接改写该文件后，宿主内存态必须跟着文件走，
/// 否则 AI 通过 exe 收回的权限在宿主里仍然生效 —— 那是安全问题，不是体验问题。</para>
/// </summary>
public sealed class AgentPermissionManagerExternalWriteTests {
    private static (AgentPermissionManager Manager, InMemoryFileSystem Fs, string RulesPath) Arrange() {
        var fs = new InMemoryFileSystem();
        var root = Path.Combine(Path.GetTempPath(), "jcc-perm-" + Guid.NewGuid().ToString("N"));
        fs.CreateDirectory(Path.Combine(root, ".git"));
        fs.SetCurrentDirectory(root);
        var rulesDir = Path.Combine(root, ".jcc", "permission");
        fs.CreateDirectory(rulesDir);
        var manager = new AgentPermissionManager(persistencePipeline: new PersistencePipeline(fs), fs: fs);
        return (manager, fs, Path.Combine(rulesDir, "rules.json"));
    }

    [Fact]
    public async Task 外部进程改写规则文件后_宿主重载为该文件内容() {
        var (manager, fs, rulesPath) = Arrange();
        await using (manager) {
            await manager.AddRuleAsync(new AgentPermissionRule {
                AgentPattern = "host-agent",
                Mode = PermissionMode.Auto,
                Level = PermissionLevel.Read
            });
            fs.FileExists(rulesPath).Should().BeTrue("宿主添加规则应先落到规则文件");

            // 模拟独立 exe 直写：camelCase + 枚举数值，与宿主 PermissionJsonContext 同 schema
            await fs.WriteAllTextAsync(rulesPath, """[{"agentPattern":"exe-agent","mode":2,"level":3}]""");

            var rules = await manager.ListRulesAsync();

            rules.Should().ContainSingle("文件里只剩一条规则，内存态里的 host-agent 必须被丢掉");
            rules[0].AgentPattern.Should().Be("exe-agent");
            rules[0].Mode.Should().Be(PermissionMode.Ask, "mode:2 按 PermissionMode 声明序是 Ask");
            rules[0].Level.Should().Be(PermissionLevel.Execute, "level:3 按 PermissionLevel 声明序是 Execute");
        }
    }

    [Fact]
    public async Task 文件未变时_不清空本进程内存规则() {
        var (manager, fs, rulesPath) = Arrange();
        await using (manager) {
            await manager.AddRuleAsync(new AgentPermissionRule {
                AgentPattern = "self-agent",
                Mode = PermissionMode.Auto,
                Level = PermissionLevel.Write
            });

            var rules = await manager.ListRulesAsync();

            rules.Should().ContainSingle("自写自读不该被自己刚写下的内容清掉");
            rules[0].AgentPattern.Should().Be("self-agent");
            fs.FileExists(rulesPath).Should().BeTrue();
        }
    }

    [Fact]
    public async Task 文件被外部清空后_宿主不再保留任何规则() {
        var (manager, fs, rulesPath) = Arrange();
        await using (manager) {
            await manager.AddRuleAsync(new AgentPermissionRule {
                AgentPattern = "gone-agent",
                Mode = PermissionMode.Ask,
                Level = PermissionLevel.Read
            });

            await fs.WriteAllTextAsync(rulesPath, "[]");

            var rules = await manager.ListRulesAsync();

            rules.Should().BeEmpty("外部清空规则文件后宿主不应继续持有旧规则");
        }
    }
}
