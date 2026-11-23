namespace Sync.Tests.ToolHandlers;

/// <summary>
/// TarExtractor tar.gz 解压器单元测试
/// </summary>
public sealed class TarExtractorTests {
    [Fact]
    public async Task ExtractAsync_TarGzWithRootPrefix_StripsPrefixAndExtractsFiles() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        using var tarGz = new MemoryStream();
        CreateTestTarGz(tarGz, "liuqihonggit-Terminal.Gui-abc123", [("README.md", "hello world"), ("src/Program.cs", "// code")]);
        tarGz.Position = 0;

        var targetDir = fs.CombinePath(System.IO.Path.GetTempPath(), "tar_extract_test_" + Guid.NewGuid().ToString("N"));
        try {
            await TarExtractor.ExtractAsync(tarGz, targetDir, fs, CancellationToken.None);

            (await fs.ReadAllTextAsync(fs.CombinePath(targetDir, "README.md"), CancellationToken.None)).Should().Be("hello world");
            (await fs.ReadAllTextAsync(fs.CombinePath(targetDir, "src", "Program.cs"), CancellationToken.None)).Should().Be("// code");
        } finally {
            if (fs.DirectoryExists(targetDir)) fs.DeleteDirectory(targetDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsync_EmptyTar_ExtractsNothing() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        using var tarGz = new MemoryStream();
        CreateTestTarGz(tarGz, "root", []);
        tarGz.Position = 0;

        var targetDir = fs.CombinePath(System.IO.Path.GetTempPath(), "tar_empty_test_" + Guid.NewGuid().ToString("N"));
        try {
            await TarExtractor.ExtractAsync(tarGz, targetDir, fs, CancellationToken.None);
            fs.DirectoryExists(targetDir).Should().BeTrue();
        } finally {
            if (fs.DirectoryExists(targetDir)) fs.DeleteDirectory(targetDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAsync_EmptyTargetDir_ThrowsArgumentException() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        using var stream = new MemoryStream();
        var act = () => TarExtractor.ExtractAsync(stream, "  ", fs, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static void CreateTestTarGz(Stream output, string rootPrefix, IReadOnlyList<(string name, string content)> files) {
        using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"{rootPrefix}/"));
        foreach (var (name, content) in files) {
            var data = Encoding.UTF8.GetBytes(content);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{rootPrefix}/{name}") {
                DataStream = new MemoryStream(data),
            };
            tar.WriteEntry(entry);
        }
    }
}
