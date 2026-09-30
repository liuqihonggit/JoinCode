namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 父文档检索切块测试 — 验证 CollectChunksWithParents 建立的父子层级关系。
/// </summary>
public sealed class ParentDocumentExtractionTests {
    private readonly CSharpSymbolExtractor _extractor = new();

    [Fact]
    public void ClassChunk_HasNullParentChunkId() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        Assert.Null(classChunk.ParentChunkId);
    }

    [Fact]
    public void MethodChunk_ParentChunkId_PointsToClass() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        var methodChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Method);
        Assert.NotNull(methodChunk.ParentChunkId);
        Assert.Equal(classChunk.ChunkId, methodChunk.ParentChunkId);
    }

    [Fact]
    public void ParentDocuments_ContainsClassDocument_WithSourceText() {
        var source = """
            public class Foo {
                public void Bar() { }
            }
            """;

        var result = _extractor.ExtractAll(source, "test.cs");

        var classChunk = Assert.Single(result.Chunks, c => c.Kind == SymbolKind.Class);
        var classDoc = Assert.Single(result.ParentDocuments, d => d.ChunkId == classChunk.ChunkId);
        Assert.Contains("Foo", classDoc.SourceText);
        Assert.Equal(classChunk.StartLine, classDoc.StartLine);
        Assert.Equal(classChunk.EndLine, classDoc.EndLine);
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
            Assert.Equal(classChunk.ChunkId, child.ParentChunkId);
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
        Assert.Equal(innerClass.ChunkId, method.ParentChunkId);
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
        Assert.Null(ifaceChunk.ParentChunkId);
        Assert.Contains(result.ParentDocuments, d => d.ChunkId == ifaceChunk.ChunkId);
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
        Assert.Null(structChunk.ParentChunkId);
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
        Assert.Null(enumChunk.ParentChunkId);
    }

    [Fact]
    public void ParentDocuments_Empty_WhenNoSymbols() {
        var result = _extractor.ExtractAll("", "empty.cs");

        Assert.Empty(result.ParentDocuments);
    }

    [Fact]
    public void ParentDocuments_ContainsAllClassLevelSymbols() {
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
            Assert.Contains(result.ParentDocuments, d => d.ChunkId == chunk.ChunkId);
        }
    }
}
