namespace AotSafety.Tests;

/// <summary>
/// P2 架构规则 fixture 测试 — JCC12006/12007/12008/12013。
/// 每个规则用独立包装器固定 RuleId，避免静态选择器在并行测试间串味。
/// </summary>
public sealed class ArchitectureRulesTests {
    private const string LibraryPath = "D:\\repo\\modules\\llm";

    private static ProjectContext ContractContext(string? apiVisibility = "internal") => new() {
        ProjectType = ProjectType.Library,
        ProjectDirectory = LibraryPath,
        Domain = "llm",
        ApiVisibility = apiVisibility ?? "",
    };

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class PublicApiBoundaryRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = ContractContext();

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC12006")
            ?? throw new InvalidOperationException("JCC12006 未在 AotSafety 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, Context));
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class IvtAllowlistRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = ContractContext();

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC12007")
            ?? throw new InvalidOperationException("JCC12007 未在 AotSafety 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, Context));
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class NamespaceRootRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = ContractContext();

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC12008")
            ?? throw new InvalidOperationException("JCC12008 未在 AotSafety 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, Context));
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class PrivateLayerRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = ContractContext();

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC12013")
            ?? throw new InvalidOperationException("JCC12013 未在 AotSafety 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, Context));
        }
    }

    private static CSharpAnalyzerTest<T, DefaultVerifier> CreateTest<T>(string source)
        where T : DiagnosticAnalyzer, new() {
        return new CSharpAnalyzerTest<T, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = source,
        };
    }

    [Fact]
    public async Task PublicTypeInInternalApi_ReportsJCC12006() {
        PublicApiBoundaryRules.Context = ContractContext("internal");

        var test = CreateTest<PublicApiBoundaryRules>("public class {|#0:C|} { }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC12006", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicTypeInPublicApi_NoDiagnostic() {
        PublicApiBoundaryRules.Context = ContractContext("public");

        var test = CreateTest<PublicApiBoundaryRules>("public class C { }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InternalTypeInInternalApi_NoDiagnostic() {
        PublicApiBoundaryRules.Context = ContractContext("internal");

        var test = CreateTest<PublicApiBoundaryRules>("internal class C { }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicTypeInTestProject_NoDiagnostic() {
        PublicApiBoundaryRules.Context = new ProjectContext {
            ProjectType = ProjectType.Test,
            ProjectDirectory = "D:\\repo\\modules\\llm\\llm.tests",
            ApiVisibility = "internal",
        };

        var test = CreateTest<PublicApiBoundaryRules>("public class C { }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ProductionIvtWithoutAllowlist_ReportsJCC12007() {
        IvtAllowlistRules.Context = ContractContext();

        var test = CreateTest<IvtAllowlistRules>(
            "{|#0:[|}assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"JoinCode.Production\")]\npublic class C { }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC12007", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ProductionIvtInAllowlist_NoDiagnostic() {
        IvtAllowlistRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = LibraryPath,
            Domain = "llm",
            AllowInternalsVisibleTo = "JoinCode.Production",
        };

        var test = CreateTest<IvtAllowlistRules>(
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"JoinCode.Production\")]\npublic class C { }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TestAssemblyIvt_NoDiagnostic() {
        IvtAllowlistRules.Context = ContractContext();

        var test = CreateTest<IvtAllowlistRules>(
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"JoinCode.Llm.Tests\")]\npublic class C { }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NamespaceOutsideRoots_ReportsJCC12008() {
        NamespaceRootRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = LibraryPath,
            Domain = "llm",
            NamespaceRoots = "JoinCode.Llm",
        };

        var test = CreateTest<NamespaceRootRules>("namespace Other { public class {|#0:C|} { } }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC12008", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NamespaceUnderRoot_NoDiagnostic() {
        NamespaceRootRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = LibraryPath,
            Domain = "llm",
            NamespaceRoots = "JoinCode.Llm;JoinCode.Shared",
        };

        var test = CreateTest<NamespaceRootRules>("namespace JoinCode.Llm.Sub { public class C { } }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NamespacePrefixBoundary_ReportsJCC12008() {
        NamespaceRootRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = LibraryPath,
            Domain = "llm",
            NamespaceRoots = "JoinCode.Llm",
        };

        var test = CreateTest<NamespaceRootRules>("namespace JoinCode.LlmX { public class {|#0:C|} { } }");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC12008", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EmptyNamespaceRoots_NoDiagnostic() {
        NamespaceRootRules.Context = ContractContext();

        var test = CreateTest<NamespaceRootRules>("namespace Other { public class C { } }");

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicTypeInPrivateDirectory_ReportsJCC12013() {
        PrivateLayerRules.Context = ContractContext();

        var test = new CSharpAnalyzerTest<PrivateLayerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("private/Foo.cs", "{|#0:public|} class C { }"));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC12013", DiagnosticSeverity.Info).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InternalTypeInPrivateDirectory_NoDiagnostic() {
        PrivateLayerRules.Context = ContractContext();

        var test = new CSharpAnalyzerTest<PrivateLayerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("private/Foo.cs", "internal class C { }"));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task PublicTypeOutsidePrivateDirectory_NoDiagnostic() {
        PrivateLayerRules.Context = ContractContext();

        var test = new CSharpAnalyzerTest<PrivateLayerRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("public/Foo.cs", "public class C { }"));

        await test.RunAsync().ConfigureAwait(true);
    }
}
