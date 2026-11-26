namespace CliPermission.Analyzer.Tests;

/// <summary>
/// 编译期策略索引的解析测试 — 确保分析器看到的是与运行时 YAML 相同的工具/参数集合。
/// </summary>
public sealed class PolicyIndexTests {
    [Fact]
    public void Load_ParsesToolsAndParameters() {
        const string policy = """
            schema_version: 1
            policy_id: cli.test
            policy_version: 1
            executables:
              - jcc-tool-test.exe
            tools:
              read:
                effect: allow
                parameters:
                  file_path:
                    effect: allow
                  offset:
                    effect: allow
            """;
        var additional = new TestAdditionalText("policy.yaml", policy);

        var index = PolicyIndex.Load([additional], CancellationToken.None);

        Assert.Equal("cli.test", index.PolicyId);
        var tool = Assert.Single(index.Tools).Value;
        Assert.Equal("read", tool.ToolId);
        Assert.Equal(["file_path", "offset"], tool.Parameters.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Load_InlineCommentAfterEffect_StillParsesDeny() {
        const string policy = """
            schema_version: 1
            policy_id: cli.test
            policy_version: 1
            executables:
              - jcc-tool-test.exe
            tools:
              read:
                effect: allow
                parameters:
                  pages:
                    effect: deny  # 本地实现未提供分页
            """;
        var additional = new TestAdditionalText("policy.yaml", policy);

        var index = PolicyIndex.Load([additional], CancellationToken.None);

        var parameter = Assert.Single(Assert.Single(index.Tools).Value.Parameters).Value;
        Assert.Equal(PolicyEffect.Deny, parameter.Effect);
    }

    private sealed class TestAdditionalText(string path, string content) : AdditionalText {
        private readonly SourceText _text = SourceText.From(content, Encoding.UTF8);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
