namespace JoinCode.CodeIndex.Tests;

public sealed class SourceDiffTests
{
    [Fact]
    public void ComputeEdit_SingleLineInsert_ReturnsCorrectEdit()
    {
        var oldSource = "class A { }\nclass B { }";
        var newSource = "class A { }\nclass C { }\nclass B { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(edit.StartIndex >= 0);
        Assert.True(edit.NewEndIndex >= edit.StartIndex);
        Assert.True(edit.NewEndPosition.Row >= edit.StartPosition.Row);
    }

    [Fact]
    public void ComputeEdit_SingleCharacterChange_ReturnsMinimalEdit()
    {
        var oldSource = "class A { }";
        var newSource = "class B { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.Equal(6, edit.StartIndex);
        Assert.Equal(7, edit.OldEndIndex);
        Assert.Equal(7, edit.NewEndIndex);
        Assert.Equal(0, edit.StartPosition.Row);
        Assert.Equal(6, edit.StartPosition.Column);
        Assert.Equal(0, edit.OldEndPosition.Row);
        Assert.Equal(7, edit.OldEndPosition.Column);
        Assert.Equal(0, edit.NewEndPosition.Row);
        Assert.Equal(7, edit.NewEndPosition.Column);
    }

    [Fact]
    public void ComputeEdit_AppendAtEnd_ReturnsCorrectEdit()
    {
        var oldSource = "class A { }";
        var newSource = "class A { }\nclass B { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.Equal(oldSource.Length, edit.StartIndex);
        Assert.Equal(oldSource.Length, edit.OldEndIndex);
        Assert.Equal(newSource.Length, edit.NewEndIndex);
    }

    [Fact]
    public void ComputeEdit_DeleteFromMiddle_ReturnsCorrectEdit()
    {
        var oldSource = "class A { }\nclass B { }\nclass C { }";
        var newSource = "class A { }\nclass C { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(edit.StartIndex < oldSource.Length);
        Assert.True(edit.OldEndIndex > edit.StartIndex);
        Assert.True(edit.NewEndIndex >= edit.StartIndex);
    }

    [Fact]
    public void ComputeEdit_IdenticalSources_ReturnsZeroEdit()
    {
        var source = "class A { }";

        var edit = SourceDiff.ComputeEdit(source, source);

        Assert.Equal(0, edit.StartIndex);
        Assert.Equal(0, edit.OldEndIndex);
        Assert.Equal(0, edit.NewEndIndex);
    }

    [Fact]
    public void ComputeEdit_EmptyOldSource_ReturnsInsertAtStart()
    {
        var newSource = "class A { }";

        var edit = SourceDiff.ComputeEdit(string.Empty, newSource);

        Assert.Equal(0, edit.StartIndex);
        Assert.Equal(0, edit.OldEndIndex);
        Assert.Equal(newSource.Length, edit.NewEndIndex);
        Assert.Equal(0, edit.StartPosition.Row);
        Assert.Equal(0, edit.StartPosition.Column);
    }

    [Fact]
    public void ComputeEdit_MultiLineInsert_PreservesLineNumbers()
    {
        var oldSource = "line1\nline2\nline5";
        var newSource = "line1\nline2\nline3\nline4\nline5";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(edit.NewEndPosition.Row > edit.StartPosition.Row);
    }

    [Fact]
    public void ComputeEdit_MultiLineDelete_PreservesLineNumbers()
    {
        var oldSource = "line1\nline2\nline3\nline4\nline5";
        var newSource = "line1\nline5";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(edit.OldEndPosition.Row > edit.StartPosition.Row);
    }

    [Fact]
    public void ComputeEdit_UnicodeContent_StartIndexIsByteOffset()
    {
        var oldSource = "class 你好 { }";
        var newSource = "class 世界 { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        var expectedStartByte = SourceDiff.CharToByteOffset(oldSource, 6);
        Assert.Equal(expectedStartByte, edit.StartIndex);
    }

    [Fact]
    public void ComputeEdit_UnicodeContent_OldEndIndexIsByteOffset()
    {
        var oldSource = "class 你好 { }";
        var newSource = "class 世界 { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        var expectedOldEndByte = SourceDiff.CharToByteOffset(oldSource, 8);
        Assert.Equal(expectedOldEndByte, edit.OldEndIndex);
    }

    [Fact]
    public void ComputeEdit_UnicodeContent_NewEndIndexIsByteOffset()
    {
        var oldSource = "class 你好 { }";
        var newSource = "class 世界 { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        var expectedNewEndByte = SourceDiff.CharToByteOffset(newSource, 8);
        Assert.Equal(expectedNewEndByte, edit.NewEndIndex);
    }

    [Fact]
    public void ComputeEdit_UnicodeContent_ColumnIsByteOffsetWithinLine()
    {
        var oldSource = "class 你好 { }";
        var newSource = "class 世界 { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        var linePrefix = "class ";
        var expectedColumnBytes = System.Text.Encoding.UTF8.GetByteCount(linePrefix);
        Assert.Equal(expectedColumnBytes, edit.StartPosition.Column);
    }

    [Fact]
    public void ComputeEdit_UnicodeMultiLine_ByteOffsetsCorrect()
    {
        var oldSource = "// 注释\nclass A { }";
        var newSource = "// 备注\nclass A { }";

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        var expectedStartByte = SourceDiff.CharToByteOffset(oldSource, 3);
        Assert.Equal(expectedStartByte, edit.StartIndex);
    }

    [Fact]
    public void ByteOffset_SimpleAscii_ReturnsSameAsCharOffset()
    {
        var text = "hello world";

        var byteOffset = SourceDiff.CharToByteOffset(text, 6);

        Assert.Equal(6, byteOffset);
    }

    [Fact]
    public void ByteOffset_Unicode_ReturnsLargerThanCharOffset()
    {
        var text = "你好世界";

        var byteOffset = SourceDiff.CharToByteOffset(text, 2);

        Assert.True(byteOffset > 2);
    }

    [Fact]
    public void ComputeEdit_RealWorldCSharpEdit_ReturnsCorrectPositions()
    {
        var oldSource = """
            namespace MyApp;

            public class Service
            {
                public void Method1() { }
                public void Method2() { }
            }
            """;

        var newSource = """
            namespace MyApp;

            public class Service
            {
                public void Method1() { }
                public void Method1A() { }
                public void Method2() { }
            }
            """;

        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(edit.StartIndex > 0);
        Assert.True(edit.NewEndIndex > edit.OldEndIndex);
        Assert.True(edit.NewEndPosition.Row >= 3);
    }
}
