[assembly: SuppressMessage("FileSystemAbstraction", "JCC9001", Scope = "type", Target = "Host.Tests.Cli.RgEngineAsciiDecodeTests", Justification = "RgEngine 搜索测试需直接访问文件系统验证真实文件的 ASCII 解码行为")]
namespace Host.Tests.Cli;

/// <summary>
/// RgEngine ASCII 解码 bug 修复测试（改动 2）。
/// <para>之前用 MemoryMarshal.Cast&lt;byte,char&gt; 把 2 字节当 1 char，损坏 ASCII 内容。
/// 现在统一用 Encoding.UTF8.GetString 解码（ASCII 是 UTF-8 子集）。</para>
/// </summary>
public sealed class RgEngineAsciiDecodeTests {
    /// <summary>
    /// 纯 ASCII 文件内容能被正确搜索命中（之前 MemoryMarshal.Cast 会乱码搜不到）。
    /// </summary>
    [Fact]
    public async Task 纯Ascii文件_搜索Ascii关键词_应命中() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_ascii_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "sample.cs");
            await File.WriteAllTextAsync(filePath, "public class Foo { }\npublic class Bar { }\n").ConfigureAwait(true);

            var query = BuildQuery("Foo", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(1);
            outcome.Results.Should().HaveCount(1);
            outcome.Results[0].MatchCount.Should().Be(1);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 纯 ASCII 文件搜索多行内容，每行都能正确解码命中。
    /// </summary>
    [Fact]
    public async Task 纯Ascii多行文件_搜索公共关键词_应命中所有匹配行() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_ascii_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "multi.cs");
            var content = "public class Alpha { }\npublic class Beta { }\npublic class Gamma { }\n";
            await File.WriteAllTextAsync(filePath, content).ConfigureAwait(true);

            var query = BuildQuery("public class", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(3);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// ASCII 文件包含特殊字符（标点、数字、下划线）时能正确搜索。
    /// </summary>
    [Fact]
    public async Task Ascii含特殊字符_搜索应正确解码() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_ascii_special_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "special.cs");
            await File.WriteAllTextAsync(filePath, "int x_123 = 456; // TODO: fix_this\n").ConfigureAwait(true);

            var query = BuildQuery("x_123", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(1);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// UTF-8 多字节内容（中文）文件能正确搜索 ASCII 关键词。
    /// </summary>
    [Fact]
    public async Task Utf8中文文件_搜索Ascii关键词_应命中() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_utf8_cn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "chinese.cs");
            await File.WriteAllTextAsync(filePath, "// 中文注释\npublic class Foo { }\n// 更多中文\n").ConfigureAwait(true);

            var query = BuildQuery("Foo", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(1);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 带 UTF-8 BOM 的 ASCII 文件能正确搜索（BOM 被跳过后解码）。
    /// </summary>
    [Fact]
    public async Task 带Bom的Ascii文件_搜索关键词_应命中() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_bom_ascii_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "bom.cs");
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var contentBytes = System.Text.Encoding.UTF8.GetBytes("public class BomClass { }\n");
            await File.WriteAllBytesAsync(filePath, [.. bom, .. contentBytes]).ConfigureAwait(true);

            var query = BuildQuery("BomClass", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(1);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 空文件应被跳过（不崩溃，返回空结果）。
    /// </summary>
    [Fact]
    public async Task 空文件_搜索应跳过不崩溃() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            var filePath = Path.Combine(tempDir, "empty.cs");
            await File.WriteAllTextAsync(filePath, "").ConfigureAwait(true);

            var query = BuildQuery("anything", [filePath]);
            var outcome = await Task.Run(() => RgEngine.Search(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.TotalMatches.Should().Be(0);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 构造最小化 RgQuery，仅设置 pattern 和 paths，其余用默认值。
    /// </summary>
    private static RgQuery BuildQuery(string pattern, IReadOnlyList<string> paths) => new(
        Pattern: pattern,
        Paths: paths,
        Globs: null,
        FileType: null,
        CaseInsensitive: false,
        SmartCase: false,
        WordRegexp: false,
        OnlyMatching: false,
        Replace: null,
        Multiline: false,
        FixedStrings: false,
        Hidden: false,
        NoIgnore: true,
        Before: null,
        After: null,
        Context: null,
        LineNumbers: true,
        HeadLimit: null,
        Offset: null,
        OutputMode: SearchOutputMode.Content,
        Sort: null,
        InvertMatch: false,
        LineRegexp: false,
        MaxCount: null,
        Threads: 1,
        MaxFilesize: null);
}
