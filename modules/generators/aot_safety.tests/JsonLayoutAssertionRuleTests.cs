namespace AotSafety.Tests;

/// <summary>
/// JCC9311 — 测试项目禁止对 JSON 字符串做排版敏感断言（TASK101 B2）:
/// DTO 序列化输出为紧凑格式 + 非 ASCII 转义, 排版断言（含 <c>"key": value</c> 空格）会随序列化实现变化集体红。
/// 正确做法: Testing.Common.JsonAssert 按路径/值语义断言。
/// </summary>
public sealed class JsonLayoutAssertionRuleTests {
    /// <summary>JCC9311 单规则测试包装器 — 手动设置 ProjectType（对齐 TestAsyncSafetyRules 先例）。</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class TestJsonLayoutRule : DiagnosticAnalyzer {
        public static ProjectType ProjectType { get; set; } = ProjectType.Unknown;

        private static readonly IAnalyzerRule Rule =
            RuleRegistry.Get("JsonSerializerAot", "JCC9311")
            ?? throw new InvalidOperationException("JCC9311 未在 JsonSerializerAot 分析器下注册");

        private static readonly IReadOnlyList<DiagnosticDescriptor> AllDescriptors = Rule.Descriptors;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.CreateRange(AllDescriptors);

        public override void Initialize(AnalysisContext context) {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            context.RegisterCompilationStartAction(compilationContext => {
                var projectContext = new ProjectContext { ProjectType = ProjectType };

                Rule.Register(compilationContext, projectContext);
            });
        }
    }

    [Fact]
    public async Task FluentAssertionsContainWithJsonLayout_ReportsJCC9311() {
        TestJsonLayoutRule.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestJsonLayoutRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("SceneTests.cs", """
                        namespace FluentAssertions.Primitives
                        {
                            public class StringAssertions
                            {
                                public void Contain(string expected) { }
                            }
                        }

                        public class SceneTests
                        {
                            void Run(string json)
                            {
                                new FluentAssertions.Primitives.StringAssertions().Contain({|#0:"{\"count\": 123}"|});
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9311", DiagnosticSeverity.Info).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task XunitAssertContainsWithJsonLayout_ReportsJCC9311() {
        TestJsonLayoutRule.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestJsonLayoutRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ResultTests.cs", """
                        namespace Xunit
                        {
                            public static class Assert
                            {
                                public static void Contains(string expectedSubstring, string actualString) { }
                            }
                        }

                        public class ResultTests
                        {
                            void Run(string json)
                            {
                                Xunit.Assert.Contains({|#0:"\"type\": \"button\""|}, json);
                            }
                        }
                        """),
                },
            },
            ExpectedDiagnostics =
            {
                new DiagnosticResult("JCC9311", DiagnosticSeverity.Info).WithLocation(0),
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ContainWithSemanticSubstring_NoDiagnostic() {
        TestJsonLayoutRule.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestJsonLayoutRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ToolTests.cs", """
                        namespace FluentAssertions.Primitives
                        {
                            public class StringAssertions
                            {
                                public void Contain(string expected) { }
                            }
                        }

                        public class ToolTests
                        {
                            void Run(string json)
                            {
                                new FluentAssertions.Primitives.StringAssertions().Contain("desktop_click");
                            }
                        }
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ContainWithoutSpaceAfterColon_NoDiagnostic() {
        // 紧凑 JSON 子串（"key":value 无空格）不误报 — 该写法与序列化输出无排版耦合
        TestJsonLayoutRule.ProjectType = ProjectType.Test;
        var test = new CSharpAnalyzerTest<TestJsonLayoutRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("ResultTests.cs", """
                        namespace FluentAssertions.Primitives
                        {
                            public class StringAssertions
                            {
                                public void Contain(string expected) { }
                            }
                        }

                        public class ResultTests
                        {
                            void Run(string json)
                            {
                                new FluentAssertions.Primitives.StringAssertions().Contain("{\"count\":123}");
                            }
                        }
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LibraryProjectLayoutContain_NoDiagnostic() {
        // 规则仅作用于测试项目 — 生产项目不受影响
        TestJsonLayoutRule.ProjectType = ProjectType.Library;
        var test = new CSharpAnalyzerTest<TestJsonLayoutRule, DefaultVerifier> {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestState =
            {
                Sources =
                {
                    ("JsonHelper.cs", """
                        namespace FluentAssertions.Primitives
                        {
                            public class StringAssertions
                            {
                                public void Contain(string expected) { }
                            }
                        }

                        public static class JsonHelper
                        {
                            public static void Check(string json)
                            {
                                new FluentAssertions.Primitives.StringAssertions().Contain("{\"count\": 123}");
                            }
                        }
                        """),
                },
            },
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
