namespace AotSafety.Tests;

/// <summary>
/// JCC9312 — 非测试程序集的 [ModuleInitializer] 必须 csproj 显式登记（TASK101 D1）:
/// 根目录全局豁免 CA2255 后，由该规则接管治理；测试程序集默认放行。
/// </summary>
public sealed class ModuleInitializerGovernanceRuleTests {
    /// <summary>JCC9312 单规则测试包装器 — 手动设置 ProjectType 与登记开关（对齐 TestAsyncSafetyRules 先例）。</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class TestModuleInitializerRule : DiagnosticAnalyzer {
        public static ProjectType ProjectType { get; set; } = ProjectType.Unknown;
        public static bool AllowModuleInitializer { get; set; }

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("AotSafety", "JCC9312")
            ?? throw new InvalidOperationException("JCC9312 未在 AotSafety 分析器下注册");

        private static readonly IReadOnlyList<DiagnosticDescriptor> AllDescriptors = Rule.Descriptors;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(AllDescriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(compilationContext => {
                var projectContext = new ProjectContext {
                    ProjectType = ProjectType,
                    AllowModuleInitializer = AllowModuleInitializer,
                };

                Rule.Register(compilationContext, projectContext);
            });
        }
    }

    private const string ModuleInitSource = """
        using System.Runtime.CompilerServices;

        internal static class Boot
        {
            [{|#0:ModuleInitializer|}]
            internal static void Init() { }
        }
        """;

    [Fact]
    public async Task LibraryProjectWithoutRegistration_ReportsJCC9312() {
        TestModuleInitializerRule.ProjectType = ProjectType.Library;
        TestModuleInitializerRule.AllowModuleInitializer = false;
        var test = new CSharpAnalyzerTest<TestModuleInitializerRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = ModuleInitSource,
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9312", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task TestProject_NoDiagnostic() {
        TestModuleInitializerRule.ProjectType = ProjectType.Test;
        TestModuleInitializerRule.AllowModuleInitializer = false;
        var test = new CSharpAnalyzerTest<TestModuleInitializerRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Runtime.CompilerServices;

                internal static class Boot
                {
                    [ModuleInitializer]
                    internal static void Init() { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LibraryProjectWithRegistration_NoDiagnostic() {
        TestModuleInitializerRule.ProjectType = ProjectType.Library;
        TestModuleInitializerRule.AllowModuleInitializer = true;
        var test = new CSharpAnalyzerTest<TestModuleInitializerRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Runtime.CompilerServices;

                internal static class Boot
                {
                    [ModuleInitializer]
                    internal static void Init() { }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
