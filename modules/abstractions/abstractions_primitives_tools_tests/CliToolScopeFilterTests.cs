namespace CliNodes.Tests;

/// <summary>
/// 发现侧裁剪 — tree/find/help 必须与执行门禁消费同一工具域（ADR 0168 阶段 4）。
/// </summary>
public sealed class CliToolScopeFilterTests {
    [Fact]
    public void Apply_NullSnapshot_ReturnsInputUnchanged() {
        var nodes = new[] { Group("file", "read", "write") };

        Assert.Same(nodes, CliToolScopeFilter.Apply(nodes, null));
    }

    [Fact]
    public void Apply_AllowedNarrowsToolsAndDropsEmptyGroups() {
        var nodes = new[] {
            Group("file", "read", "edit", "file_edit_regex", "write"),
            Group("git", "commit", "push")
        };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot(["Read", "Edit", "FileEditRegex"], []));

        var file = Assert.Single(filtered);
        Assert.Equal("file", file.Id);
        Assert.Equal(["read", "edit", "file_edit_regex"], file.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public void Apply_DeniedWinsOverAllowed() {
        var nodes = new[] { Group("file", "read", "file_delete") };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot(["read", "file_delete"], ["FileDelete"]));

        var file = Assert.Single(filtered);
        Assert.Equal(["read"], file.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public void Apply_DeniedWildcard_HidesEverything() {
        var nodes = new[] { Group("file", "read"), Group("git", "commit") };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot([], ["*"]));

        Assert.Empty(filtered);
    }

    [Fact]
    public void Apply_EmptyAllowed_KeepsAllExceptDenied() {
        var nodes = new[] { Group("file", "read", "write") };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot([], ["write"]));

        var file = Assert.Single(filtered);
        Assert.Equal(["read"], file.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public void Apply_AliasMatchesAllowed() {
        var entry = new CliToolEntry {
            Id = "/clear",
            Mode = CliExecutionMode.Host,
            Aliases = ["reset"]
        };
        var nodes = new[] {
            new CliToolDescriptor { Id = "slash", Name = "slash", Summary = "slash", Exe = "jcc-tool-slash.exe", Tools = [entry] }
        };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot(["/reset"], []));

        var slash = Assert.Single(filtered);
        Assert.Single(slash.Tools);
    }

    [Fact]
    public void Apply_StandaloneDescriptorWithoutTools_UsesNodeId() {
        var nodes = new[] {
            new CliToolDescriptor { Id = "ripgrep", Name = "ripgrep", Summary = "rg" },
            new CliToolDescriptor { Id = "notebook", Name = "notebook", Summary = "nb" }
        };

        var filtered = CliToolScopeFilter.Apply(nodes, Snapshot(["ripgrep"], []));

        var node = Assert.Single(filtered);
        Assert.Equal("ripgrep", node.Id);
    }

    private static CliToolScopeSnapshot Snapshot(string[] allowed, string[] denied) => new() {
        AllowedTools = allowed,
        DeniedTools = denied
    };

    private static CliToolDescriptor Group(string id, params string[] tools) => new() {
        Id = id,
        Name = id,
        Summary = id,
        Exe = $"jcc-tool-{id}.exe",
        Tools = tools
            .Select(tool => new CliToolEntry { Id = tool, Mode = CliExecutionMode.Local })
            .ToArray()
    };
}
