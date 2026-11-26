namespace CliNodes.Tests;

/// <summary>
/// CLI 工具权限策略 YAML 的解析与拒绝语义 — ADR 0168 参数权限单一来源。
/// <para>这些断言先于运行时与授权 broker 存在：策略若能被静默解析成空权限，
/// 后面的集中鉴权只会稳定地放行，因此把 schema 拒绝行为锁在契约层。</para>
/// </summary>
public sealed class CliToolPolicyTests {
    private const string ValidPolicy = """
        schema_version: 1
        policy_id: cli.file
        policy_version: 3
        executables:
          - jcc-tool-file.exe
        tools:
          read:
            effect: allow
            parameters:
              file_path:
                effect: allow
                capabilities: [workspace_read]
              offset:
                effect: allow
          write:
            effect: ask
            parameters:
              file_path:
                effect: allow
                capabilities: [workspace_write]
              content:
                effect: allow
                capabilities: [workspace_write]
          goal_get:
            effect: allow
            parameters: []
        """;

    [Fact]
    public void Parse_ValidPolicy_PreservesToolsParametersAndCapabilities() {
        var manifest = CliToolPolicyLoader.Parse(ValidPolicy, "test.yaml");

        Assert.Equal("1", manifest.SchemaVersion);
        Assert.Equal("cli.file", manifest.PolicyId);
        Assert.Equal(3, manifest.PolicyVersion);
        Assert.Equal(["jcc-tool-file.exe"], manifest.Executables);
        Assert.Equal(3, manifest.Tools.Count);

        var read = Assert.Single(manifest.Tools, tool => tool.ToolId == "read");
        Assert.Equal(CliPolicyEffect.Allow, read.Effect);
        var path = Assert.Single(read.Parameters, parameter => parameter.Name == "file_path");
        Assert.Equal([CliPolicyCapability.WorkspaceRead], path.Capabilities);

        var write = Assert.Single(manifest.Tools, tool => tool.ToolId == "write");
        Assert.Equal(CliPolicyEffect.Ask, write.Effect);

        var goalGet = Assert.Single(manifest.Tools, tool => tool.ToolId == "goal_get");
        Assert.Empty(goalGet.Parameters);
    }

    [Fact]
    public void Parse_UnknownCapability_RejectsWithToolParameterAndSource() {
        var yaml = ValidPolicy.Replace("workspace_read", "root_everything", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("bad.yaml", error.Message, StringComparison.Ordinal);
        Assert.Contains("read", error.Message, StringComparison.Ordinal);
        Assert.Contains("file_path", error.Message, StringComparison.Ordinal);
        Assert.Contains("root_everything", error.Message, StringComparison.Ordinal);
        Assert.Contains("workspace_read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownEffect_RejectsInsteadOfDefaultingToAllow() {
        var yaml = ValidPolicy.Replace("effect: ask", "effect: maybe", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("write", error.Message, StringComparison.Ordinal);
        Assert.Contains("maybe", error.Message, StringComparison.Ordinal);
        Assert.Contains("allow/ask/deny", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingParameterEffect_Rejects() {
        var yaml = ValidPolicy.Replace(
            "      offset:\n        effect: allow",
            "      offset:\n        capabilities: []",
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("offset", error.Message, StringComparison.Ordinal);
        Assert.Contains("effect", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateParameter_Rejects() {
        var yaml = ValidPolicy.Replace(
            "      offset:\n        effect: allow",
            "      offset:\n        effect: allow\n      offset:\n        effect: deny",
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "dup.yaml"));

        Assert.Contains("offset", error.Message, StringComparison.Ordinal);
        Assert.Contains("重复", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingToolEffect_Rejects() {
        var yaml = ValidPolicy.Replace("""
          goal_get:
            effect: allow
            parameters: []
        """, """
          goal_get:
            parameters: []
        """, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("goal_get", error.Message, StringComparison.Ordinal);
        Assert.Contains("effect", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingParametersKey_RejectsInsteadOfAssumingNoParameters() {
        var yaml = ValidPolicy.Replace("""
          goal_get:
            effect: allow
            parameters: []
        """, """
          goal_get:
            effect: allow
        """, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("goal_get", error.Message, StringComparison.Ordinal);
        Assert.Contains("parameters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DuplicateTool_Rejects() {
        var yaml = ValidPolicy.Replace("""
          write:
            effect: ask
        """, """
          write:
            effect: ask
          write:
            effect: deny
        """, StringComparison.Ordinal).Replace("""
              content:
                effect: allow
                capabilities: [workspace_write]
        """, "", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "dup.yaml"));

        Assert.Contains("write", error.Message, StringComparison.Ordinal);
        Assert.Contains("重复", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyExecutables_Rejects() {
        var yaml = ValidPolicy.Replace("""
        executables:
          - jcc-tool-file.exe
        """, """
        executables: []
        """, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("executables", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_Rejects() {
        var yaml = ValidPolicy.Replace("schema_version: 1", "schema_version: 2", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => CliToolPolicyLoader.Parse(yaml, "bad.yaml"));

        Assert.Contains("schema_version", error.Message, StringComparison.Ordinal);
        Assert.Contains("2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FilePolicy_CoversEveryDeclaredTool() {
        var policy = CliToolPolicyLoader.Parse(ReadResource("CliNodes.Tests.allpolicies.file.policy.yaml"), "file.yaml");
        var node = NodeManifestLoader.LoadNodeFromText(
            ReadResource("CliNodes.Tests.node.file.yaml"), "file/node.yaml");

        var policyIds = policy.Tools.Select(tool => tool.ToolId).Order(StringComparer.Ordinal).ToArray();
        var nodeIds = node.Tools.Select(tool => tool.Id).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(nodeIds, policyIds);
        Assert.All(policy.Tools, tool => Assert.NotNull(tool.Parameters));
        Assert.Equal(["jcc-tool-file.exe"], policy.Executables);
    }

    [Fact]
    public void FilePolicy_DeclaresWorkingDirReadByApplyPatchHandler() {
        var policy = CliToolPolicyLoader.Parse(ReadResource("CliNodes.Tests.allpolicies.file.policy.yaml"), "file.yaml");

        var applyPatch = Assert.Single(policy.Tools, tool => tool.ToolId == "apply_patch");

        Assert.Contains(applyPatch.Parameters, parameter => parameter.Name == "working_dir");
    }

    [Fact]
    public void AllShippedPolicies_ParseAndMatchTheirExecutable() {
        const string marker = ".allpolicies.";
        var resources = typeof(CliToolPolicyTests).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(marker, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(13, resources.Length);
        foreach (var resourceName in resources) {
            var policy = CliToolPolicyLoader.Parse(ReadResource(resourceName), resourceName);
            var group = resourceName[(resourceName.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]
                .Replace(".policy.yaml", "", StringComparison.Ordinal);
            Assert.Equal($"cli.{group}", policy.PolicyId);
            var expectedExecutables = group switch {
                "github" => new[] { "jcc-tool-github.exe", "jcc-gh.exe" },
                "slash" => new[] { "jcc-tool-slash.exe", "jcc.exe" },
                _ => new[] { $"jcc-tool-{group}.exe" }
            };
            Assert.Equal(expectedExecutables, policy.Executables);
            Assert.NotEmpty(policy.Tools);
        }
    }

    private static string ReadResource(string resourceName) {
        using var stream = typeof(CliToolPolicyTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"测试资源不存在: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
