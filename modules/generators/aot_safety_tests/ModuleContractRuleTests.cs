namespace AotSafety.Tests;

/// <summary>
/// JCC12001 — 域契约缺失或非法：18 个契约域必须声明合法 JccDomain，
/// JccAllowedDomains 只能包含已知域；测试项目与非交付工具项目豁免。
/// </summary>
public sealed class ModuleContractRuleTests {
    /// <summary>JCC12001 单规则测试包装器 — 直接注入 ProjectContext，避免 AnalyzerConfigFiles 不传 build_property 的限制。</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class TestModuleContractRules : DiagnosticAnalyzer {
        public static ProjectContext Context { get; set; } = new() {
            ProjectType = ProjectType.Library,
            ProjectDirectory = "D:\\repo\\modules\\llm",
            Domain = "llm",
        };

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC12001")
            ?? throw new InvalidOperationException("JCC12001 未在 AotSafety 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => {
                Rule.Register(compilationContext, Context);
            });
        }
    }

    private const string SourceTemplate = "{|#0:public|} class C { }";

    [Fact]
    public async Task ValidContract_NoDiagnostic() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = "D:\\repo\\modules\\llm",
            Domain = "llm",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task MissingDomain_ReportsJCC12001() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = "D:\\repo\\modules\\llm",
            Domain = "",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC12001", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UnknownDomain_ReportsJCC12001() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = "D:\\repo\\modules\\llm",
            Domain = "not_a_domain",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC12001", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UnknownAllowedDomain_ReportsJCC12001() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Library,
            ProjectDirectory = "D:\\repo\\modules\\llm",
            Domain = "llm",
            AllowedDomains = "abstractions,not_a_domain",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC12001", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TestProjectWithoutDomain_NoDiagnostic() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Test,
            ProjectDirectory = "D:\\repo\\modules\\llm\\llm.tests",
            Domain = "",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task NonContractToolProject_NoDiagnostic() {
        TestModuleContractRules.Context = new ProjectContext {
            ProjectType = ProjectType.Tool,
            ProjectDirectory = "D:\\repo\\non_deliverables_tools\\probe",
            Domain = "",
        };

        var test = new CSharpAnalyzerTest<TestModuleContractRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = SourceTemplate,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
