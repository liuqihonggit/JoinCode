namespace Abs.Tests.IO;

/// <summary>
/// MappedFileReader 单元测试 — 验证 mmap 读取、using 释放、异常处理。
/// </summary>
public sealed class MappedFileReaderTests
{
    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mmap_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Open_ReadToEnd_ReturnsFileContent()
    {
        var path = CreateTempFile("hello world");
        try
        {
            using var reader = new MappedFileReader(path);
            var content = reader.ReadToEnd();
            Assert.Equal("hello world", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_ToArray_ReturnsFileBytes()
    {
        var path = CreateTempFile("ABC");
        try
        {
            using var reader = new MappedFileReader(path);
            var bytes = reader.ToArray();
            Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, bytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_Length_ReturnsFileSize()
    {
        var path = CreateTempFile("1234567890");
        try
        {
            using var reader = new MappedFileReader(path);
            Assert.Equal(10, reader.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_EmptyFile_ReturnsEmptyContent()
    {
        var path = CreateTempFile("");
        try
        {
            using var reader = new MappedFileReader(path);
            Assert.Equal("", reader.ReadToEnd());
            Assert.Equal(0, reader.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_LargeFile_ReadsCorrectly()
    {
        var expected = new string('x', 100_000);
        var path = CreateTempFile(expected);
        try
        {
            using var reader = new MappedFileReader(path);
            var content = reader.ReadToEnd();
            Assert.Equal(expected, content);
            Assert.Equal(100_000, reader.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.txt");
        Assert.Throws<FileNotFoundException>(() => new MappedFileReader(path));
    }

    [Fact]
    public void Open_Utf8Content_ReturnsCorrectString()
    {
        var path = CreateTempFile("你好世界");
        try
        {
            using var reader = new MappedFileReader(path);
            var content = reader.ReadToEnd();
            Assert.Equal("你好世界", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var path = CreateTempFile("test");
        try
        {
            var reader = new MappedFileReader(path);
            reader.Dispose();
            reader.Dispose();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_MultipleReaders_OnSameFile_AllSucceed()
    {
        var path = CreateTempFile("shared content");
        try
        {
            using var r1 = new MappedFileReader(path);
            using var r2 = new MappedFileReader(path);
            Assert.Equal("shared content", r1.ReadToEnd());
            Assert.Equal("shared content", r2.ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
