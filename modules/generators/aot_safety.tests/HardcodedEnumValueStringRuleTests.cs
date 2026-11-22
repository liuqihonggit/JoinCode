namespace AotSafety.Tests;

/// <summary>
/// JCC10007 回归测试 — 字段默认值与 [EnumValue] 同值时的报告边界。
/// <para>不变量: 文件未引用枚举时不报告（外部协议字符串场景）；引用枚举时才报告。</para>
/// </summary>
public sealed class HardcodedEnumValueStringRuleTests {
    private const string EnumDefinitions = """
        namespace Demo;

        public sealed class EnumValueAttribute : System.Attribute {
            public EnumValueAttribute(string value) { }
        }

        public enum CommandOrigin {
            [EnumValue("user")]
            User
        }
        """;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class HardcodedEnumValueRules : DiagnosticAnalyzer {
        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("CodeOrganization", "JCC10007")
            ?? throw new InvalidOperationException("JCC10007 未在 CodeOrganization 分析器下注册");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(Rule.Descriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(compilationContext => Rule.Register(compilationContext, new ProjectContext {
                ProjectType = ProjectType.Library,
                ProjectDirectory = "D:\\repo\\modules\\apps\\cli",
                Domain = "apps",
            }));
        }
    }

    private static CSharpAnalyzerTest<HardcodedEnumValueRules, DefaultVerifier> CreateTest(
        params (string FileName, string Source)[] files) {
        var test = new CSharpAnalyzerTest<HardcodedEnumValueRules, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        foreach (var (fileName, source) in files)
            test.TestState.Sources.Add((fileName, source));
        return test;
    }

    [Fact]
    public async Task FieldDefaultMatchingEnumValue_WithoutEnumReference_NoDiagnostic() {
        var test = CreateTest(
            ("Enum.cs", EnumDefinitions),
            ("Consumer.cs", "namespace Demo; public sealed class Consumer { private readonly string _apiKeySource = \"user\"; }"));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FieldDefaultMatchingEnumValue_WithEnumReference_ReportsJCC10007() {
        var test = CreateTest(
            ("Enum.cs", EnumDefinitions),
            ("Consumer.cs", "namespace Demo; public sealed class Consumer { private readonly string _apiKeySource = {|#0:\"user\"|}; public bool IsUser(CommandOrigin origin) => origin == CommandOrigin.User; }"));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC10007", DiagnosticSeverity.Warning).WithLocation(0));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CoalesceDefaultMatchingEnumValue_WithEnumReference_NoDiagnostic() {
        var test = CreateTest(
            ("Enum.cs", EnumDefinitions),
            ("Consumer.cs", "namespace Demo; public sealed class Consumer { private readonly string _s; public Consumer(string? v) { _s = v ?? \"user\"; } public bool IsUser(CommandOrigin origin) => origin == CommandOrigin.User; }"));

        await test.RunAsync().ConfigureAwait(true);
    }
}