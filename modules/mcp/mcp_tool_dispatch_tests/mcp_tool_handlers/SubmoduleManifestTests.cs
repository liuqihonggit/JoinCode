namespace Sync.Tests.ToolHandlers;

/// <summary>
/// SubmoduleManifest .gitmodules 解析器单元测试
/// </summary>
public sealed class SubmoduleManifestTests {
    [Fact]
    public void Parse_ValidGitmodules_ReturnsAllEntries() {
        var content = """
[submodule "libs/Editor"]
	path = libs/Editor
	url = git@github.com:liuqihonggit/Editor.git
	branch = develop
[submodule "libs/Dock"]
	path = libs/Dock
	url = git@github.com:liuqihonggit/Dock.git
	branch = fix/aot-compiled-bindings
""";
        var entries = SubmoduleManifest.Parse(content);

        entries.Should().HaveCount(2);
        entries[0].Should().Be(new SubmoduleEntry("libs/Editor", "libs/Editor", "git@github.com:liuqihonggit/Editor.git", "develop"));
        entries[1].Should().Be(new SubmoduleEntry("libs/Dock", "libs/Dock", "git@github.com:liuqihonggit/Dock.git", "fix/aot-compiled-bindings"));
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyList() {
        SubmoduleManifest.Parse("").Should().BeEmpty();
        SubmoduleManifest.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoBranch_BranchIsNull() {
        var content = """
[submodule "libs/Editor"]
	path = libs/Editor
	url = git@github.com:liuqihonggit/Editor.git
""";
        var entries = SubmoduleManifest.Parse(content);

        entries.Should().HaveCount(1);
        entries[0].Branch.Should().BeNull();
    }

    [Fact]
    public void Parse_CommentLines_Ignored() {
        var content = """
# This is a comment
[submodule "libs/Editor"]
	; another comment
	path = libs/Editor
	url = git@github.com:liuqihonggit/Editor.git
""";
        var entries = SubmoduleManifest.Parse(content);

        entries.Should().HaveCount(1);
        entries[0].Name.Should().Be("libs/Editor");
    }

    [Fact]
    public void Parse_MissingPath_DefaultsToName() {
        var content = """
[submodule "libs/Editor"]
	url = git@github.com:liuqihonggit/Editor.git
""";
        var entries = SubmoduleManifest.Parse(content);

        entries.Should().HaveCount(1);
        entries[0].Path.Should().Be("libs/Editor");
    }

    [Fact]
    public void Parse_RemoteSection_Ignored() {
        // .gitmodules 可能含 [remote "submodule.xxx"] 段,不应当作子模块
        var content = """
[submodule "libs/Editor"]
	path = libs/Editor
	url = git@github.com:liuqihonggit/Editor.git
[remote "submodule.libs/Editor"]
	url = git@gitee.com:JJbox/Editor.git
""";
        var entries = SubmoduleManifest.Parse(content);

        entries.Should().HaveCount(1);
        entries[0].Name.Should().Be("libs/Editor");
    }
}
