namespace CliNodes.Tests;

/// <summary>
/// 工具参数名归一化契约 — 宿主路径（中间件）与 exe 路径必须给出同一个答案。
/// <para>不一致的后果：同一个 <c>--filePath</c>，host 路径被自动修复、local 路径被直接拒绝，
/// 调用方（尤其是模型）无从判断该用哪种写法，只能逐个试错。
/// 真机证据见 docs/implemented/task/TASK087-参数名归一化两条路径统一.md。</para>
/// </summary>
public sealed class ToolParameterNameRepairerTests {
    private const string HelpText = """
        jcc-tool-file — file 工具分组

        工具列表（说明复用自 [McpTool] 特性）:
          read                              读取文件内容

        单个工具的参数说明:

          read — 读取文件内容
            --file_path                    string    必填   文件路径
            --encoding                     string    可选   编码
        """;

    [Fact]
    public void RepairNames_ConvertsCamelCaseToDeclaredSnakeCase() {
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["filePath"] = "a.cs" }, ["file_path", "encoding"]);

        Assert.Equal("a.cs", repaired["file_path"]);
        Assert.False(repaired.ContainsKey("filePath"));
    }

    [Fact]
    public void RepairNames_ConvertsSnakeCaseToDeclaredCamelCase() {
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["file_path"] = "a.cs" }, ["filePath"]);

        Assert.Equal("a.cs", repaired["filePath"]);
    }

    [Fact]
    public void RepairNames_KeepsUnknownNameUntouched() {
        // 解析不出规范名就保持原样 —— 由工具自己报「缺少必填参数」并给正确写法，
        // 在这里瞎猜会把参数映射到错的参数上。
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["mystery"] = "1" }, ["file_path"]);

        Assert.Equal("1", repaired["mystery"]);
    }

    [Fact]
    public void RepairNames_MatchesCaseInsensitively() {
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["FILE_PATH"] = "a.cs" }, ["file_path"]);

        Assert.Equal("a.cs", repaired["file_path"]);
    }

    [Fact]
    public void RepairNames_LeavesNamesAloneWhenNoSchemaKnown() {
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["filePath"] = "a.cs" }, []);

        Assert.Equal("a.cs", repaired["filePath"]);
    }

    [Fact]
    public void RepairParameterNames_NormalizesAliasAgainstSchema() {
        var schema = new ToolSchema {
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["file_path"] = new() { Type = "string" }
            }
        };
        var arguments = new Dictionary<string, JsonElement> {
            ["filePath"] = JsonSerializer.SerializeToElement("a.cs")
        };

        var (repaired, modified, _) = ToolParameterNameRepairer.RepairParameterNames(arguments, schema);

        Assert.True(modified);
        Assert.Equal("a.cs", repaired["file_path"].GetString());
    }

    [Fact]
    public void RepairParameterNames_SubjectAliasMapsToTaskCreateTitle() {
        var schema = new ToolSchema {
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["title"] = new() { Type = "string" }
            }
        };
        var arguments = new Dictionary<string, JsonElement> {
            ["subject"] = JsonSerializer.SerializeToElement("修复登录流程")
        };

        var (repaired, modified, _) = ToolParameterNameRepairer.RepairParameterNames(arguments, schema);

        Assert.True(modified);
        Assert.Equal("修复登录流程", repaired["title"].GetString());
        Assert.False(repaired.ContainsKey("subject"));
    }

    [Fact]
    public void RepairNames_SubjectAliasDoesNotHijackToolsDeclaringSubject() {
        // gh_pr_merge 等工具自身声明 subject；直接匹配优先，别名不得改写为 title。
        var repaired = ToolParameterNameRepairer.RepairNames(
            new Dictionary<string, string> { ["subject"] = "合并标题" }, ["subject", "body"]);

        Assert.Equal("合并标题", repaired["subject"]);
        Assert.False(repaired.ContainsKey("title"));
    }

    [Fact]
    public void DeclaredParameterNames_ReadsNamesFromToolDetailSection() {
        var names = CliNodeRunner.DeclaredParameterNames(HelpText, "read");

        Assert.Equal(["file_path", "encoding"], names);
    }

    [Fact]
    public void DeclaredParameterNames_ReturnsEmptyForUnknownTool() {
        // 解析不到就返回空集合（不伪造参数名）—— 宁可让工具报缺参。
        Assert.Empty(CliNodeRunner.DeclaredParameterNames(HelpText, "not_a_tool"));
    }

    [Fact]
    public void NormalizeOptionNames_MapsOptionToDeclaredParameter() {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["filePath"] = "a.cs" };

        var normalized = CliNodeRunner.NormalizeOptionNames(options, HelpText, "read");

        Assert.Equal("a.cs", normalized["file_path"]);
    }
}
