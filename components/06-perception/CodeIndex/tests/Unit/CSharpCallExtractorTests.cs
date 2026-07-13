namespace JoinCode.CodeIndex.Tests;

public sealed class CSharpCallExtractorTests
{
    private readonly CSharpSymbolExtractor _symbolExtractor = new();

    [Fact]
    public void ExtractCalls_DirectMethodCall_ReturnsCallEdge()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    Validate();
                }

                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "Service.Validate" && c.CallKind == CallKind.Direct);
    }

    [Fact]
    public void ExtractCalls_ConstructorCall_ReturnsConstructorKind()
    {
        var source = """
            public class Service
            {
                public void Create()
                {
                    var obj = new Model();
                }
            }

            public class Model { }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CalleeSymbol == "Model" && c.CallKind == CallKind.Constructor);
    }

    [Fact]
    public void ExtractCalls_StaticMethodCall_ReturnsStaticKind()
    {
        var source = """
            public class Helper
            {
                public static void Log() { }
            }

            public class Service
            {
                public void DoWork()
                {
                    Helper.Log();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CalleeSymbol == "Helper.Log" && c.CallKind == CallKind.Static);
    }

    [Fact]
    public void ExtractCalls_MethodChaining_ReturnsMultipleCallEdges()
    {
        var source = """
            public class Builder
            {
                public Builder WithName() { return this; }
                public Builder WithAge() { return this; }
            }

            public class Service
            {
                public void Build()
                {
                    new Builder().WithName().WithAge();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public void ExtractCalls_NoCalls_ReturnsEmptyList()
    {
        var source = """
            public class Service
            {
                public void DoNothing() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Empty(calls);
    }

    [Fact]
    public void ExtractCalls_CallSiteLine_IsCorrect()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    Validate();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        var call = Assert.Single(calls);
        Assert.Equal(5, call.CallSiteLine);
    }

    [Fact]
    public void ExtractCalls_InterfaceMethodCall_ReturnsVirtualKind()
    {
        var source = """
            public interface IRepository
            {
                void Save();
            }

            public class Service
            {
                public void Process(IRepository repo)
                {
                    repo.Save();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CalleeSymbol == "IRepository.Save" && c.CallKind == CallKind.Virtual);
    }

    [Fact]
    public void ExtractCalls_BaseMethodCall_ReturnsVirtualKind()
    {
        var source = """
            public class Base
            {
                public virtual void Execute() { }
            }

            public class Derived : Base
            {
                public override void Execute()
                {
                    base.Execute();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CalleeSymbol == "Base.Execute" && c.CallKind == CallKind.Virtual);
    }

    [Fact]
    public void ExtractCalls_ThisMethodCall_ReturnsVirtualKind()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    this.Validate();
                }

                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CalleeSymbol == "Service.Validate" && c.CallKind == CallKind.Virtual);
    }

    [Fact]
    public void ExtractCalls_LambdaCall_AttributedToEnclosingMethod()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    System.Action action = () => Validate();
                }

                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "Service.Validate");
    }

    [Fact]
    public void ExtractCalls_LocalFunctionCall_AttributedToLocalFunction()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    void Helper()
                    {
                        Validate();
                    }
                    Helper();
                }

                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process.Helper" && c.CalleeSymbol == "Service.Validate");
        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "Service.Process.Helper");
    }

    [Fact]
    public void ExtractCalls_PropertyAccessorCall_AttributedToProperty()
    {
        var source = """
            public class Service
            {
                public int Value
                {
                    get { return Compute(); }
                }

                public int Compute() => 42;
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Value" && c.CalleeSymbol == "Service.Compute");
    }

    [Fact]
    public void ExtractCalls_ConstructorInitializerBase_Detected()
    {
        var source = """
            public class Base { }
            public class Derived : Base
            {
                public Derived() : base() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Derived.ctor" && c.CalleeSymbol == "Base.ctor" && c.CallKind == CallKind.Constructor);
    }

    [Fact]
    public void ExtractCalls_ConstructorInitializerThis_Detected()
    {
        var source = """
            public class Service
            {
                public Service() { }
                public Service(int x) : this() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.ctor" && c.CalleeSymbol == "Service.ctor" && c.CallKind == CallKind.Constructor);
    }

    [Fact]
    public void ExtractCalls_ExtensionMethod_ResolvedToStaticClass()
    {
        var source = """
            public static class StringExtensions
            {
                public static string ToCustom(this string s) => s;
            }

            public class Service
            {
                public void Process()
                {
                    "hello".ToCustom();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "StringExtensions.ToCustom");
    }

    [Fact]
    public void ExtractCalls_NameofExpression_NotDetectedAsCall()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    var name = nameof(Process);
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Empty(calls);
    }

    [Fact]
    public void ExtractCalls_NameofWithMemberAccess_NotDetectedAsCall()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    var name = nameof(Service.Validate);
                }

                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Empty(calls);
    }

    [Fact]
    public void ExtractCalls_EventHandlerSubscription_Detected()
    {
        var source = """
            using System;
            public class Service
            {
                public event EventHandler Changed;
                public void Subscribe()
                {
                    Changed += OnChanged;
                }
                public void OnChanged(object sender, EventArgs e) { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Subscribe" && c.CalleeSymbol == "Service.OnChanged" && c.CallKind == CallKind.EventHandler);
    }

    [Fact]
    public void ExtractCalls_EventHandlerUnsubscription_Detected()
    {
        var source = """
            using System;
            public class Service
            {
                public event EventHandler Changed;
                public void Unsubscribe()
                {
                    Changed -= OnChanged;
                }
                public void OnChanged(object sender, EventArgs e) { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Unsubscribe" && c.CalleeSymbol == "Service.OnChanged" && c.CallKind == CallKind.EventHandler);
    }

    [Fact]
    public void ExtractCalls_RegularAssignment_NotEventHandler()
    {
        var source = """
            public class Service
            {
                public void Process()
                {
                    var x = 42;
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Empty(calls);
    }

    [Fact]
    public void ExtractCalls_LambdaInLinqChain_AttributedToContainingMethod()
    {
        var source = """
            using System.Linq;
            public class Item { public void Process() { } }
            public class Service
            {
                public void Run(List<Item> items)
                {
                    items.Where(x => x.IsValid).Select(x => x.Process());
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Run" && c.CalleeSymbol == "Item.Process");
    }

    [Fact]
    public void ExtractCalls_PropertySetter_CallsAttributedToProperty()
    {
        var source = """
            public class Service
            {
                private string _value;
                public string Value
                {
                    get => _value;
                    set { _value = value; Validate(); }
                }
                public void Validate() { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Value" && c.CalleeSymbol == "Service.Validate");
    }

    [Fact]
    public void ExtractCalls_ExtensionMethodChain_ResolvesAll()
    {
        var source = """
            public static class StringExt
            {
                public static string ToUpper(this string s) => s;
                public static string Trim(this string s) => s;
            }
            public class Service
            {
                public void Process(string input)
                {
                    input.ToUpper().Trim();
                }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "StringExt.ToUpper");
        Assert.Contains(calls, c => c.CallerSymbol == "Service.Process" && c.CalleeSymbol == "StringExt.Trim");
    }

    [Fact]
    public void ExtractCalls_ConstructorChainWithArgs_Detected()
    {
        var source = """
            public class Base
            {
                public Base(int x) { }
            }
            public class Derived : Base
            {
                public Derived(string s) : base(s.Length) { }
            }
            """;

        var symbols = _symbolExtractor.ExtractSymbols(source, "test.cs");
        var extractor = new CSharpCallExtractor();
        var calls = extractor.ExtractCalls(source, "test.cs", symbols);

        Assert.Contains(calls, c => c.CallerSymbol == "Derived.ctor" && c.CalleeSymbol == "Base.ctor" && c.CallKind == CallKind.Constructor);
    }
}
