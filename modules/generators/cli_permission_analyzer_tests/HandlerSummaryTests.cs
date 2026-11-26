namespace CliPermission.Analyzer.Tests;

/// <summary>helper 链参数键摘要测试 — 只追踪真正作为字典键传入的字面量。</summary>
public sealed class HandlerSummaryTests {
    [Fact]
    public void Build_FollowsHelperChainAndSkipsToolIdLiteral() {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Fixture {
                internal static class Commands {
                    private static Task<int> ReadAsync(
                        IReadOnlyDictionary<string, string> options,
                        CancellationToken cancellationToken) {
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
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "Summary",
            [tree],
            TrustedReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetSemanticModel(tree)
            .GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "ReadAsync")) as IMethodSymbol;
        Assert.NotNull(method);

        var summary = new CliPermissionAnalyzer.HandlerSummaryBuilder(compilation, CancellationToken.None).Build(method);

        Assert.Equal(["file_path", "offset"], summary.Parameters.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(summary.KeyParameterIndices);
        Assert.Empty(summary.DynamicLocations);
    }

    [Fact]
    public void Build_FollowsExpressionBodiedForwarderInSameFile() {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Fixture {
                internal static class Commands {
                    private static Task<int> LspGotoDefinitionAsync(
                        IReadOnlyDictionary<string, string> options,
                        CancellationToken cancellationToken)
                        => LspLocationToolAsync(options, cancellationToken, "textDocument/definition");
                    private static async Task<int> LspLocationToolAsync(
                        IReadOnlyDictionary<string, string> options,
                        CancellationToken cancellationToken,
                        string method) {
                        var file = Required(options, "file_path", method);
                        var line = GetInt(options, "line", 1);
                        await Task.Yield();
                        return file.Length + line;
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
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "SummaryForwarderSameFile",
            [tree],
            TrustedReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetSemanticModel(tree)
            .GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "LspGotoDefinitionAsync")) as IMethodSymbol;
        Assert.NotNull(method);

        var summary = new CliPermissionAnalyzer.HandlerSummaryBuilder(compilation, CancellationToken.None).Build(method);

        Assert.Equal(["file_path", "line"], summary.Parameters.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Build_FollowsExpressionBodiedForwarderAcrossPartialFiles() {
        const string forwarder = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Fixture {
                internal static partial class Commands {
                    private static Task<int> LspGotoDefinitionAsync(
                        IReadOnlyDictionary<string, string> options,
                        CancellationToken cancellationToken)
                        => LspLocationToolAsync(options, cancellationToken, "textDocument/definition");
                }
            }
            """;
        const string implementation = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            namespace Fixture {
                internal static partial class Commands {
                    private static async Task<int> LspLocationToolAsync(
                        IReadOnlyDictionary<string, string> options,
                        CancellationToken cancellationToken,
                        string method) {
                        var file = Required(options, "file_path", method);
                        var line = GetInt(options, "line", 1);
                        await Task.Yield();
                        return file.Length + line;
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
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new[] {
            CSharpSyntaxTree.ParseText(forwarder, parseOptions),
            CSharpSyntaxTree.ParseText(implementation, parseOptions)
        };
        var compilation = CSharpCompilation.Create(
            "SummaryForwarder",
            trees,
            TrustedReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var syntaxTree = trees[0];
        var method = compilation.GetSemanticModel(syntaxTree)
            .GetDeclaredSymbol(syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(node => node.Identifier.ValueText == "LspGotoDefinitionAsync")) as IMethodSymbol;
        Assert.NotNull(method);

        var summary = new CliPermissionAnalyzer.HandlerSummaryBuilder(compilation, CancellationToken.None).Build(method);

        Assert.Equal(["file_path", "line"], summary.Parameters.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(summary.KeyParameterIndices);
        Assert.Empty(summary.DynamicLocations);
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedReferences = new(() => {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    });
}
