namespace JoinCode.CodeIndex.Tests;

public sealed class CSharpDependencyExtractorTests
{
    private readonly CSharpSymbolExtractor _symbolExtractor = new();

    [Fact]
    public void ExtractDependencies_ClassInheritance_ReturnsInheritsEdge()
    {
        var source = """
            public class Animal { }
            public class Dog : Animal { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Dog" && d.TargetSymbol == "Animal" && d.DependencyKind == DependencyKind.Inherits);
    }

    [Fact]
    public void ExtractDependencies_InterfaceImplementation_ReturnsImplementsEdge()
    {
        var source = """
            public interface IRepository { }
            public class SqlRepository : IRepository { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "SqlRepository" && d.TargetSymbol == "IRepository" && d.DependencyKind == DependencyKind.Implements);
    }

    [Fact]
    public void ExtractDependencies_FieldType_ReturnsUsesEdge()
    {
        var source = """
            public class Logger { }
            public class Service
            {
                private Logger _logger;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Logger" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_MethodParameterType_ReturnsUsesEdge()
    {
        var source = """
            public class Request { }
            public class Handler
            {
                public void Process(Request req) { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Handler" && d.TargetSymbol == "Request" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_MethodReturnType_ReturnsUsesEdge()
    {
        var source = """
            public class Result { }
            public class Service
            {
                public Result Execute() { return null; }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Result" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_UsingDirective_ReturnsImportsEdge()
    {
        var source = """
            using System.Collections.Generic;
            public class Service { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.TargetSymbol == "System.Collections.Generic" && d.DependencyKind == DependencyKind.Imports);
    }

    [Fact]
    public void ExtractDependencies_NoDependencies_ReturnsEmptyList()
    {
        var source = """
            public class Standalone { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Empty(deps);
    }

    [Fact]
    public void ExtractDependencies_GenericTypeArgument_ExtractsInnerType()
    {
        var source = """
            public class Repository { }
            public class Service
            {
                private List<Repository> _repos;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Repository" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_GenericBaseClass_ExtractsTypeArgs()
    {
        var source = """
            public class Entity { }
            public class RepositoryBase<T> { }
            public class EntityRepository : RepositoryBase<Entity> { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "EntityRepository" && d.TargetSymbol == "Entity" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_GenericConstraint_ExtractsConstraintType()
    {
        var source = """
            public interface IEntity { }
            public class Repository<T> where T : IEntity { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Repository" && d.TargetSymbol == "IEntity" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_Attribute_ExtractsAttributeType()
    {
        var source = """
            public class CustomAttribute : System.Attribute { }
            [Custom]
            public class Service { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Custom" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_NestedType_ReturnsContainsEdge()
    {
        var source = """
            public class Outer
            {
                public class Inner { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Outer" && d.TargetSymbol == "Outer.Inner" && d.DependencyKind == DependencyKind.Contains);
    }

    [Fact]
    public void ExtractDependencies_BclTypeNotIncluded_FiltersList()
    {
        var source = """
            public class Service
            {
                private List<string> _items;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.DoesNotContain(deps, d => d.TargetSymbol == "List");
        Assert.DoesNotContain(deps, d => d.TargetSymbol == "string");
    }

    [Fact]
    public void ExtractDependencies_LowercaseCustomType_Extracted()
    {
        var source = """
            public class myService
            {
            }
            public class Client
            {
                private myService _svc;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Client" && d.TargetSymbol == "myService" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_NestedGenericConstraint_ExtractsAllTypes()
    {
        var source = """
            public interface IValidator { }
            public interface IHandler { }
            public class Service<T> where T : IValidator, IHandler { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.TargetSymbol == "IValidator" && d.DependencyKind == DependencyKind.Uses);
        Assert.Contains(deps, d => d.TargetSymbol == "IHandler" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_NestedGenericArguments_ExtractsAllTypes()
    {
        var source = """
            public class Repository { }
            public class Service
            {
                private Dictionary<string, List<Repository>> _cache;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Repository" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_FullAttributeName_StripsSuffix()
    {
        var source = """
            public class CustomAttribute { }
            [CustomAttribute]
            public class Service { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Custom" && d.DependencyKind == DependencyKind.Uses);
    }

    [Fact]
    public void ExtractDependencies_MethodAttribute_ExtractsType()
    {
        var source = """
            public class TestAttribute { }
            public class Service
            {
                [Test]
                public void Execute() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpDependencyExtractor();
        var deps = extractor.ExtractDependencies(source, "test.cs", symbols);

        Assert.Contains(deps, d => d.SourceSymbol == "Service" && d.TargetSymbol == "Test" && d.DependencyKind == DependencyKind.Uses);
    }
}
