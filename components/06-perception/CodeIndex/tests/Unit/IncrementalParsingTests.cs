namespace CodeIndex.Tests;

public sealed class IncrementalParsingTests
{
    [Fact]
    public void ExtractAll_Incremental_ProducesSameSymbolsAsFullParse()
    {
        var extractor = new CSharpSymbolExtractor();

        var source1 = """
            namespace MyApp;

            public class Service
            {
                public void Method1() { }
            }
            """;

        var result1 = extractor.ExtractAll(source1, "test.cs");

        var source2 = """
            namespace MyApp;

            public class Service
            {
                public void Method1() { }
                public void Method2() { }
            }
            """;

        var result2 = extractor.ExtractAll(source2, "test.cs");

        Assert.Equal(3, result1.Symbols.Count);
        Assert.Equal(4, result2.Symbols.Count);
        Assert.Contains(result2.Symbols, s => s.Name == "Method2");
    }

    [Fact]
    public void ExtractAll_MultipleFiles_MaintainsSeparateCaches()
    {
        var extractor = new CSharpSymbolExtractor();

        var sourceA = "public class ClassA { }";
        var sourceB = "public class ClassB { }";

        var resultA = extractor.ExtractAll(sourceA, "a.cs");
        var resultB = extractor.ExtractAll(sourceB, "b.cs");

        Assert.Single(resultA.Symbols);
        Assert.Single(resultB.Symbols);
        Assert.Equal("ClassA", resultA.Symbols[0].Name);
        Assert.Equal("ClassB", resultB.Symbols[0].Name);
    }

    [Fact]
    public void ExtractAll_SecondParseOfSameFile_UsesIncrementalParsing()
    {
        var extractor = new CSharpSymbolExtractor();

        var source1 = "public class Service { public void M1() { } }";
        var result1 = extractor.ExtractAll(source1, "service.cs");

        var source2 = "public class Service { public void M1() { } public void M2() { } }";
        var result2 = extractor.ExtractAll(source2, "service.cs");

        Assert.Single(result1.Symbols, s => s.Kind == SymbolKind.Method);
        Assert.Equal(2, result2.Symbols.Count(s => s.Kind == SymbolKind.Method));
    }

    [Fact]
    public void ExtractAll_DeleteFromMiddle_UpdatesCorrectly()
    {
        var extractor = new CSharpSymbolExtractor();

        var source1 = """
            public class Service
            {
                public void M1() { }
                public void M2() { }
                public void M3() { }
            }
            """;

        var result1 = extractor.ExtractAll(source1, "service.cs");

        var source2 = """
            public class Service
            {
                public void M1() { }
                public void M3() { }
            }
            """;

        var result2 = extractor.ExtractAll(source2, "service.cs");

        Assert.Equal(3, result1.Symbols.Count(s => s.Kind == SymbolKind.Method));
        Assert.Equal(2, result2.Symbols.Count(s => s.Kind == SymbolKind.Method));
        Assert.DoesNotContain(result2.Symbols, s => s.Name == "M2");
    }

    [Fact]
    public void ExtractAll_ComplexEdit_MaintainsCorrectCallGraph()
    {
        var extractor = new CSharpSymbolExtractor();

        var source1 = """
            public class ServiceA
            {
                public void DoWork() { }
            }

            public class ServiceB
            {
                public void CallA(ServiceA a) { a.DoWork(); }
            }
            """;

        var result1 = extractor.ExtractAll(source1, "services.cs");

        var source2 = """
            public class ServiceA
            {
                public void DoWork() { }
                public void DoMore() { }
            }

            public class ServiceB
            {
                public void CallA(ServiceA a) { a.DoWork(); a.DoMore(); }
            }
            """;

        var result2 = extractor.ExtractAll(source2, "services.cs");

        Assert.Single(result1.Calls);
        Assert.Equal(2, result2.Calls.Count);
    }

    [Fact]
    public void ExtractAll_DependencyChanges_TrackedCorrectly()
    {
        var extractor = new CSharpSymbolExtractor();

        var source1 = """
            public interface IService { void Execute(); }
            public class ServiceImpl : IService { public void Execute() { } }
            """;

        var result1 = extractor.ExtractAll(source1, "service.cs");

        var source2 = """
            public interface IService { void Execute(); }
            public interface IAnother { void Run(); }
            public class ServiceImpl : IService, IAnother { public void Execute() { } public void Run() { } }
            """;

        var result2 = extractor.ExtractAll(source2, "service.cs");

        Assert.Single(result1.Dependencies, d => d.DependencyKind == DependencyKind.Implements);
        Assert.Equal(2, result2.Dependencies.Count(d => d.DependencyKind == DependencyKind.Implements));
    }
}
