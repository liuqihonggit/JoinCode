namespace AotSafety.Tests;

/// <summary>
/// JCC1018 — 测试项目禁止 Windows 盘符路径字面量（TASK070 CI #959 事故防复发）:
/// 盘符路径喂给 Path.GetFullPath 在 Linux 归一化语义分叉。非测试项目豁免（生产 Windows 专属常量合法）。
/// </summary>
public sealed class WindowsPathLiteralRuleTests {
    /// <summary>AotSafety 规则测试包装器 — 手动设置 ProjectType（对齐 TestAsyncSafetyRules 先例）。</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class TestAotSafetyRules : DiagnosticAnalyzer {
        public static ProjectType ProjectType { get; set; } = ProjectType.Unknown;

        private static readonly IReadOnlyList<IAnalyzerRule> Rules = RuleRegistry.GetListByAnalyzer("AotSafety");
        private static readonly IReadOnlyList<DiagnosticDescriptor> AllDescriptors =
            Rules.SelectMany(r => r.Descriptors).ToArray();

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(AllDescriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(compilationContext => {
                var projectContext = new ProjectContext { ProjectType = ProjectType };

                foreach (var rule in Rules) {
                    rule.Register(compilationContext, projectContext);
                }
            });
        }
    }

    [Fact]
    public async Task VerbatimDriveLetterInTestProject_ReportsJCC1018() {
        TestAotSafetyRules.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestAotSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ResumeStepTests.cs", """
                        public class ResumeStepTests
                        {
                            void Run()
                            {
                                var path = {|#0:@"D:\cur\repo"|};
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1018", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EscapedDriveLetterInTestProject_ReportsJCC1018() {
        TestAotSafetyRules.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestAotSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("SomeTests.cs", """
                        public class SomeTests
                        {
                            string Path1() => {|#0:"D:\\repo"|};
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1018", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UrlScheme_NoDiagnostic() {
        // https:// 的单字母尾部(s:/)被"前一字符非字母"约束排除 — URL 协议不误报
        TestAotSafetyRules.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestAotSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ApiTests.cs", """
                        public class ApiTests
                        {
                            string Url() => "https://api.example.com/v1";
                            string Ftp() => "ftp://files.example.com/pub";
                        }
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FileUrlWithDriveLetter_ReportsJCC1018() {
        // file:///D:/ 携带盘符 — 也是平台专属数据（D 前是 / 非字母 → 命中）
        TestAotSafetyRules.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestAotSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("FileUrlTests.cs", """
                        public class FileUrlTests
                        {
                            string FileUrl() => {|#0:"file:///D:/data/x.json"|};
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC1018", DiagnosticSeverity.Warning).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DriveLetterInLibraryProject_NoDiagnostic() {
        // 生产项目 Windows 专属常量合法（managed-settings 路径/注册表等）
        TestAotSafetyRules.ProjectType = ProjectType.Library;
        var test = new CSharpAnalyzerTest<TestAotSafetyRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ManagedPaths.cs", """
                        public static class ManagedPaths
                        {
                            public const string ManagedSettingsPath = @"C:\Program Files\jcc\managed-settings.json";
                        }
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
