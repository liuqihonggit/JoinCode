namespace Core.Tests;

public class BriefLogicTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly BriefLogic _briefLogic;

    public BriefLogicTests()
    {
        _briefLogic = new BriefLogic(_fs);
    }

    [Fact]
    public void ValidateAttachment_ValidFile_ReturnsValidResult()
    {
        var filePath = CreateFile("Hello World content", "test.txt");

        var result = _briefLogic.ValidateAttachment(filePath);

        Assert.True(result.IsValid);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal("Hello World content".Length, result.FileSize);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateAttachment_FileNotFound_ReturnsInvalidResult()
    {
        var filePath = "/test/nonexistent.txt";

        var result = _briefLogic.ValidateAttachment(filePath);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [Fact]
    public void ValidateAttachment_FileExceedsSizeLimit_ReturnsInvalidResult()
    {
        var content = new string('A', 500);
        var filePath = CreateFile(content, "large.txt");

        var result = _briefLogic.ValidateAttachment(filePath, maxSizeBytes: 100);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("大小", result.ErrorMessage);
    }

    [Fact]
    public void ValidateAttachment_NullPath_ReturnsInvalidResult()
    {
        var result = _briefLogic.ValidateAttachment(null!);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateAttachment_EmptyPath_ReturnsInvalidResult()
    {
        var result = _briefLogic.ValidateAttachment("");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateAttachment_UnderSizeLimit_ReturnsValidResult()
    {
        var content = new string('A', 50);
        var filePath = CreateFile(content, "small.txt");

        var result = _briefLogic.ValidateAttachment(filePath, maxSizeBytes: 100);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateAttachment_DetectsFileType()
    {
        var filePath = CreateFile("{}", "data.json");

        var result = _briefLogic.ValidateAttachment(filePath);

        Assert.True(result.IsValid);
        Assert.Equal(".json", result.FileType);
    }

    [Fact]
    public void ValidateAttachment_ImageFileType()
    {
        var filePath = CreateFile("fake image data", "screenshot.png");

        var result = _briefLogic.ValidateAttachment(filePath);

        Assert.True(result.IsValid);
        Assert.Equal(".png", result.FileType);
    }

    [Fact]
    public void FormatMessage_PlainMessage_ReturnsMarkdown()
    {
        var result = _briefLogic.FormatMessage("任务已完成");

        Assert.Contains("任务已完成", result);
    }

    [Fact]
    public void FormatMessage_WithAttachments_IncludesAttachmentInfo()
    {
        var filePath1 = CreateFile("diff content", "changes.diff");
        var filePath2 = CreateFile("error log", "error.log");
        var attachments = new List<BriefSendResult>
        {
            _briefLogic.ValidateAttachment(filePath1),
            _briefLogic.ValidateAttachment(filePath2)
        };

        var result = _briefLogic.FormatMessage("修复了错误", attachments);

        Assert.Contains("修复了错误", result);
        Assert.Contains("changes.diff", result);
        Assert.Contains("error.log", result);
    }

    [Fact]
    public void FormatMessage_EmptyMessage_ReturnsAttachmentOnly()
    {
        var filePath = CreateFile("data", "output.txt");
        var attachments = new List<BriefSendResult>
        {
            _briefLogic.ValidateAttachment(filePath)
        };

        var result = _briefLogic.FormatMessage("", attachments);

        Assert.Contains("output.txt", result);
    }

    [Fact]
    public void FormatMessage_NullAttachments_ReturnsMessageOnly()
    {
        var result = _briefLogic.FormatMessage("仅文本消息", null);

        Assert.Contains("仅文本消息", result);
    }

    [Fact]
    public void FormatMessage_ProactiveMessage_HasProactivePrefix()
    {
        var result = _briefLogic.FormatMessage("后台任务完成", isProactive: true);

        Assert.Contains("后台任务完成", result);
        Assert.Contains("主动", result);
    }

    [Fact]
    public void FormatMessage_CodeBlock_IsPreserved()
    {
        var message = "结果如下:\n```\ncode content\n```";

        var result = _briefLogic.FormatMessage(message);

        Assert.Contains("```", result);
        Assert.Contains("code content", result);
    }

    [Fact]
    public void FormatMessage_LongMessage_IsNotTruncated()
    {
        var message = new string('X', 500);

        var result = _briefLogic.FormatMessage(message);

        Assert.Contains(message, result);
    }

    [Fact]
    public void FormatMessageWithPaths_ValidAndInvalidPaths()
    {
        var validPath = CreateFile("valid content", "valid.txt");
        var invalidPath = "/test/nonexistent.txt";
        var paths = new[] { validPath, invalidPath };

        var result = _briefLogic.FormatMessageWithPaths("测试消息", paths);

        Assert.Contains("测试消息", result);
        Assert.Contains("valid.txt", result);
        Assert.DoesNotContain("nonexistent.txt", result);
    }

    [Fact]
    public void FormatMessageWithPaths_HandlesNullPaths()
    {
        var result = _briefLogic.FormatMessageWithPaths("仅消息", null);

        Assert.Contains("仅消息", result);
    }

    private string CreateFile(string content, string fileName)
    {
        var filePath = $"/test/{fileName}";
        _fs.WriteAllText(filePath, content);
        return filePath;
    }
}