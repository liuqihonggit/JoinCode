namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 父文档检索切块测试 — 验证 CollectChunksWithParents 建立的父子层级关系（定位信息写入 ChunkInfo）。
/// </summary>
public sealed class ParentDocumentExtractionTests {
    private readonly CSharpSymbolExtractor _extractor = new();

    [Fact]
    public void ClassChunk_HasNullParentFilePath() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        Assert.Null(classChunk.ParentFilePath);
    }

    [Fact]
    public void MethodChunk_ParentFilePath_PointsToClass() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        var methodChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Method);
        Assert.NotNull(methodChunk.ParentFilePath);
        Assert.Equal("test.cs", methodChunk.ParentFilePath);
        Assert.Equal(classChunk.StartLine, methodChunk.ParentStartLine);
        Assert.Equal(classChunk.EndLine, methodChunk.ParentEndLine);
        Assert.Contains("Foo", methodChunk.ParentSymbolFqn);
    }

    [Fact]
    public void MultipleMethods_AllPointToSameClass() {
        var source = """
            public class Foo {
                public void Bar() { }
                public void Baz() { }
                public int Qux { get; set; }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        var childChunks = result.Chunks.Where(c => c.Kind != SymbolKind.Class).ToList();
        Assert.True(childChunks.Count >= 3);
        foreach (var child in childChunks) {
            Assert.Equal("test.cs", child.ParentFilePath);
            Assert.Equal(classChunk.StartLine, child.ParentStartLine);
            Assert.Equal(classChunk.EndLine, child.ParentEndLine);
        }
    }

    [Fact]
    public void NestedClass_MethodPointsToNestedClass() {
        var source = """
            public class Outer {
                public class Inner {
                    public void Method() { }
                }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var innerClass = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class && c.SymbolFqn.Contains("Inner"));
        var method = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Method);
        Assert.Equal("test.cs", method.ParentFilePath);
        Assert.Equal(innerClass.StartLine, method.ParentStartLine);
        Assert.Equal(innerClass.EndLine, method.ParentEndLine);
    }

    [Fact]
    public void Interface_IsParentDocument() {
        var source = """
            public interface IFoo {
                void Bar();
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var ifaceChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Interface);
        Assert.Null(ifaceChunk.ParentFilePath);
    }

    [Fact]
    public void Struct_IsParentDocument() {
        var source = """
            public struct Point {
                public int X;
                public int Y;
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var structChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Struct);
        Assert.Null(structChunk.ParentFilePath);
    }

    [Fact]
    public void Enum_IsParentDocument() {
        var source = """
            public enum Color {
                Red,
                Green,
                Blue
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var enumChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Enum);
        Assert.Null(enumChunk.ParentFilePath);
    }

    [Fact]
    public void NoChunks_WhenNoSymbols() {
        var result = _extractor.ExtractAll("", "empty.cs");
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public void ClassLevelSymbols_HaveNullParentFilePath() {
        var source = """
            public class Foo { }
            public class Bar { }
            public interface IBaz { }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classLevelChunks = result.Chunks.Where(c =>
            c.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Struct
                or SymbolKind.Record or SymbolKind.Enum).ToList();
        Assert.True(classLevelChunks.Count >= 3);
        foreach (var chunk in classLevelChunks) {
            Assert.Null(chunk.ParentFilePath);
        }
    }
}
