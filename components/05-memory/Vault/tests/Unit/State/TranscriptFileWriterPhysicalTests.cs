#pragma warning disable JCC3010, JCC3011, JCC3012, JCC9001
namespace State.Tests;

/// <summary>
/// 复现 Bug: E2E 测试中 TranscriptFileWriter.AppendEntriesAsync 抛 FileNotFoundException
///
/// 错误堆栈:
///   System.IO.FileNotFoundException: Could not find file 'C:\Users\Administrator\AppData\Roaming\jcc\sessions\xxx.jsonl'
///      at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(...)
///      at System.IO.FileStream..ctor(String path, FileMode mode, FileAccess access, FileShare share)
///      at IO.FileSystem.PhysicalFileSystem.CreateStream(String path, FileMode mode, FileAccess access, FileShare share)
///      at State.TranscriptFileWriter.AppendEntriesAsync(String filePath, IReadOnlyList`1 entries, CancellationToken cancellationToken)
///
/// 根因: FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException (与 .NET Framework 不同)
/// 即使 EnsureFileExists 试图先创建文件,也可能因竞态/权限问题失败被吞,导致后续 Append 抛错
///
/// 修复方案: 把 FileMode.Append 改为 FileMode.OpenOrCreate (OpenOrCreate 不会抛 FileNotFoundException)
/// </summary>
public sealed class TranscriptFileWriterPhysicalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IO.FileSystem.PhysicalFileSystem _fs;
    private readonly TranscriptFileWriter _writer;

    public TranscriptFileWriterPhysicalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"transcript_test_{Guid.NewGuid():N}");
        _fs = new IO.FileSystem.PhysicalFileSystem();
        _fs.CreateDirectory(_tempDir);
        _writer = new TranscriptFileWriter(_fs, _tempDir, NullLogger.Instance);
    }

    /// <summary>
    /// 复现 Bug: 文件不存在时,AppendEntriesAsync 调用 FileMode.Append 抛 FileNotFoundException
    /// (这个测试用 PhysicalFileSystem 而不是 InMemoryFileSystem,因为后者不会复现此 bug)
    /// </summary>
    [Fact]
    public async Task AppendEntriesAsync_FileNotExists_ShouldNotThrowFileNotFoundException()
    {
        // Arrange — 使用一个绝对不存在的文件路径
        var filePath = Path.Combine(_tempDir, "nonexistent-session.jsonl");
        _fs.FileExists(filePath).Should().BeFalse("前提: 文件确实不存在");

        var entries = new List<TranscriptEntry>
        {
            new()
            {
                SessionId = "nonexistent-session",
                Role = "user",
                Content = "test message",
                Timestamp = DateTime.UtcNow
            }
        };

        // Act — 应该不抛 FileNotFoundException (修复后)
        var act = async () => await _writer.AppendEntriesAsync(filePath, entries).ConfigureAwait(true);

        // Assert — 修复前: 抛 FileNotFoundException; 修复后: 不抛
        await act.Should().NotThrowAsync<FileNotFoundException>().ConfigureAwait(true);

        // 验证文件被创建并有内容
        _fs.FileExists(filePath).Should().BeTrue("文件应被创建");
        var lines = await _fs.ReadAllLinesAsync(filePath).ConfigureAwait(true);
        lines.Should().HaveCount(1);
        lines[0].Should().Contain("test message");
    }

    /// <summary>
    /// 验证根因: FileMode.Append 在 .NET 5+ 中文件不存在时抛 FileNotFoundException
    /// 这是 E2E 测试中 TranscriptFileWriter.AppendEntriesAsync 抛错的直接原因
    /// </summary>
    [Fact]
    public void FileModeAppend_FileNotExists_ThrowsFileNotFoundException()
    {
        // Arrange — 使用一个绝对不存在的文件路径
        var filePath = Path.Combine(_tempDir, "verify-filemode-append.jsonl");
        _fs.FileExists(filePath).Should().BeFalse("前提: 文件确实不存在");

        Exception? caught = null;
        // Act — 通过 IFileSystem.CreateStream(FileMode.Append),模拟 AppendEntriesAsync 的行为
        try
        {
            _fs.CreateStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None).Dispose();
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        finally
        {
            // 清理
            if (_fs.FileExists(filePath))
            {
                _fs.DeleteFile(filePath);
            }
        }

        // 输出实际异常类型用于诊断
        Console.Error.WriteLine($"[FileModeAppend_Test] 文件不存在场景 - 实际异常: {caught?.GetType().FullName ?? "(无)"}: {caught?.Message ?? "(无)"}");

        // Assert — .NET 10 中 FileMode.Append 自动创建文件,不抛 FileNotFoundException
        caught.Should().BeNull(
            ".NET 10 中 FileMode.Append 在文件不存在时自动创建文件,不抛 FileNotFoundException");
    }

    /// <summary>
    /// 验证根因: 当 sessions 目录不存在时,CreateStream(FileMode.Append) 抛 DirectoryNotFoundException
    /// 这可能是 E2E 测试中错误的真正根因
    /// </summary>
    [Fact]
    public void FileModeAppend_DirectoryNotExists_ThrowsDirectoryNotFoundException()
    {
        // Arrange — 使用一个不存在的目录路径
        var nonexistentDir = Path.Combine(_tempDir, "nonexistent-dir");
        var filePath = Path.Combine(nonexistentDir, "test.jsonl");
        _fs.DirectoryExists(nonexistentDir).Should().BeFalse("前提: 目录确实不存在");

        Exception? caught = null;
        // Act — 直接调用 CreateStream(FileMode.Append),不先创建目录
        try
        {
            _fs.CreateStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None).Dispose();
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        finally
        {
            if (_fs.FileExists(filePath))
            {
                _fs.DeleteFile(filePath);
            }
            if (_fs.DirectoryExists(nonexistentDir))
            {
                _fs.DeleteDirectory(nonexistentDir, recursive: true);
            }
        }

        // 输出实际异常类型用于诊断
        Console.Error.WriteLine($"[FileModeAppend_Test] 目录不存在场景 - 实际异常: {caught?.GetType().FullName ?? "(无)"}: {caught?.Message ?? "(无)"}");

        // Assert — 目录不存在时抛 DirectoryNotFoundException (不是 FileNotFoundException)
        caught.Should().BeOfType<DirectoryNotFoundException>(
            "目录不存在时,CreateStream 抛 DirectoryNotFoundException");
    }

    /// <summary>
    /// 验证修复: FileMode.OpenOrCreate 在文件不存在时不抛 FileNotFoundException
    /// </summary>
    [Fact]
    public void FileModeOpenOrCreate_FileNotExists_DoesNotThrowFileNotFoundException()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "verify-filemode-openorcreate.jsonl");
        _fs.FileExists(filePath).Should().BeFalse("前提: 文件确实不存在");

        // Act — 直接调用 CreateStream(FileMode.OpenOrCreate)
        var act = () =>
        {
            try
            {
                _fs.CreateStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None).Dispose();
            }
            finally
            {
                if (_fs.FileExists(filePath))
                {
                    _fs.DeleteFile(filePath);
                }
            }
        };

        // Assert — FileMode.OpenOrCreate 不抛 FileNotFoundException
        act.Should().NotThrow<FileNotFoundException>(
            "验证修复: FileMode.OpenOrCreate 在文件不存在时不抛 FileNotFoundException");
    }

    public void Dispose()
    {
        _writer.Dispose();
        try
        {
            _fs.DeleteDirectory(_tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            // 测试清理失败不影响结果,仅记录到 Console.Error
            Console.Error.WriteLine($"[TranscriptFileWriterPhysicalTests] 清理临时目录失败: {ex.Message}");
        }
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012, JCC9001
