[assembly: SuppressMessage("FileSystemAbstraction", "JCC9001", Scope = "type", Target = "Host.Tests.Cli.RgEngineListFilesTests", Justification = "RgEngine --files 列文件测试需直接访问文件系统验证真实目录枚举+gitignore+glob 过滤行为")]
namespace Host.Tests.Cli;

/// <summary>
/// RgEngine.ListFiles 测试 — --files 模式：复用 CollectFiles 过滤链（gitignore/glob/type/二进制排除），跳过正则搜索。
/// </summary>
public sealed class RgEngineListFilesTests {
    /// <summary>
    /// glob 过滤生效：只列出匹配 *.cs 的文件（含子目录）。
    /// </summary>
    [Fact]
    public async Task ListFiles_应用glob过滤_只列出匹配文件() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_files_glob_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            Directory.CreateDirectory(Path.Combine(tempDir, "sub"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "a.cs"), "class A {}\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "b.txt"), "plain text\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sub", "c.cs"), "class C {}\n").ConfigureAwait(true);

            var query = BuildListFilesQuery([tempDir], globs: ["*.cs"]);
            var outcome = await Task.Run(() => RgEngine.ListFiles(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            var fileNames = outcome.Results.Select(r => Path.GetFileName(r.FilePath)).ToList();
            fileNames.Should().BeEquivalentTo(["a.cs", "c.cs"]);
            outcome.Results.Should().OnlyContain(r => r.MatchCount == 0 && r.ContentLines == null);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// .gitignore 生效：被忽略的文件不出现在列表中（NoIgnore=false 时）。
    /// </summary>
    [Fact]
    public async Task ListFiles_遵守gitignore_被忽略文件不列出() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_files_ignore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".gitignore"), "*.log\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "a.cs"), "class A {}\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "debug.log"), "log content\n").ConfigureAwait(true);

            var query = BuildListFilesQuery([tempDir], noIgnore: false);
            var outcome = await Task.Run(() => RgEngine.ListFiles(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            var fileNames = outcome.Results.Select(r => Path.GetFileName(r.FilePath)).ToList();
            fileNames.Should().NotContain("debug.log");
            fileNames.Should().Contain("a.cs");
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 排序与分页接入：sort=path + head-limit=1 只返回排序后的第一个文件。
    /// </summary>
    [Fact]
    public async Task ListFiles_排序与分页_应生效() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_files_sort_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "b.txt"), "b\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "a.txt"), "a\n").ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "c.txt"), "c\n").ConfigureAwait(true);

            var query = BuildListFilesQuery([tempDir], sort: "path", headLimit: 1);
            var outcome = await Task.Run(() => RgEngine.ListFiles(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.Results.Should().ContainSingle();
            Path.GetFileName(outcome.Results[0].FilePath).Should().Be("a.txt");
            outcome.AppliedLimit.Should().Be(1);
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 空目录（无匹配文件）返回 Empty 成功结果（对齐 rg --files 无文件时的行为）。
    /// </summary>
    [Fact]
    public async Task ListFiles_无匹配文件_返回Empty成功() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"rg_files_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "a.txt"), "a\n").ConfigureAwait(true);

            var query = BuildListFilesQuery([tempDir], globs: ["*.nomatch"]);
            var outcome = await Task.Run(() => RgEngine.ListFiles(query, CancellationToken.None)).ConfigureAwait(true);

            outcome.Success.Should().BeTrue();
            outcome.Results.Should().BeEmpty();
        } finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// 构造 --files 模式的 RgQuery（Pattern 空占位，仅过滤相关字段生效）。
    /// </summary>
    private static RgQuery BuildListFilesQuery(
        IReadOnlyList<string> paths,
        IReadOnlyList<string>? globs = null,
        bool noIgnore = true,
        string? sort = null,
        int? headLimit = null) => new(
        Pattern: "",
        Paths: paths,
        Globs: globs,
        FileType: null,
        CaseInsensitive: false,
        SmartCase: false,
        WordRegexp: false,
        OnlyMatching: false,
        Replace: null,
        Multiline: false,
        FixedStrings: false,
        Hidden: false,
        NoIgnore: noIgnore,
        Before: null,
        After: null,
        Context: null,
        LineNumbers: false,
        HeadLimit: headLimit,
        Offset: null,
        OutputMode: SearchOutputMode.Files,
        Sort: sort,
        InvertMatch: false,
        LineRegexp: false,
        MaxCount: null,
        Threads: 1,
        MaxFilesize: null);
}
