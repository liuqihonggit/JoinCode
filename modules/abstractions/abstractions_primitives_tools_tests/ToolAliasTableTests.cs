namespace CliNodes.Tests;

/// <summary>
/// 工具名等价判定 —— 全名（<c>file_read</c>）与短名（<c>read</c>）必须互为别名，
/// 高频同义词（<c>rm</c> / <c>del</c> / <c>ls</c>）也要落到同一个工具。
/// <para>这层存在的理由不是「方便」：同一组里 <c>read</c> 没有前缀、<c>file_delete</c> 有前缀，
/// 调用方每次都得先猜要不要带 <c>file_</c>，猜错得到的是「工具不存在」而不是可用提示。
/// 规则必须只有一份 —— 宿主 <c>jcc tool call</c> 与分组 exe 各自实现一份就会一条通一条不通。</para>
/// </summary>
public sealed class ToolAliasTableTests {
    private static readonly string[] FileTools = [
        "read", "write", "edit", "file_delete", "file_edit_regex", "file_insert_lines",
        "file_delete_lines", "file_batch_edit", "directory_list", "apply_patch"
    ];

    [Theory]
    [InlineData("file_read", "read")]
    [InlineData("file_write", "write")]
    [InlineData("file_edit", "edit")]
    [InlineData("delete", "file_delete")]
    [InlineData("edit_regex", "file_edit_regex")]
    [InlineData("insert_lines", "file_insert_lines")]
    [InlineData("batch_edit", "file_batch_edit")]
    public void Resolve_PrefixIsBidirectional(string requested, string expected) {
        Assert.Equal(expected, ToolAliasTable.Resolve("file", FileTools, requested));
    }

    [Theory]
    [InlineData("rm", "file_delete")]
    [InlineData("del", "file_delete")]
    [InlineData("remove", "file_delete")]
    [InlineData("erase", "file_delete")]
    [InlineData("cat", "read")]
    [InlineData("save", "write")]
    [InlineData("sed", "edit")]
    [InlineData("ls", "directory_list")]
    [InlineData("patch", "apply_patch")]
    public void Resolve_SynonymLandsOnCanonical(string requested, string expected) {
        Assert.Equal(expected, ToolAliasTable.Resolve("file", FileTools, requested));
    }

    [Fact]
    public void Resolve_ExactBeatsDerived_WhenBothExist() {
        // 组里同时有 read 与 file_read 时不得互相顶替 —— 精确命中永远优先。
        string[] both = ["read", "file_read"];

        Assert.Equal("read", ToolAliasTable.Resolve("file", both, "read"));
        Assert.Equal("file_read", ToolAliasTable.Resolve("file", both, "file_read"));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive() {
        Assert.Equal("read", ToolAliasTable.Resolve("file", FileTools, "READ"));
        Assert.Equal("file_delete", ToolAliasTable.Resolve("file", FileTools, "File_Delete"));
    }

    [Fact]
    public void Resolve_ToleratesLeadingSlash() {
        // 斜杠组的 ID 就是 /agents，Git Bash 会把前导 / 展开成路径；两种写法都得认。
        Assert.Equal("/agents", ToolAliasTable.Resolve("slash", ["/agents", "analyze"], "/agents"));
        Assert.Equal("/agents", ToolAliasTable.Resolve("slash", ["/agents", "analyze"], "agents"));
    }

    [Fact]
    public void Resolve_UnknownNameReturnsNull() {
        Assert.Null(ToolAliasTable.Resolve("file", FileTools, "permissions.mode"));
        Assert.Null(ToolAliasTable.Resolve("file", FileTools, "file_rename"));
    }

    [Fact]
    public void Resolve_AmbiguousNameReturnsNull() {
        // 同名两条（大小写不同）时不许挑一个执行 —— 挑错了就是删错文件。
        Assert.Null(ToolAliasTable.Resolve("file", ["Read", "read"], "read"));
    }

    [Fact]
    public void Resolve_SynonymsDoNotLeakAcrossGroups() {
        // rm 是 shell 的说法，不该在 git 组里指向任何东西。
        Assert.Null(ToolAliasTable.Resolve("git", ["git_status", "git_commit"], "rm"));
    }

    [Fact]
    public void Resolve_EmptyGroupListReturnsNull() {
        Assert.Null(ToolAliasTable.Resolve("file", [], "read"));
    }

    [Fact]
    public void AliasesOf_StartsWithCanonicalId() {
        var aliases = ToolAliasTable.AliasesOf("file", "file_delete").ToArray();

        Assert.Equal("file_delete", aliases[0]);
        Assert.Contains("delete", aliases);
        Assert.Contains("rm", aliases);
        Assert.Contains("del", aliases);
    }

    [Fact]
    public void AliasesOf_UnprefixedToolAlsoGetsPrefixedForm() {
        var aliases = ToolAliasTable.AliasesOf("file", "read").ToArray();

        Assert.Equal("read", aliases[0]);
        Assert.Contains("file_read", aliases);
        Assert.Contains("cat", aliases);
    }

    [Fact]
    public void AliasesOf_DoesNotRepeatCanonical() {
        Assert.Single(ToolAliasTable.AliasesOf("file", "read"), alias => alias == "read");
    }

    [Fact]
    public void FindTool_ResolvesShortAndSynonymNames() {
        var node = new CliToolDescriptor {
            Id = "file",
            Name = "jcc-tool-file",
            Summary = "file 分组",
            Tools = [.. FileTools.Select(id => new CliToolEntry { Id = id, Mode = CliExecutionMode.Local })]
        };

        Assert.Equal("read", node.FindTool("file_read")?.Id);
        Assert.Equal("file_delete", node.FindTool("rm")?.Id);
        Assert.Equal("directory_list", node.FindTool("ls")?.Id);
        Assert.Null(node.FindTool("file_rename"));
    }

    [Fact]
    public void FindTool_DeclaredAliasStillWins() {
        // node.yaml 里显式声明的 aliases（斜杠组）不能被前缀推导覆盖
        var node = new CliToolDescriptor {
            Id = "slash",
            Name = "jcc-tool-slash",
            Summary = "斜杠命令",
            Tools = [new CliToolEntry { Id = "clear", Mode = CliExecutionMode.Host, Aliases = ["reset", "cls"] }]
        };

        Assert.Equal("clear", node.FindTool("cls")?.Id);
        Assert.Equal("clear", node.FindTool("clear")?.Id);
    }
}
