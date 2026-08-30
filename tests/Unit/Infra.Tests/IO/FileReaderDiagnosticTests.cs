namespace Infra.Tests.IO;

/// <summary>
/// FileReader 结构化诊断单元测试 — 验证各错误路径返回 ToolDiagnostic。
/// </summary>
public class FileReaderDiagnosticTests
{
    private static readonly IFileSystem Fs = TestFileSystem.Current;

    #region 文件不存在 → FileNotFound 诊断

    [Fact]
    public async Task ReadFileAsync_FileNotFound_ReturnsFileNotFoundDiagnostic()
    {
        var reader = new FileReader(Fs, new FileOperationConfig());
        var filePath = "/test/nonexistent_file_for_diagnostic_test.txt";

        var result = await reader.ReadFileAsync(filePath).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("FileNotFound");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "filePath");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "cwd");
        result.Diagnostic.Suggestions.Should().NotBeEmpty();
    }

    #endregion

    #region 目录而非文件 → IsDirectoryNotFile 诊断

    [Fact]
    public async Task ReadFileAsync_DirectoryNotFile_ReturnsIsDirectoryDiagnostic()
    {
        var reader = new FileReader(Fs, new FileOperationConfig());
        var dirPath = "/test/some_directory_for_diagnostic_test";
        Fs.CreateDirectory(dirPath);

        var result = await reader.ReadFileAsync(dirPath).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("IsDirectoryNotFile");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "filePath");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "type" && d.Value == "directory");
    }

    #endregion

    #region 文件过大 → FileTooLarge 诊断

    [Fact]
    public async Task ReadFileAsync_FileTooLarge_ReturnsFileTooLargeDiagnostic()
    {
        // 使用极小的 MaxReadSize 触发过大检测
        var config = new FileOperationConfig { MaxReadSize = 10 };
        var reader = new FileReader(Fs, config);
        var filePath = "/test/too_large_file_for_diagnostic_test.txt";

        // 写入 100 字节文件（超过 MaxReadSize=10）
        var content = new string('A', 100);
        await Fs.WriteAllTextAsync(filePath, content).ConfigureAwait(true);

        var result = await reader.ReadFileAsync(filePath).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("FileTooLarge");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "filePath");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "fileSize");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "maxSize" && d.Value == "10");
        result.Diagnostic.Suggestions.Should().NotBeEmpty();
    }

    #endregion

    #region 二进制文件 → BinaryFileDetected 诊断

    [Fact]
    public async Task ReadFileAsync_BinaryFileWithNullByte_ReturnsBinaryFileDiagnostic()
    {
        var reader = new FileReader(Fs, new FileOperationConfig());
        var filePath = "/test/binary_null_byte_file_for_diagnostic_test.bin";

        // 写入含 null byte 的文件
        await Fs.WriteAllBytesAsync(filePath, [0x41, 0x42, 0x00, 0x43]).ConfigureAwait(true);

        var result = await reader.ReadFileAsync(filePath).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("BinaryFileDetected");
        result.Diagnostic.Details.Should().Contain(d => d.Key == "filePath");
        result.Diagnostic.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReadFileAsync_BinaryFileWithHighNonPrintableRatio_ReturnsBinaryFileDiagnostic()
    {
        var reader = new FileReader(Fs, new FileOperationConfig());
        var filePath = "/test/binary_nonprintable_file_for_diagnostic_test.bin";

        // 写入高非打印字符比例的文件（>10% 非打印字符）
        var bytes = new byte[100];
        for (var i = 0; i < 50; i++) bytes[i] = 0x41; // 'A'
        for (var i = 50; i < 100; i++) bytes[i] = 0x01; // 非打印控制字符
        await Fs.WriteAllBytesAsync(filePath, bytes).ConfigureAwait(true);

        var result = await reader.ReadFileAsync(filePath).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("BinaryFileDetected");
    }

    #endregion

    #region 正常文本文件 → 无诊断

    [Fact]
    public async Task ReadFileAsync_TextFile_SuccessNoDiagnostic()
    {
        var reader = new FileReader(Fs, new FileOperationConfig());
        var filePath = "/test/text_file_for_diagnostic_test.txt";

        await Fs.WriteAllTextAsync(filePath, "Hello World\nLine 2\n").ConfigureAwait(true);

        var result = await reader.ReadFileAsync(filePath).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Diagnostic.Should().BeNull();
        result.Content.Should().Contain("Hello World");
    }

    #endregion

    #region FileReadResult.FailureResult(diagnostic) 工厂方法

    [Fact]
    public void FileReadResult_FailureResult_WithDiagnostic_SetsDiagnosticAndMessage()
    {
        var diagnostic = ToolDiagnostic.Create(
            "TestReason",
            "Test formatted message",
            [new DiagnosticDetail("key1", "value1")],
            ["Test suggestion"]);

        var result = FileReadResult.FailureResult("/test/path.txt", diagnostic);

        result.Success.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Reason.Should().Be("TestReason");
        result.ErrorMessage.Should().Be("Test formatted message");
    }

    #endregion
}
