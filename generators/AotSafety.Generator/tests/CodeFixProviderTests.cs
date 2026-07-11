namespace AotSafety.Tests;

using JccCodeFixes;

public class Jcc6005CodeFixProviderTests
{
    [Fact]
    public async Task InsertAtZero_InLoop_ReportsJCC6005()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void BuildList(List<string> items, string[] source)
                    {
                        foreach (var s in source)
                        {
                            {|JCC6005:items.Insert(0, s)|};
                        }
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InsertAtZero_OutsideLoop_AlsoReports()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void BuildList(List<string> items)
                    {
                        {|JCC6005:items.Insert(0, "first")|};
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task InsertAtNonZero_InLoop_NoWarning()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    void BuildList(List<string> items, string[] source)
                    {
                        foreach (var s in source)
                        {
                            items.Insert(1, s);
                        }
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}

public class Jcc6002CodeFixProviderTests
{
    [Fact]
    public async Task ListContains_InLoop_ReportsJCC6002()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    bool FindItem(List<string> list, string[] keys)
                    {
                        foreach (var key in keys)
                        {
                            if ({|JCC6002:list.Contains(key)|})
                                return true;
                        }
                        return false;
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListContains_OutsideLoop_NoWarning()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    bool FindItem(List<string> list, string key)
                    {
                        return list.Contains(key);
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ListIndexOf_InLoop_ReportsJCC6002()
    {
        var test = new CSharpAnalyzerTest<PerformanceRules, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                using System.Collections.Generic;
                class TestClass
                {
                    int FindIndex(List<string> list, string[] keys)
                    {
                        foreach (var key in keys)
                        {
                            var idx = {|JCC6002:list.IndexOf(key)|};
                            if (idx >= 0) return idx;
                        }
                        return -1;
                    }
                }
                """,
        };

        await test.RunAsync().ConfigureAwait(true);
    }
}
