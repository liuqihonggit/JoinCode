namespace Host.Tests.Cli;

/// <summary>
/// 诚实清单数据一致性守卫 — 模型切换时打印的清单与 TASK083 文档必须同步。
/// <para>本测试只锁数据形状，不替代真机验证：真正的触发点是 <c>/model</c>，
/// 必须在构建后的 jcc 上确认切换后会打印「诚实提示」并等待用户决定。</para>
/// </summary>
public sealed class HonestyChecklistTests {
    [Fact]
    public async Task Checklist_YamlAndTaskDoc_ShouldAgreeOnCheckboxes() {
        var root = FindRepoRoot();
        if (root is null)
            throw new InvalidOperationException("测试必须从 jcc 仓库目录树启动");
        var fs = IO.FileSystem.FileSystemFactory.Create();

        var yaml = await fs.ReadAllText(fs.CombinePath(root, "resources", "honesty", "checklist.yaml"));
        var doc = await fs.ReadAllText(fs.CombinePath(root, "docs", "implemented", "task",
            "TASK083-诚实模块与目标驱动.md"));

        var ids = Regex.Matches(yaml, @"^\s*- id:\s*(\S+)", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value).ToArray();
        ids.Should().NotBeEmpty();
        ids.Should().OnlyHaveUniqueItems();
        Regex.Matches(yaml, @"^\s+status:\s*(done|pending)\s*$", RegexOptions.Multiline)
            .Count.Should().Be(ids.Length, "每个条目都要显式声明 done 或 pending");

        var doneInYaml = Regex.Matches(yaml, @"^\s+status:\s*done\s*$", RegexOptions.Multiline).Count;
        var doneInDoc = Regex.Matches(doc, @"^- \[x\]", RegexOptions.Multiline).Count;
        var pendingInDoc = Regex.Matches(doc, @"^- \[ \]", RegexOptions.Multiline).Count;

        doneInDoc.Should().Be(doneInYaml, "文档打勾数必须与清单 done 数一致");
        pendingInDoc.Should().Be(ids.Length - doneInYaml, "文档未完成数必须与清单 pending 数一致");
    }

    private static string? FindRepoRoot() {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            if (fs.FileExists(fs.CombinePath(dir.FullName, "resources", "honesty", "checklist.yaml")))
                return dir.FullName;
        }
        return null;
    }
}
