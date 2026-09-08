namespace Infra.IO.Tests;

/// <summary>
/// FileWriter BOM 写入行为测试。
/// 验证新创建的文件不含 UTF-8 BOM（EF BB BF），更新已有文件时保持原有编码。
/// </summary>
public class FileWriterBomTests
{
    private static string GetTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "jcc-bom-test-" + Guid.NewGuid().ToString("N") + ".txt");
        return path;
    }

    [Fact]
    public async Task WriteFileAsync_NewFile_NoBom()
    {
        var fs = new PhysicalFileSystem();
        var config = new FileOperationConfig();
        var writer = new FileWriter(fs, config);
        var path = GetTempPath();
        try
        {
            await writer.WriteFileAsync(path, "abc", default);

            var bytes = await fs.ReadAllBytesAsync(path, default);
            // 不应以 EF BB BF 开头
            bytes.Length.Should().Be(3);
            bytes[0].Should().Be((byte)'a');
            bytes[1].Should().Be((byte)'b');
            bytes[2].Should().Be((byte)'c');
        }
        finally
        {
            if (fs.FileExists(path)) fs.DeleteFile(path);
        }
    }

    [Fact]
    public async Task WriteFileAsync_NewFile_EmptyContent_NoBom()
    {
        var fs = new PhysicalFileSystem();
        var config = new FileOperationConfig();
        var writer = new FileWriter(fs, config);
        var path = GetTempPath();
        try
        {
            await writer.WriteFileAsync(path, "", default);

            var bytes = await fs.ReadAllBytesAsync(path, default);
            // 空文件应为 0 字节，不是 3 字节 BOM
            bytes.Length.Should().Be(0);
        }
        finally
        {
            if (fs.FileExists(path)) fs.DeleteFile(path);
        }
    }

    [Fact]
    public async Task WriteFileAsync_UpdateExistingNoBom_PreservesNoBom()
    {
        var fs = new PhysicalFileSystem();
        var config = new FileOperationConfig();
        var writer = new FileWriter(fs, config);
        var path = GetTempPath();
        try
        {
            // 先创建无 BOM 文件
            await writer.WriteFileAsync(path, "original", default);
            var bytesBefore = await fs.ReadAllBytesAsync(path, default);
            bytesBefore.Length.Should().Be(8, "无 BOM 的 original 应为 8 字节");

            // 更新文件
            await writer.WriteFileAsync(path, "updated", default);
            var bytesAfter = await fs.ReadAllBytesAsync(path, default);
            bytesAfter.Length.Should().Be(7, "无 BOM 的 updated 应为 7 字节");
            bytesAfter[0].Should().Be((byte)'u');
        }
        finally
        {
            if (fs.FileExists(path)) fs.DeleteFile(path);
        }
    }
}
