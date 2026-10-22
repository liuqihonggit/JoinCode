namespace NativeTui.Tests.Repl;

/// <summary>
/// rg 搜索执行器纯函数面测试 — 参数序列（BuildArguments 固定序）/ 输出行解析
///（ParseRipgrepLine 懒惰匹配含 Windows 盘符与冒号内嵌形态）/ 路径归一（RelativizePath
/// 根内外分支）/ 可执行缺失降级（IsAvailable=false + 空流，无真 rg 依赖）。
/// 真 rg 进程集成见 GlobalSearchRipgrepIntegrationTests（Trait Category=Integration）。
/// </summary>
public sealed class GlobalSearchRipgrepTests
{
    /// <summary>参数序列 — 任务书面（--line-number --no-heading --smart-case --max-count 50）
    /// + 基准 -F -e 字面量语义 + 排除目录否定 glob 序；查询原样透传（非正则）。</summary>
    [Fact]
    public void BuildArguments_ProducesFixedSequence()
    {
        var arguments = RipgrepSearcher.BuildArguments("Needle");

        string[] expected =
        [
            "--line-number", "--no-heading", "--smart-case", "--max-count", "50",
            "--fixed-strings", "--regexp", "Needle",
            "--glob", "!.git", "--glob", "!node_modules", "--glob", "!bin", "--glob", "!obj", "--glob", "!artifacts",
        ];
        Assert.Equal(expected, arguments);
    }

    /// <summary>参数校验 — 空/空白查询拒绝（rg -e 需非空模式）。</summary>
    [Fact]
    public void BuildArguments_RejectsBlankQuery() =>
        Assert.Throws<ArgumentException>(() => RipgrepSearcher.BuildArguments("   "));

    /// <summary>有效解析 — 常规路径 / Windows 盘符（首个冒号后非数字回溯）/ 冒号内嵌文本 / 空文本（空行命中）。</summary>
    [Theory]
    [InlineData("src/app.ts:42:const x = 1", "src/app.ts", 42, "const x = 1")]
    [InlineData("D:\\proj\\a.cs:10:文本 行", "D:\\proj\\a.cs", 10, "文本 行")]
    [InlineData("a:1:b:c", "a", 1, "b:c")]
    [InlineData("a:12:x", "a", 12, "x")]
    [InlineData("a.cs:5:", "a.cs", 5, "")]
    public void ParseRipgrepLine_Valid(string line, string file, int lineNumber, string text)
    {
        var match = RipgrepSearcher.ParseRipgrepLine(line);

        Assert.NotNull(match);
        Assert.Equal(new SearchMatch(file, lineNumber, text), match);
    }

    /// <summary>无效解析 — 空行 / 无行号段 / 行号非数字 / 文件段空（基准 !file 语义）。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("纯文本无行号")]
    [InlineData("a.cs:xx:t")]
    [InlineData(":1:t")]
    [InlineData("no:trailing:colon")]
    public void ParseRipgrepLine_Invalid_ReturnsNull(string line) =>
        Assert.Null(RipgrepSearcher.ParseRipgrepLine(line));

    /// <summary>路径归一 — 根内转相对（对话框紧凑显示）/ 越出根保留原样（基准 startsWith('..') 分支）。</summary>
    [Fact]
    public void RelativizePath_InsideRootRelative_OutsideKeepsOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gs_rel_{Guid.NewGuid():N}");
        var inside = Path.Combine(root, "src", "a.cs");
        Assert.Equal(Path.Combine("src", "a.cs"), RipgrepSearcher.RelativizePath(inside, root));

        var outside = Path.Combine(Path.GetTempPath(), "other_root", "a.cs");
        Assert.Equal(outside, RipgrepSearcher.RelativizePath(outside, root));
    }

    /// <summary>可执行缺失降级 — IsAvailable=false（探测进程启动失败缓存）+ ExecuteAsync 空流
    ///（无异常传播，调用方拿空序列；降级判定不依赖真 rg 存在）。</summary>
    [Fact]
    public async Task MissingExecutable_DegradesToUnavailable_EmptyStream()
    {
        var searcher = new RipgrepSearcher("rg_missing_executable_no_such");

        Assert.False(searcher.IsAvailable);

        var results = new List<SearchMatch>();
        await foreach (var match in searcher.ExecuteAsync(Path.GetTempPath(), "x"))
        {
            results.Add(match);
        }

        Assert.Empty(results);
    }

    /// <summary>参数即时校验 — 迭代器壳形态保证空根/空查询在调用即抛（非首次枚举延迟抛）。</summary>
    [Fact]
    public void ExecuteAsync_RejectsBlankArgumentsImmediately()
    {
        var searcher = new RipgrepSearcher();

        Assert.Throws<ArgumentException>(() => searcher.ExecuteAsync("   ", "q"));
        Assert.Throws<ArgumentException>(() => searcher.ExecuteAsync(Path.GetTempPath(), "   "));
    }
}
