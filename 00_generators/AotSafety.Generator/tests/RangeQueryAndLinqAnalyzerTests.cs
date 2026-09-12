namespace AotSafety.Tests;

public class RangeQueryAnalyzerTests
{
    [Fact]
    public async Task RangeCondition_InForEach_ReportsJCC6006()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void FindRange(List<int> sorted, int low, int high)
                    {
                        foreach (var x in sorted)
                        {
                            if (x >= low && x <= high)
                            {
                                var y = x;
                            }
                        }
                    }
                }
                """,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC6006", DiagnosticSeverity.Info).WithLocation(8, 17));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RangeCondition_InFor_ReportsJCC6006()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                class TestClass
                {
                    void FindRange(int[] arr, int low, int high)
                    {
                        for (var i = 0; i < arr.Length; i++)
                        {
                            if (arr[i] >= low && arr[i] <= high)
                            {
                                var x = arr[i];
                            }
                        }
                    }
                }
                """,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC6006", DiagnosticSeverity.Info).WithLocation(7, 17));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SingleComparison_NoWarning()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void FindAbove(List<int> sorted, int threshold)
                    {
                        foreach (var x in sorted)
                        {
                            if (x >= threshold)
                            {
                                var y = x;
                            }
                        }
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}

public class ForeachToLinqAnalyzerTests
{
    [Fact]
    public async Task ForeachIfAdd_ReportsJCC6008()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    List<int> Filter(List<int> source)
                    {
                        var result = new List<int>();
                        foreach (var x in source)
                        {
                            if (x > 0) result.Add(x);
                        }
                        return result;
                    }
                }
                """,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC6008", DiagnosticSeverity.Info).WithLocation(7, 9).WithArguments("过滤+收集", ".Where(...).ToList()"));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ForeachIfReturnTrue_ReportsJCC6008()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    bool HasMatch(List<int> source, int target)
                    {
                        foreach (var x in source)
                        {
                            if (x == target) return true;
                        }
                        return false;
                    }
                }
                """,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC6008", DiagnosticSeverity.Info).WithLocation(6, 9).WithArguments("存在判断", ".Any(...)"));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ForeachAddAssign_ReportsJCC6008()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    int Total(List<int> source)
                    {
                        var sum = 0;
                        foreach (var x in source)
                        {
                            sum += x;
                        }
                        return sum;
                    }
                }
                """,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("JCC6008", DiagnosticSeverity.Info).WithLocation(7, 9).WithArguments("聚合", ".Sum(...)"));

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ComplexLoopBody_NoWarning()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void Process(List<int> source)
                    {
                        var sum = 0;
                        var count = 0;
                        var max = 0;
                        var min = int.MaxValue;
                        foreach (var x in source)
                        {
                            sum += x;
                            count++;
                            if (x > max) max = x;
                            if (x < min) min = x;
                        }
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
