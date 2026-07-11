using IO.FileSystem;

namespace IO;

/// <summary>
/// GitignoreMatcher 单元测试 — 对齐 .gitignore 规范
/// </summary>
public sealed class GitignoreMatcherTests
{
    [Fact]
    public void Parse_SimpleExtension_IgnoresMatchingFiles()
    {
        var matcher = GitignoreMatcher.Parse("*.log\n*.tmp\n");
        Assert.True(matcher.IsIgnored("debug.log"));
        Assert.True(matcher.IsIgnored("temp.tmp"));
        Assert.False(matcher.IsIgnored("main.cs"));
    }

    [Fact]
    public void Parse_NegationPattern_OverridesIgnore()
    {
        var matcher = GitignoreMatcher.Parse("*.log\n!important.log\n");
        Assert.True(matcher.IsIgnored("debug.log"));
        Assert.False(matcher.IsIgnored("important.log"));
    }

    [Fact]
    public void Parse_DirectoryOnlyPattern_OnlyMatchesDirectories()
    {
        var matcher = GitignoreMatcher.Parse("build/\n");
        Assert.True(matcher.IsIgnored("build", isDirectory: true));
        Assert.False(matcher.IsIgnored("build", isDirectory: false));
    }

    [Fact]
    public void Parse_AnchoredPattern_OnlyMatchesFromRoot()
    {
        var matcher = GitignoreMatcher.Parse("/TODO\n");
        Assert.True(matcher.IsIgnored("TODO"));
        Assert.False(matcher.IsIgnored("src/TODO"));
    }

    [Fact]
    public void Parse_UnanchoredPattern_MatchesAnyLevel()
    {
        var matcher = GitignoreMatcher.Parse("TODO\n");
        Assert.True(matcher.IsIgnored("TODO"));
        Assert.True(matcher.IsIgnored("src/TODO"));
        Assert.True(matcher.IsIgnored("a/b/TODO"));
    }

    [Fact]
    public void Parse_DoubleStar_MatchesMultipleDirectories()
    {
        var matcher = GitignoreMatcher.Parse("**/logs\n");
        Assert.True(matcher.IsIgnored("logs"));
        Assert.True(matcher.IsIgnored("src/logs"));
        Assert.True(matcher.IsIgnored("a/b/logs"));
    }

    [Fact]
    public void Parse_DoubleStarMiddle_MatchesMultipleDirectories()
    {
        var matcher = GitignoreMatcher.Parse("src/**/obj\n");
        Assert.True(matcher.IsIgnored("src/obj"));
        Assert.True(matcher.IsIgnored("src/sub/obj"));
        Assert.True(matcher.IsIgnored("src/a/b/obj"));
        Assert.False(matcher.IsIgnored("obj"));
    }

    [Fact]
    public void Parse_CommentsAndEmptyLines_Ignored()
    {
        var matcher = GitignoreMatcher.Parse("# This is a comment\n\n*.log\n# Another comment\n");
        Assert.True(matcher.IsIgnored("debug.log"));
        Assert.False(matcher.IsIgnored("main.cs"));
    }

    [Fact]
    public void Parse_EscapedHash_TreatedAsLiteral()
    {
        var matcher = GitignoreMatcher.Parse("\\#*#\n");
        Assert.True(matcher.IsIgnored("#test#"));
        Assert.False(matcher.IsIgnored("test"));
    }

    [Fact]
    public void Parse_PathWithSlash_Anchored()
    {
        // 包含 / 的模式锚定到根目录
        var matcher = GitignoreMatcher.Parse("doc/*.txt\n");
        Assert.True(matcher.IsIgnored("doc/readme.txt"));
        Assert.False(matcher.IsIgnored("src/doc/readme.txt"));
    }

    [Fact]
    public void Parse_QuestionMark_MatchesSingleChar()
    {
        var matcher = GitignoreMatcher.Parse("file?.txt\n");
        Assert.True(matcher.IsIgnored("file1.txt"));
        Assert.True(matcher.IsIgnored("fileA.txt"));
        Assert.False(matcher.IsIgnored("file.txt"));
        Assert.False(matcher.IsIgnored("file12.txt"));
    }

    [Fact]
    public void Parse_CharRange_MatchesRange()
    {
        var matcher = GitignoreMatcher.Parse("file[0-9].txt\n");
        Assert.True(matcher.IsIgnored("file1.txt"));
        Assert.True(matcher.IsIgnored("file9.txt"));
        Assert.False(matcher.IsIgnored("fileA.txt"));
    }

    [Fact]
    public void Parse_NegatedCharRange_MatchesComplement()
    {
        var matcher = GitignoreMatcher.Parse("file[^0-9].txt\n");
        Assert.False(matcher.IsIgnored("file1.txt"));
        Assert.True(matcher.IsIgnored("fileA.txt"));
    }

    [Fact]
    public void Parse_ComplexSequence_LaterRulesOverride()
    {
        // 对齐 gitignore 规范：后面的规则覆盖前面的
        var content = """
            /*
            !/src
            /src/*
            !/src/important
            """;
        var matcher = GitignoreMatcher.Parse(content);
        Assert.True(matcher.IsIgnored("build"));
        Assert.False(matcher.IsIgnored("src"));
        Assert.True(matcher.IsIgnored("src/temp"));
        Assert.False(matcher.IsIgnored("src/important"));
    }

    [Fact]
    public void Parse_StarDoesNotMatchSlash()
    {
        // * 不匹配 /
        var matcher = GitignoreMatcher.Parse("*.js\n");
        Assert.True(matcher.IsIgnored("app.js"));
        Assert.True(matcher.IsIgnored("src/app.js"));
        Assert.False(matcher.IsIgnored("src/app.ts"));
    }

    [Fact]
    public void IsIgnored_WindowsPathSeparators_Normalized()
    {
        var matcher = GitignoreMatcher.Parse("*.log\n");
        Assert.True(matcher.IsIgnored("src\\debug.log"));
    }

    [Fact]
    public void FromFile_NonExistentPath_ReturnsNull()
    {
        var matcher = GitignoreMatcher.FromFile("nonexistent/.gitignore", TestFileSystem.Current);
        Assert.Null(matcher);
    }
}
