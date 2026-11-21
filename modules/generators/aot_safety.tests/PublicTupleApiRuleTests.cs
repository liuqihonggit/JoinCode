namespace AotSafety.Tests;

/// <summary>
/// JCC10011 公开 API 禁元组 — fixture 测试。
/// </para>
/// </summary>
public sealed class PublicTupleApiRuleTests {
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class PublicTupleApiRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = LibraryContext();

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("CodeOrganization", "JCC10011")
            ?? throw new InvalidOperationException("JCC10011 未在 CodeOrganization 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, Context));
        }
    }

    private static ProjectContext LibraryContext() => new() {
        ProjectType = ProjectType.Library,
        ProjectDirectory = "D:\\repo\\modules\\llm",
        Domain = "llm",
    };

    private static CSharpAnalyzerTest<PublicTupleApiRules, DefaultVerifier> CreateTest(string source) {
        return new CSharpAnalyzerTest<PublicTupleApiRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = source,
        };
    }

    [Fact]
    public async Task PublicMethodReturningTuple_ReportsJCC10011() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public class C { public (int, string) {|#0:M|}() => (1, \"a\"); }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC10011", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodTupleParameter_ReportsJCC10011() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public class C { public void {|#0:M|}((int, string) value) { } }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC10011", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicPropertyWithNestedTuple_ReportsJCC10011() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public class C { public System.Collections.Generic.List<(int, string)> {|#0:P|} { get; } = new(); }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC10011", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicFieldTuple_ReportsJCC10011() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public class C { public (int, string) {|#0:F|}; }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC10011", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InternalMethodReturningTuple_NoDiagnostic() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public class C { internal (int, string) M() => (1, \"a\"); }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodInInternalType_NoDiagnostic() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("internal class C { public (int, string) M() => (1, \"a\"); }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicMethodWithNamedRecord_NoDiagnostic() {
        PublicTupleApiRules.Context = LibraryContext();

        var test = CreateTest("public record Pair(int A, string B); public class C { public Pair M() => new(1, \"a\"); }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TestProject_NoDiagnostic() {
        PublicTupleApiRules.Context = new ProjectContext {
            ProjectType = ProjectType.Test,
            ProjectDirectory = "D:\\repo\\modules\\llm\\llm.tests",
        };

        var test = CreateTest("public class C { public (int, string) M() => (1, \"a\"); }");

        await test.RunAsync().ConfigureAwait(true);
    }
}
