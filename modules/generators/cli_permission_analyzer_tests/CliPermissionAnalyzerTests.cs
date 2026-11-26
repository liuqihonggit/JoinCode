namespace CliPermission.Analyzer.Tests;

/// <summary>
/// CliPermissionAnalyzer 行为测试 — 用真实 Roslyn 编译与 AdditionalText 锁定双向对账。
/// </summary>
public sealed class CliPermissionAnalyzerTests {
    private const string FixtureSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace JoinCode.Abstractions.Tools
        {
            public delegate Task<int> CliNodeHandler(
                IReadOnlyDictionary<string, string> options,
                CancellationToken cancellationToken);
        }

        namespace Fixture
        {
            internal static class Commands
            {
                private static readonly Dictionary<string, JoinCode.Abstractions.Tools.CliNodeHandler> Handlers =
                    new(StringComparer.Ordinal)
                    {
                        ["read"] = ReadAsync,
                    };

                private static Task<int> ReadAsync(
                    IReadOnlyDictionary<string, string> options,
                    CancellationToken cancellationToken)
                {
                    var path = Required(options, "file_path", "read");
                    var offset = GetInt(options, "offset", 1);
                    Console.WriteLine(path + offset);
                    return Task.FromResult(0);
                }

                private static string Required(
                    IReadOnlyDictionary<string, string> options,
                    string key,
                    string toolId)
                    => options.TryGetValue(key, out var value) ? value : toolId;

                private static int GetInt(
                    IReadOnlyDictionary<string, string> options,
                    string key,
                    int fallback)
                    => int.TryParse(Get(options, key, string.Empty), out var value) ? value : fallback;

                private static string Get(
                    IReadOnlyDictionary<string, string> options,
                    string key,
                    string fallback)
                    => options.TryGetValue(key, out var value) ? value : fallback;
            }
        }
        """;

    private const string ValidPolicy = """
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
                capabilities: [workspace_read]
              offset:
                effect: allow
        """;

    [Fact]
    public async Task CompletePolicy_NoDiagnostic() {
        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", ValidPolicy));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SourceParameterMissingFromPolicy_ReportsJcc13001() {
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
                    capabilities: [workspace_read]
            """;

        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", policy));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("JCC13001", diagnostic.Id);
        Assert.Contains("offset", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("read", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolicyParameterNotReadByHandler_ReportsJcc13002() {
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
                    capabilities: [workspace_read]
                  offset:
                    effect: allow
                  limit:
                    effect: allow
            """;

        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", policy));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("JCC13002", diagnostic.Id);
        Assert.Contains("limit", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("read", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DenyOnlyPolicyParameter_IsTombstoneAndProducesNoDiagnostic() {
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
                    capabilities: [workspace_read]
                  offset:
                    effect: allow
                  pages:
                    effect: deny
            """;

        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", policy));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task MissingPolicyTool_ReportsJcc13001WithToolName() {
        var policy = ValidPolicy.Replace("  read:\n", "  write:\n", StringComparison.Ordinal);

        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", policy));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("JCC13001", diagnostic.Id);
        Assert.Contains("read", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("cli.test", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DynamicParameterKey_ReportsJcc13004() {
        var source = FixtureSource.Replace(
            "var offset = GetInt(options, \"offset\", 1);",
            "var key = \"offset\";\n        var offset = GetInt(options, key, 1);",
            StringComparison.Ordinal);

        var diagnostics = await AnalyzeAsync(source, ("policy.yaml", ValidPolicy));

        var dynamicDiagnostic = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "JCC13004");
        Assert.Contains("key", dynamicDiagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPolicyFile_ReportsJcc13008() {
        var diagnostics = await AnalyzeAsync(FixtureSource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("JCC13008", diagnostic.Id);
        Assert.Contains("policy", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SharedHelper_DoesNotTreatToolIdLiteralAsParameter() {
        var diagnostics = await AnalyzeAsync(FixtureSource, ("policy.yaml", ValidPolicy));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.GetMessage().Contains("'read'", StringComparison.Ordinal));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source, params (string Path, string Content)[] additionalFiles) {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: "Fixture.cs");
        var compilation = CSharpCompilation.Create(
            "CliPermissionFixture",
            [syntaxTree],
            TrustedReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var additionalTexts = additionalFiles
            .Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Content))
            .ToImmutableArray();
        var analyzerOptions = new AnalyzerOptions(additionalTexts);
        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new CliPermissionAnalyzer()),
            analyzerOptions);

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedReferences = new(() => {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    });

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText {
        private readonly SourceText _text = SourceText.From(content, Encoding.UTF8);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
