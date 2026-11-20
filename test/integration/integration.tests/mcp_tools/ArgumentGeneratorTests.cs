namespace Integration.Tests.McpTools;

/// <summary>
/// ArgumentGenerator 语义构造单测 — 验证按 tempDir 构造的绝对路径、schema default/enum、
/// 以及每个工具构造的参数能通过自身必填校验（TASK072 A 项，清单 :80）。
/// </summary>
public sealed class ArgumentGeneratorTests {
    private static ToolInfo ToolWith(string name, params (string Name, string Type)[] required) =>
        new() {
            Name = name,
            InputSchema = new ToolSchema {
                Required = required.Select(r => r.Name).ToList(),
                Properties = required.ToDictionary(
                    r => r.Name,
                    r => new ToolSchemaProperty { Type = r.Type }),
            },
        };

    [Fact]
    public void NoTempDir_FilePath_KeepsRelativeDefault() {
        var tool = ToolWith("file_read", ("file_path", "string"));

        var args = ArgumentGenerator.GenerateDefaultArguments(tool);

        args["file_path"].GetString().Should().Be("test.txt", "无 tempDir 时保持相对路径默认，兼容既有测试");
    }

    [Fact]
    public void WithTempDir_FilePath_UsesAbsolutePathInPatrolDir() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"patrol-{Guid.NewGuid():N}");
        var tool = ToolWith("file_write", ("file_path", "string"), ("content", "string"));

        var args = ArgumentGenerator.GenerateDefaultArguments(tool, tempDir);

        var path = args["file_path"].GetString();
        Path.IsPathRooted(path).Should().BeTrue("file_path 应为绝对路径，触发 file 工具核心逻辑");
        path.Should().StartWith(tempDir, "绝对路径应落在独占 patrol 目录内");
        Path.GetFileName(path).Should().Be("patrol-test.txt");
    }

    [Fact]
    public void WithTempDir_DirectoryPath_UsesTempDirItself() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"patrol-{Guid.NewGuid():N}");
        var tool = ToolWith("directory_list", ("directory_path", "string"));

        var args = ArgumentGenerator.GenerateDefaultArguments(tool, tempDir);

        args["directory_path"].GetString().Should().Be(tempDir);
    }

    [Fact]
    public void SchemaDefault_IsHonored() {
        var tool = new ToolInfo {
            Name = "flag_tool",
            InputSchema = new ToolSchema {
                Required = ["mode"],
                Properties = new() {
                    ["mode"] = new ToolSchemaProperty { Type = "string", Default = "\"fast\"" },
                },
            },
        };

        var args = ArgumentGenerator.GenerateDefaultArguments(tool, null);

        args["mode"].GetString().Should().Be("fast", "schema default 优先于参数名启发式");
    }

    [Fact]
    public void EnumParam_PicksFirstValue() {
        var tool = new ToolInfo {
            Name = "enum_tool",
            InputSchema = new ToolSchema {
                Required = ["level"],
                Properties = new() {
                    ["level"] = new ToolSchemaProperty { Type = "string", Enum = ["high", "low"] },
                },
            },
        };

        var args = ArgumentGenerator.GenerateDefaultArguments(tool, null);

        args["level"].GetString().Should().Be("high", "enum 参数取首值确保通过工具自身校验");
    }

    [Fact]
    public void EveryRequiredParam_IsPresent_AndPassesPresenceCheck() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"patrol-{Guid.NewGuid():N}");
        ToolInfo[] tools = [
            ToolWith("file_read", ("file_path", "string")),
            ToolWith("file_write", ("file_path", "string"), ("content", "string")),
            ToolWith("sleep", ("duration_seconds", "integer")),
            ToolWith("grep", ("pattern", "string"), ("path", "string")),
            ToolWith("run", ("command", "string"), ("confirm", "boolean")),
        ];

        foreach (var tool in tools) {
            var args = ArgumentGenerator.GenerateDefaultArguments(tool, tempDir);
            var required = tool.InputSchema?.Required;
            if (required is null) continue;
            foreach (var req in required) {
                args.Should().ContainKey(req, $"{tool.Name} 必需参数 {req} 应被构造");
                args[req].ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Undefined);
            }
        }
    }
}
