namespace CliNodes.Tests;

/// <summary>
/// 基础工具 YAML 注册契约 — 锁定「首轮 tools 恒为 4 个」防 466KB 巨型请求复活。
/// 回归形态见 ToolKindDefaultGuardTests.cs:10。
/// </summary>
public sealed class BaseToolRegistryTests {
    [Fact]
    public void LoadDefault_FromEmbeddedResource_ReturnsExactlyFourBaseTools() {
        var registry = BaseToolRegistry.LoadDefault();

        Assert.Equal(4, registry.Count);
        // file_edit 取代了最初的 code_execution —— 后者在 546 个内核工具中并不存在，
        // 若进入协议 tools 字段，LLM 会拿到一个永远「工具不存在」的槽位。
        Assert.Equal(new[] { "file_read", "file_write", "file_edit", "bash" },
            registry.All.Select(tool => tool.Id).ToArray());
    }

    [Fact]
    public void LoadDefault_AllToolsAreHostImplemented_NoExeDeclared() {
        var registry = BaseToolRegistry.LoadDefault();

        Assert.All(registry.All, tool => Assert.True(tool.IsBaseTool, $"基础工具 '{tool.Id}' 不应声明 exe"));
        Assert.All(registry.All, tool => Assert.Null(tool.Exe));
    }

    [Fact]
    public void ToToolDefinitions_SchemaCarriesRequiredParametersNotPrompts() {
        var registry = BaseToolRegistry.LoadDefault();

        var definitions = registry.ToToolDefinitions();

        Assert.Equal(4, definitions.Count);
        var read = Assert.Single(definitions, d => d.Name == "FileRead");
        Assert.Equal("读取指定路径的文件内容，可按行偏移与行数限制截取", read.Description);
        Assert.Contains("file_path", read.Schema.Required);
        Assert.DoesNotContain("offset", read.Schema.Required);
        Assert.Equal("string", read.Schema.Properties["file_path"].Type);
        Assert.Equal("0", read.Schema.Properties["offset"].Default);
    }

    [Fact]
    public void Get_ById_ReturnsDescriptor() {
        var registry = BaseToolRegistry.LoadDefault();

        Assert.Equal("Bash", registry.Get("bash")?.Name);
        Assert.Null(registry.Get("not_exist"));
    }

    [Fact]
    public void FromManifest_DuplicateId_ThrowsWithClearMessage() {
        var manifest = new BaseToolManifest {
            Tools = [
                MakeTool("dup"),
                MakeTool("dup")
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => BaseToolRegistry.FromManifest(manifest));
        Assert.Contains("重复工具 id 'dup'", ex.Message);
    }

    [Fact]
    public void ParseBase_BaseToolDeclaringExe_IsRejected() {
        const string yaml = """
            version: 1
            tools:
              - id: rogue
                name: Rogue
                summary: 不应作为基础工具
                exe: rogue.exe
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => NodeManifestLoader.LoadBaseFromText(yaml, "probe.yaml"));
        Assert.Contains("不得声明 exe", ex.Message);
    }

    [Fact]
    public void ParseBase_EmptyTools_IsRejected() {
        const string yaml = """
            version: 1
            tools: []
            """;

        Assert.Throws<InvalidOperationException>(() => NodeManifestLoader.LoadBaseFromText(yaml, "empty.yaml"));
    }

    private static CliToolDescriptor MakeTool(string id) =>
        new() { Id = id, Name = id, Summary = "probe" };
}
