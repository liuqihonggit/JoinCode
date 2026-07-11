namespace CodeIndex.Tests;

public sealed class CSharpSymbolExtractorTests
{
    private readonly CSharpSymbolExtractor _extractor = new();

    [Fact]
    public void ExtractSymbols_ClassDeclaration_ReturnsClassSymbol()
    {
        var source = """
            public class UserService
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        Assert.NotEmpty(symbols);
        var classSymbol = Assert.Single(symbols, s => s.Kind == SymbolKind.Class);
        Assert.Equal("UserService", classSymbol.Name);
        Assert.Equal("public", classSymbol.Accessibility);
    }

    [Fact]
    public void ExtractSymbols_InterfaceDeclaration_ReturnsInterfaceSymbol()
    {
        var source = """
            public interface IUserRepository
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var iface = Assert.Single(symbols, s => s.Kind == SymbolKind.Interface);
        Assert.Equal("IUserRepository", iface.Name);
    }

    [Fact]
    public void ExtractSymbols_StructDeclaration_ReturnsStructSymbol()
    {
        var source = """
            public struct Point
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var structSymbol = Assert.Single(symbols, s => s.Kind == SymbolKind.Struct);
        Assert.Equal("Point", structSymbol.Name);
    }

    [Fact]
    public void ExtractSymbols_EnumDeclaration_ReturnsEnumSymbol()
    {
        var source = """
            public enum Color
            {
                Red, Green, Blue
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var enumSymbol = Assert.Single(symbols, s => s.Kind == SymbolKind.Enum);
        Assert.Equal("Color", enumSymbol.Name);
    }

    [Fact]
    public void ExtractSymbols_MethodDeclaration_ReturnsMethodSymbol()
    {
        var source = """
            public class Service
            {
                public void DoWork() { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var method = Assert.Single(symbols, s => s.Kind == SymbolKind.Method);
        Assert.Equal("DoWork", method.Name);
        Assert.Equal("Service", method.ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_PropertyDeclaration_ReturnsPropertySymbol()
    {
        var source = """
            public class Model
            {
                public string Name { get; set; }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var prop = Assert.Single(symbols, s => s.Kind == SymbolKind.Property);
        Assert.Equal("Name", prop.Name);
    }

    [Fact]
    public void ExtractSymbols_ConstructorDeclaration_ReturnsConstructorSymbol()
    {
        var source = """
            public class Service
            {
                public Service() { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ctor = Assert.Single(symbols, s => s.Kind == SymbolKind.Constructor);
        Assert.Equal("ctor", ctor.Name);
        Assert.Equal("Service.ctor", ctor.FullyQualifiedName);
    }

    [Fact]
    public void ExtractSymbols_FieldDeclaration_ReturnsFieldSymbol()
    {
        var source = """
            public class Service
            {
                private int _count;
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var field = Assert.Single(symbols, s => s.Kind == SymbolKind.Field);
        Assert.Equal("_count", field.Name);
    }

    [Fact]
    public void ExtractSymbols_ConstField_ReturnsConstantSymbol()
    {
        var source = """
            public class Config
            {
                public const int MaxRetries = 3;
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var constant = Assert.Single(symbols, s => s.Kind == SymbolKind.Constant);
        Assert.Equal("MaxRetries", constant.Name);
    }

    [Fact]
    public void ExtractSymbols_NestedClass_SetsParentSymbol()
    {
        var source = """
            public class Outer
            {
                public class Inner { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var inner = Assert.Single(symbols, s => s.Name == "Inner");
        Assert.Equal("Outer", inner.ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_Namespace_SetsNamespaceContext()
    {
        var source = """
            namespace MyApp.Services
            {
                public class UserService { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ns = Assert.Single(symbols, s => s.Kind == SymbolKind.Namespace);
        Assert.Equal("MyApp.Services", ns.Name);

        var cls = Assert.Single(symbols, s => s.Kind == SymbolKind.Class);
        Assert.Equal("MyApp.Services", cls.Namespace);
    }

    [Fact]
    public void ExtractSymbols_DelegateDeclaration_ReturnsDelegateSymbol()
    {
        var source = """
            public delegate void NotifyHandler(string message);
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var del = Assert.Single(symbols, s => s.Kind == SymbolKind.Delegate);
        Assert.Equal("NotifyHandler", del.Name);
    }

    [Fact]
    public void ExtractSymbols_MultipleFields_ReturnsAllFieldSymbols()
    {
        var source = """
            public class Data
            {
                private int _x, _y;
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var fields = symbols.Where(s => s.Kind == SymbolKind.Field).ToList();
        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "_x");
        Assert.Contains(fields, f => f.Name == "_y");
    }

    [Fact]
    public void ExtractSymbols_EmptySource_ReturnsEmptyList()
    {
        var symbols = _extractor.ExtractSymbols("", "empty.cs");
        Assert.Empty(symbols);
    }

    [Fact]
    public void LanguageId_ReturnsCSharp()
    {
        Assert.Equal("c-sharp", _extractor.LanguageId);
    }

    [Fact]
    public void FileExtensions_ReturnsCs()
    {
        Assert.Equal([".cs"], _extractor.FileExtensions);
    }

    [Fact]
    public void ExtractSymbols_RecordDeclaration_ReturnsRecordSymbol()
    {
        var source = """
            public record Person(string Name, int Age);
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var record = Assert.Single(symbols, s => s.Kind == SymbolKind.Record);
        Assert.Equal("Person", record.Name);
    }

    [Fact]
    public void ExtractSymbols_RecordStructDeclaration_ReturnsRecordStructSymbol()
    {
        var source = """
            public record struct Point(double X, double Y);
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var rs = Assert.Single(symbols, s => s.Kind == SymbolKind.RecordStruct);
        Assert.Equal("Point", rs.Name);
    }

    [Fact]
    public void ExtractSymbols_OperatorDeclaration_ReturnsOperatorSymbol()
    {
        var source = """
            public class Vector
            {
                public static Vector operator +(Vector a, Vector b) { return a; }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var op = Assert.Single(symbols, s => s.Kind == SymbolKind.Operator);
        Assert.Equal("op_+", op.Name);
        Assert.Equal("Vector", op.ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_IndexerDeclaration_ReturnsIndexerSymbol()
    {
        var source = """
            public class Container
            {
                public int this[int index] => 0;
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var indexer = Assert.Single(symbols, s => s.Kind == SymbolKind.Indexer);
        Assert.Equal("this", indexer.Name);
    }

    [Fact]
    public void ExtractSymbols_DestructorDeclaration_ReturnsDestructorSymbol()
    {
        var source = """
            public class Resource
            {
                ~Resource() { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var dtor = Assert.Single(symbols, s => s.Kind == SymbolKind.Destructor);
        Assert.Equal("Finalize", dtor.Name);
    }

    [Fact]
    public void ExtractSymbols_LocalFunction_ReturnsLocalFunctionSymbol()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    void Helper() { }
                }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var local = Assert.Single(symbols, s => s.Kind == SymbolKind.LocalFunction);
        Assert.Equal("Helper", local.Name);
        Assert.Equal("Process", local.ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_FileScopedNamespace_ReturnsNamespaceSymbol()
    {
        var source = """
            namespace MyApp;

            public class Service { }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ns = Assert.Single(symbols, s => s.Kind == SymbolKind.Namespace);
        Assert.Equal("MyApp", ns.Name);

        var cls = Assert.Single(symbols, s => s.Kind == SymbolKind.Class);
        Assert.Equal("MyApp", cls.Namespace);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_ReturnsConstructorSymbol()
    {
        var source = """
            public class Person(string name)
            {
                public string Name => name;
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ctors = symbols.Where(s => s.Kind == SymbolKind.Constructor).ToList();
        Assert.Single(ctors);
        Assert.Equal("ctor", ctors[0].Name);
        Assert.Equal("Person.ctor", ctors[0].FullyQualifiedName);
        Assert.Equal("Person", ctors[0].ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_Record_ExtractsCtorSymbol()
    {
        var source = "public record Person(string Name, int Age);";

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var record = Assert.Single(symbols, s => s.Kind == SymbolKind.Record);
        Assert.Equal("Person", record.Name);

        var ctor = Assert.Single(symbols, s => s.Kind == SymbolKind.Constructor);
        Assert.Equal("ctor", ctor.Name);
        Assert.Equal("Person.ctor", ctor.FullyQualifiedName);
        Assert.Equal("Person", ctor.ParentSymbol);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_RecordStruct_ExtractsCtorSymbol()
    {
        var source = "public record struct Point(double X, double Y);";

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var rs = Assert.Single(symbols, s => s.Kind == SymbolKind.RecordStruct);
        Assert.Equal("Point", rs.Name);

        var ctor = Assert.Single(symbols, s => s.Kind == SymbolKind.Constructor);
        Assert.Equal("ctor", ctor.Name);
        Assert.Equal("Point.ctor", ctor.FullyQualifiedName);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_Struct_ExtractsCtorSymbol()
    {
        var source = """
            public struct Value(int count)
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var st = Assert.Single(symbols, s => s.Kind == SymbolKind.Struct);
        Assert.Equal("Value", st.Name);

        var ctor = Assert.Single(symbols, s => s.Kind == SymbolKind.Constructor);
        Assert.Equal("ctor", ctor.Name);
        Assert.Equal("Value.ctor", ctor.FullyQualifiedName);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_WithBaseInit_ExtractsCtorSymbol()
    {
        var source = """
            public class Derived(int x) : Base(x)
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ctor = Assert.Single(symbols, s => s.Kind == SymbolKind.Constructor);
        Assert.Equal("Derived.ctor", ctor.FullyQualifiedName);
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_WithRegularCtor_ExtractsBoth()
    {
        var source = """
            public class Service(int x)
            {
                public Service(string name) : this(name.Length) { }
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var ctors = symbols.Where(s => s.Kind == SymbolKind.Constructor).ToList();
        Assert.Equal(2, ctors.Count);
        Assert.All(ctors, c => Assert.Equal("ctor", c.Name));
        Assert.All(ctors, c => Assert.Equal("Service.ctor", c.FullyQualifiedName));
    }

    [Fact]
    public void ExtractSymbols_PrimaryConstructor_RecordNoParams_NoCtorSymbol()
    {
        var source = """
            public record Empty
            {
            }
            """;

        var symbols = _extractor.ExtractSymbols(source, "test.cs");

        var record = Assert.Single(symbols, s => s.Kind == SymbolKind.Record);
        Assert.Equal("Empty", record.Name);
        Assert.DoesNotContain(symbols, s => s.Kind == SymbolKind.Constructor);
    }
}
