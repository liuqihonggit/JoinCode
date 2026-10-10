
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Host.Tests.Cli;
public sealed class RgSubCommandTests {
    [Theory]
    [InlineData(@"finally\s*\{", @"finally\s*\{")]
    [InlineData(@"finally\\s*\\{", @"finally\s*\{")]
    [InlineData(@"\d+\w+", @"\d+\w+")]
    [InlineData(@"\\d+\\w+", @"\d+\w+")]
    [InlineData(@"class\s+\w+Service", @"class\s+\w+Service")]
    [InlineData(@"class\\s+\\w+Service", @"class\s+\w+Service")]
    [InlineData(@"^\s*finally", @"^\s*finally")]
    [InlineData(@"^\\s*finally", @"^\s*finally")]
    [InlineData(@"foo", "foo")]
    [InlineData("", "")]
    [InlineData("no backslash", "no backslash")]
    public void FixPowerShellEscaping_ShouldNormalizeDoubleBackslash(string input, string expected) {
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(expected);
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldHandleMixedEscaping() {
        var input = @"finally\\s*\{\\d";
        var result = RgSubCommand.FixPowerShellEscaping(input);
        result.Should().Be(@"finally\s*\{\d");
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldNotTouchSingleBackslashBeforeNonMeta() {
        var input = @"foo\bar";
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(@"foo\bar");
    }

    [Fact]
    public void FixPowerShellEscaping_ShouldNotTouchTrailingDoubleBackslash() {
        var input = @"foo\\";
        RgSubCommand.FixPowerShellEscaping(input).Should().Be(@"foo\\");
    }

    [Theory]
    [InlineData("C:\\", true)]
    [InlineData("C:/", true)]
    [InlineData("D:\\", true)]
    [InlineData("/", true)]
    [InlineData("C:\\project", false)]
    [InlineData("C:\\project\\w3", false)]
    [InlineData("/home/user", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("src", false)]
    public void IsRootPath_ShouldDetectRootPaths(string path, bool expected) {
        RgSubCommand.IsRootPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData('s', true)]
    [InlineData('d', true)]
    [InlineData('w', true)]
    [InlineData('{', true)]
    [InlineData('}', true)]
    [InlineData('.', true)]
    [InlineData('+', true)]
    [InlineData('*', true)]
    [InlineData('?', true)]
    [InlineData('|', true)]
    [InlineData('^', true)]
    [InlineData('$', true)]
    [InlineData('n', true)]
    [InlineData('r', true)]
    [InlineData('t', true)]
    [InlineData('a', false)]
    [InlineData('z', true)]
    [InlineData('x', true)]
    [InlineData('p', true)]
    [InlineData('P', true)]
    [InlineData('b', true)]
    [InlineData('B', true)]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData('G', true)]
    [InlineData('k', true)]
    [InlineData('u', true)]
    [InlineData('c', true)]
    [InlineData('f', true)]
    [InlineData('v', true)]
    [InlineData('0', true)]
    [InlineData('[', true)]
    [InlineData(']', true)]
    [InlineData('(', true)]
    [InlineData(')', true)]
    [InlineData('m', false)]
    [InlineData('q', false)]
    [InlineData('y', false)]
    public void IsRegexMetaChar_ShouldClassifyCorrectly(char c, bool expected) {
        RgSubCommand.IsRegexMetaChar(c).Should().Be(expected);
    }

    [Fact]
    public void ParseArgs_SmartCase_ShouldSetSmartCaseFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-S"]);
        opts.Should().NotBeNull();
        opts!.SmartCase.Should().BeTrue();
        opts.CaseInsensitive.Should().BeFalse();
    }

    [Fact]
    public void ParseArgs_SmartCaseLongForm_ShouldSetSmartCaseFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--smart-case"]);
        opts.Should().NotBeNull();
        opts!.SmartCase.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WordRegexp_ShouldSetWordRegexpFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-w"]);
        opts.Should().NotBeNull();
        opts!.WordRegexp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_WordRegexpLongForm_ShouldSetWordRegexpFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--word-regexp"]);
        opts.Should().NotBeNull();
        opts!.WordRegexp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_OnlyMatching_ShouldSetOnlyMatchingFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-o"]);
        opts.Should().NotBeNull();
        opts!.OnlyMatching.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_OnlyMatchingLongForm_ShouldSetOnlyMatchingFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--only-matching"]);
        opts.Should().NotBeNull();
        opts!.OnlyMatching.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_Replace_ShouldSetReplaceValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-r", "REPLACEMENT"]);
        opts.Should().NotBeNull();
        opts!.Replace.Should().Be("REPLACEMENT");
    }

    [Fact]
    public void ParseArgs_ReplaceLongForm_ShouldSetReplaceValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--replace", "XYZ"]);
        opts.Should().NotBeNull();
        opts!.Replace.Should().Be("XYZ");
    }

    [Fact]
    public void ParseArgs_Sort_ShouldSetSortMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--sort", "path"]);
        opts.Should().NotBeNull();
        opts!.Sort.Should().Be("path");
    }

    [Fact]
    public void ParseArgs_SortModified_ShouldSetSortMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--sort", "modified"]);
        opts.Should().NotBeNull();
        opts!.Sort.Should().Be("modified");
    }

    [Fact]
    public void ParseArgs_Hidden_ShouldSetHiddenFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--hidden"]);
        opts.Should().NotBeNull();
        opts!.Hidden.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_NoIgnore_ShouldSetNoIgnoreFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--no-ignore"]);
        opts.Should().NotBeNull();
        opts!.NoIgnore.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_MultiplePaths_ShouldCollectAllPaths() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "app/", "core/"]);
        opts.Should().NotBeNull();
        opts!.Paths.Should().HaveCount(3);
        opts.Paths.Should().ContainInOrder("src/", "app/", "core/");
    }

    [Fact]
    public void ParseArgs_CombinedShortOptions_ShouldParseAllFlags() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-inSwo"]);
        opts.Should().NotBeNull();
        opts!.CaseInsensitive.Should().BeTrue();
        opts.LineNumbers.Should().BeTrue();
        opts.SmartCase.Should().BeTrue();
        opts.WordRegexp.Should().BeTrue();
        opts.OnlyMatching.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_Timeout_ShouldClampToMax() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--timeout", "999"]);
        opts.Should().NotBeNull();
        opts!.TimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void ParseArgs_TimeoutZero_ShouldUseDefault() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--timeout", "0"]);
        opts.Should().NotBeNull();
        opts!.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void ParseArgs_HeadLimitZero_ShouldAllowUnlimited() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--head-limit", "0"]);
        opts.Should().NotBeNull();
        opts!.HeadLimit.Should().Be(0);
    }

    [Fact]
    public void ParseArgs_Offset_ShouldSetOffsetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--offset", "10"]);
        opts.Should().NotBeNull();
        opts!.Offset.Should().Be(10);
    }

    [Fact]
    public void ParseArgs_MissingPath_ShouldReturnNull() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern"]);
        opts.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_MissingPattern_ShouldReturnNull() {
        var opts = RgSubCommand.ParseArgs(["src/"]);
        opts.Should().BeNull();
    }

    [Fact]
    public void ParseArgs_DefaultTimeout_ShouldBe30Seconds() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/"]);
        opts.Should().NotBeNull();
        opts!.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void ParseArgs_MultipleGlobs_ShouldCollectAllGlobs() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--glob", "*.cs", "--glob", "!**/tests/**"]);
        opts.Should().NotBeNull();
        opts!.Globs.Should().HaveCount(2);
        opts.Globs.Should().ContainInOrder("*.cs", "!**/tests/**");
    }

    [Fact]
    public void ParseArgs_MultipleShortGlobs_ShouldCollectAllGlobs() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-g", "*.cs", "-g", "!**/obj/**"]);
        opts.Should().NotBeNull();
        opts!.Globs.Should().HaveCount(2);
        opts.Globs.Should().ContainInOrder("*.cs", "!**/obj/**");
    }

    [Fact]
    public void ParseArgs_NoGlob_ShouldReturnEmptyGlobsList() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/"]);
        opts.Should().NotBeNull();
        opts!.Globs.Should().BeEmpty();
    }

    [Fact]
    public void ParseArgs_MultilineDotall_ShouldSetMultilineFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--multiline-dotall"]);
        opts.Should().NotBeNull();
        opts!.Multiline.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_DefaultOutputMode_ShouldBeContent() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Content);
    }

    [Fact]
    public void ParseArgs_InvertMatchShort_ShouldSetFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-v"]);
        opts.Should().NotBeNull();
        opts!.InvertMatch.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_InvertMatchLong_ShouldSetFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--invert-match"]);
        opts.Should().NotBeNull();
        opts!.InvertMatch.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_LineRegexp_ShouldWrapPatternWithAnchors() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-x"]);
        opts.Should().NotBeNull();
        opts!.Pattern.Should().Be("^(?:pattern)$");
        opts.LineRegexp.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_MaxCountShort_ShouldSetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-m", "5"]);
        opts.Should().NotBeNull();
        opts!.MaxCount.Should().Be(5);
    }

    [Fact]
    public void ParseArgs_MaxCountLong_ShouldSetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--max-count", "10"]);
        opts.Should().NotBeNull();
        opts!.MaxCount.Should().Be(10);
    }

    [Fact]
    public void ParseArgs_ThreadsShort_ShouldSetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-j", "4"]);
        opts.Should().NotBeNull();
        opts!.Threads.Should().Be(4);
    }

    [Fact]
    public void ParseArgs_ThreadsLong_ShouldSetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--threads", "8"]);
        opts.Should().NotBeNull();
        opts!.Threads.Should().Be(8);
    }

    [Fact]
    public void ParseArgs_MaxFilesizePlain_ShouldSetValue() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--max-filesize", "1000"]);
        opts.Should().NotBeNull();
        opts!.MaxFilesize.Should().Be(1000L);
    }

    [Fact]
    public void ParseArgs_MaxFilesizeKSuffix_ShouldMultiplyBy1024() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--max-filesize", "1K"]);
        opts.Should().NotBeNull();
        opts!.MaxFilesize.Should().Be(1024L);
    }

    [Fact]
    public void ParseArgs_MaxFilesizeMSuffix_ShouldMultiplyBy1M() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--max-filesize", "1M"]);
        opts.Should().NotBeNull();
        opts!.MaxFilesize.Should().Be(1024L * 1024);
    }

    [Fact]
    public void ParseArgs_MaxFilesizeGSuffix_ShouldMultiplyBy1G() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "--max-filesize", "1G"]);
        opts.Should().NotBeNull();
        opts!.MaxFilesize.Should().Be(1024L * 1024 * 1024);
    }

    [Fact]
    public void ParseArgs_BareContent_ShouldSetContentMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "content"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Content);
    }

    [Fact]
    public void ParseArgs_BareCount_ShouldSetCountMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "count"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Count);
    }

    [Fact]
    public void ParseArgs_BareFilesWithMatches_ShouldSetFilesMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "files-with-matches"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Files);
    }

    [Fact]
    public void ParseArgs_BareJson_ShouldSetJsonFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "json"]);
        opts.Should().NotBeNull();
        opts!.Json.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_BareHidden_ShouldSetHiddenFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "hidden"]);
        opts.Should().NotBeNull();
        opts!.Hidden.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_BareNoIgnore_ShouldSetNoIgnoreFlag() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "no-ignore"]);
        opts.Should().NotBeNull();
        opts!.NoIgnore.Should().BeTrue();
    }

    [Fact]
    public void ParseArgs_MultipleRegexp_ShouldMergeAsOrPattern() {
        var opts = RgSubCommand.ParseArgs(["rg", "-e", "foo", "-e", "bar", "src/"]);
        opts.Should().NotBeNull();
        opts!.Pattern.Should().Be("foo|bar");
        opts.Paths.Should().ContainSingle().Which.Should().Be("src/");
    }

    [Fact]
    public void ParseArgs_RegexpWithPositionalPattern_ShouldTreatPositionalAsPaths() {
        var opts = RgSubCommand.ParseArgs(["rg", "-e", "foo", "bar", "src/"]);
        opts.Should().NotBeNull();
        opts!.Pattern.Should().Be("foo");
        opts.Paths.Should().HaveCount(2);
        opts.Paths.Should().ContainInOrder("bar", "src/");
    }

    [Fact]
    public void ParseArgs_FilesWithMatchesExplicit_ShouldSetFilesMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-l"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Files);
    }

    [Fact]
    public void ParseArgs_CountExplicit_ShouldSetCountMode() {
        var opts = RgSubCommand.ParseArgs(["rg", "pattern", "src/", "-c"]);
        opts.Should().NotBeNull();
        opts!.OutputMode.Should().Be(SearchOutputMode.Count);
    }
}