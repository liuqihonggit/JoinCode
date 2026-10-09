// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Abs.Tests.IO;

/// <summary>
/// MappedFileReader 单元测试 — 验证 mmap 读取、using 释放、异常处理。
/// </summary>
public sealed class MappedFileReaderTests {
    private static string CreateTempFile(string content) {
        var path = Path.Combine(Path.GetTempPath(), $"mmap_test_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Open_ReadToEnd_ReturnsFileContent() {
        var path = CreateTempFile("hello world");
        try {
            using var reader = MappedFileReader.Open(path);
            var content = reader.ReadToEnd();
            Assert.Equal("hello world", content);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_ToArray_ReturnsFileBytes() {
        var path = CreateTempFile("ABC");
        try {
            using var reader = MappedFileReader.Open(path);
            var bytes = reader.ToArray();
            Assert.Equal(new byte[] { 0x41, 0x42, 0x43 }, bytes);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_Length_ReturnsFileSize() {
        var path = CreateTempFile("1234567890");
        try {
            using var reader = MappedFileReader.Open(path);
            Assert.Equal(10, reader.Length);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_EmptyFile_ReturnsEmptyContent() {
        var path = CreateTempFile("");
        try {
            using var reader = MappedFileReader.Open(path);
            Assert.Equal("", reader.ReadToEnd());
            Assert.Equal(0, reader.Length);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_LargeFile_ReadsCorrectly() {
        var expected = new string('x', 100_000);
        var path = CreateTempFile(expected);
        try {
            using var reader = MappedFileReader.Open(path);
            var content = reader.ReadToEnd();
            Assert.Equal(expected, content);
            Assert.Equal(100_000, reader.Length);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_NonExistentFile_ThrowsFileNotFoundException() {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.txt");
        Assert.Throws<FileNotFoundException>(() => MappedFileReader.Open(path));
    }

    [Fact]
    public void Open_Utf8Content_ReturnsCorrectString() {
        var path = CreateTempFile("你好世界");
        try {
            using var reader = MappedFileReader.Open(path);
            var content = reader.ReadToEnd();
            Assert.Equal("你好世界", content);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow() {
        var path = CreateTempFile("test");
        try {
            var reader = MappedFileReader.Open(path);
            reader.Dispose();
            reader.Dispose();
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_MultipleReaders_OnSameFile_AllSucceed() {
        var path = CreateTempFile("shared content");
        try {
            using var r1 = MappedFileReader.Open(path);
            using var r2 = MappedFileReader.Open(path);
            Assert.Equal("shared content", r1.ReadToEnd());
            Assert.Equal("shared content", r2.ReadToEnd());
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_Utf8BomFile_ReadToEnd_SkipsBom() {
        var path = Path.Combine(Path.GetTempPath(), $"mmap_bom_{Guid.NewGuid():N}.json");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var json = "{\"vendor\":null}"u8.ToArray();
        File.WriteAllBytes(path, [.. bom, .. json]);
        try {
            using var reader = MappedFileReader.Open(path);
            var content = reader.ReadToEnd();
            Assert.Equal("{\"vendor\":null}", content);
            Assert.NotEqual('\uFEFF', content[0]);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_WithNullPath_ThrowsArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() => MappedFileReader.Open(null!));
    }

    [Fact]
    public void Open_WithEmptyPath_ThrowsArgumentException() {
        Assert.ThrowsAny<ArgumentException>(() => MappedFileReader.Open(""));
    }

    [Fact]
    public void Open_AfterDispose_ToArray_ThrowsObjectDisposedException() {
        var path = CreateTempFile("dispose test");
        try {
            var reader = MappedFileReader.Open(path);
            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.ToArray());
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_AfterDispose_ReadToEnd_ThrowsObjectDisposedException() {
        var path = CreateTempFile("dispose readtoend");
        try {
            var reader = MappedFileReader.Open(path);
            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadToEnd());
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_WithBinaryContent_ToArray_ReturnsExactBytes() {
        var expected = new byte[] { 0x00, 0xFF, 0x42, 0x6F, 0x6F, 0x21, 0x0A, 0x0D, 0x01 };
        var path = Path.Combine(Path.GetTempPath(), $"mmap_bin_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, expected);
        try {
            using var reader = MappedFileReader.Open(path);
            var actual = reader.ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Length, reader.Length);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToArray_AfterReadToEnd_ReturnsConsistentData() {
        var path = CreateTempFile("consistency check");
        try {
            using var reader = MappedFileReader.Open(path);
            var str = reader.ReadToEnd();
            var bytes = reader.ToArray();
            Assert.Equal(str, Encoding.UTF8.GetString(bytes));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_WithWhitespaceOnlyContent_ReturnsWhitespace() {
        var path = CreateTempFile("   \n\t  \r\n  ");
        try {
            using var reader = MappedFileReader.Open(path);
            Assert.Equal("   \n\t  \r\n  ", reader.ReadToEnd());
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_SingleByteFile_ReadsCorrectly() {
        var path = CreateTempFile("X");
        try {
            using var reader = MappedFileReader.Open(path);
            Assert.Equal("X", reader.ReadToEnd());
            Assert.Equal(1, reader.Length);
            Assert.Equal(new byte[] { 0x58 }, reader.ToArray());
        } finally {
            File.Delete(path);
        }
    }
}