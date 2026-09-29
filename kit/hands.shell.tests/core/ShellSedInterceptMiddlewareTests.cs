namespace Hands.Tests.Shell;

/// <summary>
/// ShellSedInterceptMiddleware 单元测试 — 验证 sed 拦截中间件的结构化诊断
/// </summary>
public class ShellSedInterceptMiddlewareTests {
    [Fact]
    public void BuildFileSystemUnavailableDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileSystemUnavailableDiagnostic();

        diagnostic.Reason.Should().Be("服务不可用");
        diagnostic.FormattedMessage.Should().Contain("IFileSystem");
    }

    [Fact]
    public void BuildWriteFailedDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildWriteFailedDiagnostic("/test/file.txt", "Access denied");

        diagnostic.Reason.Should().Be("写入文件失败");
        diagnostic.FormattedMessage.Should().Contain("Access denied");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "Access denied");
    }

    [Fact]
    public void BuildFileNotFoundDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileNotFoundDiagnostic("missing.txt");

        diagnostic.Reason.Should().Be("文件未找到");
        diagnostic.FormattedMessage.Should().Contain("missing.txt");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "file_path" && d.Value == "missing.txt");
        diagnostic.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public void BuildReadFailedDiagnostic_ReturnsCorrectStructure() {
        var diagnostic = ShellSedInterceptMiddleware.BuildReadFailedDiagnostic("/test/file.txt", "IO error");

        diagnostic.Reason.Should().Be("读取文件失败");
        diagnostic.FormattedMessage.Should().Contain("IO error");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "IO error");
    }

    // === ResolveFilePath 纯计算测试 ===

    [Fact]
    public void ResolveFilePath_AbsolutePath_ReturnsAsIs() {
        var absolute = Path.GetFullPath("/tmp/test.txt");
        ShellSedInterceptMiddleware.ResolveFilePath(absolute, "/work", "/cwd").Should().Be(absolute);
    }

    [Fact]
    public void ResolveFilePath_RelativePath_WithWorkingDirectory_Combines() {
        var result = ShellSedInterceptMiddleware.ResolveFilePath("sub/file.txt", "/work", "/cwd");
        result.Should().Be(Path.Combine("/work", "sub/file.txt"));
    }

    [Fact]
    public void ResolveFilePath_RelativePath_NullWorkingDirectory_UsesCurrentDir() {
        var result = ShellSedInterceptMiddleware.ResolveFilePath("file.txt", null, "/cwd");
        result.Should().Be(Path.Combine("/cwd", "file.txt"));
    }

    [Fact]
    public void ResolveFilePath_WorkingDirectory_TakesPrecedence_OverCurrentDir() {
        // workingDirectory 非 null 时优先于 currentDir
        var result = ShellSedInterceptMiddleware.ResolveFilePath("f.txt", "/work", "/cwd");
        result.Should().Be(Path.Combine("/work", "f.txt"));
        result.Should().NotContain("/cwd");
    }

    // === DetectLineEnding 纯计算测试 ===

    [Fact]
    public void DetectLineEnding_Crlf_ReturnsCrlf() {
        ShellSedInterceptMiddleware.DetectLineEnding("line1\r\nline2\r\n").Should().Be("\r\n");
    }

    [Fact]
    public void DetectLineEnding_LfOnly_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("line1\nline2\n").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_EmptyString_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_CrOnly_ReturnsLf() {
        // 仅 CR（无 LF）不含 \r\n,应返回 LF
        ShellSedInterceptMiddleware.DetectLineEnding("line1\rline2").Should().Be("\n");
    }

    [Fact]
    public void DetectLineEnding_NoLineEnding_ReturnsLf() {
        ShellSedInterceptMiddleware.DetectLineEnding("single line").Should().Be("\n");
    }

    // === NormalizeLineEndings 纯计算测试 ===

    [Fact]
    public void NormalizeLineEndings_Crlf_ReplacedWithLf() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\r\nb\r\n").Should().Be("a\nb\n");
    }

    [Fact]
    public void NormalizeLineEndings_LfOnly_Unchanged() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\nb\n").Should().Be("a\nb\n");
    }

    [Fact]
    public void NormalizeLineEndings_EmptyString_ReturnsEmpty() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("").Should().Be("");
    }

    [Fact]
    public void NormalizeLineEndings_MultipleCrlf_AllReplaced() {
        ShellSedInterceptMiddleware.NormalizeLineEndings("a\r\nb\r\nc\r\n").Should().Be("a\nb\nc\n");
    }

    [Fact]
    public void NormalizeLineEndings_NoCrlf_Unchanged() {
        var content = "no line endings at all";
        ShellSedInterceptMiddleware.NormalizeLineEndings(content).Should().Be(content);
    }

    // === BuildNoChangeMessage 纯计算测试 ===

    [Fact]
    public void BuildNoChangeMessage_EmptyContent_ReturnsEmptyFileMessage() {
        ShellSedInterceptMiddleware.BuildNoChangeMessage("").Should().Be("File is empty, pattern did not match");
    }

    [Fact]
    public void BuildNoChangeMessage_NonEmptyContent_ReturnsNoMatchMessage() {
        ShellSedInterceptMiddleware.BuildNoChangeMessage("some content").Should().Be("Pattern did not match any content");
    }

    // === BuildSedPreview 纯计算测试 ===

    private static SedEditInfo CreateSedInfo(string filePath = "test.txt", string pattern = "old", string replacement = "new", string flags = "g") =>
        new() { FilePath = filePath, Pattern = pattern, Replacement = replacement, Flags = flags, ExtendedRegex = false };

    [Fact]
    public void BuildSedPreview_ContainsHeaderInfo() {
        var sedInfo = CreateSedInfo("file.cs", "foo", "bar", "g");
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "foo", "bar");

        preview.Should().Contain("Sed edit preview for file.cs:");
        preview.Should().Contain("Pattern: foo");
        preview.Should().Contain("Replacement: bar");
        preview.Should().Contain("Flags: g");
    }

    [Fact]
    public void BuildSedPreview_WithDiff_ShowsChangedLines() {
        var sedInfo = CreateSedInfo();
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "old line\n", "new line\n");

        preview.Should().Contain("- old line");
        preview.Should().Contain("+ new line");
        preview.Should().Contain("1 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_NoChange_ShowsZeroChanges() {
        var sedInfo = CreateSedInfo();
        var content = "same content\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, content, content);

        // 内容相同,changeCount 保持 0（不进入 diff 循环）
        preview.Should().Contain("0 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_MultipleChanges_CountsAll() {
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\nline2\nline3\n";
        var newContent = "changed1\nchanged2\nchanged3\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        preview.Should().Contain("- line1");
        preview.Should().Contain("+ changed1");
        preview.Should().Contain("- line2");
        preview.Should().Contain("+ changed2");
        preview.Should().Contain("- line3");
        preview.Should().Contain("+ changed3");
        preview.Should().Contain("3 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_CappedAt20Changes() {
        // 超过 20 行变更时,只显示前 20 行 diff,changeCount 也被截断为 20（循环退出条件）
        var sedInfo = CreateSedInfo();
        var oldLines = Enumerable.Range(0, 30).Select(i => $"old{i}").ToArray();
        var newLines = Enumerable.Range(0, 30).Select(i => $"new{i}").ToArray();
        var oldContent = string.Join("\n", oldLines) + "\n";
        var newContent = string.Join("\n", newLines) + "\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        // 应包含前 20 行的 diff
        preview.Should().Contain("- old0");
        preview.Should().Contain("+ new0");
        preview.Should().Contain("- old19");
        preview.Should().Contain("+ new19");
        // 不应包含第 21 行（changeCount 已达 20,循环退出）
        preview.Should().NotContain("- old20");
        preview.Should().NotContain("+ new20");
        // changeCount 在循环中被截断为 20（原代码行为:changeCount < 20 是循环条件）
        preview.Should().Contain("20 line(s) changed");
    }

    [Fact]
    public void BuildSedPreview_DifferentLineCount_OnlyAdditions() {
        // 新增行:oldContent 行数少于 newContent
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\n";
        var newContent = "line1\nline2\nline3\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        // line1 相同,新增 line2/line3
        preview.Should().Contain("+ line2");
        preview.Should().Contain("+ line3");
        preview.Should().NotContain("- line1");
    }

    [Fact]
    public void BuildSedPreview_DifferentLineCount_OnlyDeletions() {
        // 删除行:oldContent 行数多于 newContent
        var sedInfo = CreateSedInfo();
        var oldContent = "line1\nline2\nline3\n";
        var newContent = "line1\n";
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, oldContent, newContent);

        preview.Should().Contain("- line2");
        preview.Should().Contain("- line3");
        preview.Should().NotContain("+ line2");
    }

    [Fact]
    public void BuildSedPreview_EndsWithConfirmInstruction() {
        var sedInfo = CreateSedInfo();
        var preview = ShellSedInterceptMiddleware.BuildSedPreview(sedInfo, "a\n", "b\n");

        // AppendLine 使用 Environment.NewLine,用 Contain 验证关键文本（跨平台兼容）
        preview.Should().Contain("Re-run the same sed command to confirm and apply this edit.");
    }

    // === InvokeAsync 分发器 + HandleSedEditAsync 主流程测试 ===
    // 全部用 Mock<IFileSystem> 隔离 IO;SessionContext.Current 默认 null → GetCurrentCache() 返回 null → 走 _fallbackEdits 分支
    // 同一个中间件实例的多次调用共享 _fallbackEdits,用于验证"首次预览→二次确认"流程
    // 确定性:无时序依赖(60s 窗口内连续调用)、无磁盘 IO(全 mock)、无 session 上下文(默认 AsyncLocal null)

    private static Mock<ISystemActuator> CreateBashProvider() {
        var mock = new Mock<ISystemActuator>();
        mock.SetupGet(x => x.Kind).Returns(SystemActuatorKind.FromId("bash")!);
        mock.SetupGet(x => x.ShellPath).Returns("bash");
        return mock;
    }

    private static ShellPipelineContext CreateContext(string command, string? workDir = "/work") {
        return new ShellPipelineContext {
            Command = command,
            Provider = CreateBashProvider().Object,
            WorkingDirectory = workDir,
        };
    }

    private static Mock<IFileSystem> CreateFileSystemMock(string currentDir = "/cwd") {
        var mock = new Mock<IFileSystem>();
        mock.Setup(x => x.GetCurrentDirectory()).Returns(currentDir);
        return mock;
    }

    // --- InvokeAsync 分发器(2 路径) ---

    /// <summary>
    /// 路径1: 非 sed 命令 → 调用 next 委托,不设置 SedResult
    /// </summary>
    [Fact]
    public async Task InvokeAsync_NonSedCommand_CallsNext_DoesNotSetSedResult() {
        await using var sut = new ShellSedInterceptMiddleware(CreateFileSystemMock().Object);
        var context = CreateContext("echo hello");

        var nextCalled = false;
        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue("非 sed 命令应调用 next 委托");
        context.SedResult.Should().BeNull("非 sed 命令不应设置 SedResult");
    }

    /// <summary>
    /// 路径2: sed -i 命令 → 短路,不调用 next,设置 SedResult 和 Result
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SedCommand_ShortCircuits_SetsSedResult_SkipsNext() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("foo bar");
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var context = CreateContext("sed -i 's/foo/baz/g' file.txt");

        var nextCalled = false;
        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse("sed 命令应短路不调用 next");
        context.SedResult.Should().NotBeNull("sed 命令应设置 SedResult");
        context.Result.Should().NotBeNull("sed 命令应同步设置 Result");
        context.Result.Should().BeSameAs(context.SedResult);
    }

    // --- HandleSedEditAsync 主流程(8 路径) ---

    /// <summary>
    /// 路径1: _fs is null → BuildFileSystemUnavailableDiagnostic
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FsNull_ReturnsFileSystemUnavailableDiagnostic() {
        await using var sut = new ShellSedInterceptMiddleware(fs: null);
        var context = CreateContext("sed -i 's/a/b/g' file.txt");

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.SedResult.Should().NotBeNull();
        context.SedResult!.IsError.Should().BeTrue();
        context.SedResult.Diagnostic.Should().NotBeNull();
        context.SedResult.Diagnostic!.Reason.Should().Be("服务不可用");
        context.SedResult.Diagnostic.FormattedMessage.Should().Contain("IFileSystem");
    }

    /// <summary>
    /// 路径2: 文件不存在 → BuildFileNotFoundDiagnostic
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FileNotExists_ReturnsFileNotFoundDiagnostic() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var context = CreateContext("sed -i 's/a/b/g' missing.txt");

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.SedResult!.IsError.Should().BeTrue();
        context.SedResult.Diagnostic!.Reason.Should().Be("文件未找到");
        context.SedResult.Diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "missing.txt");
        context.SedResult.Diagnostic.Suggestions.Should().HaveCount(2);
    }

    /// <summary>
    /// 路径3: 读取失败 → BuildReadFailedDiagnostic
    /// </summary>
    [Fact]
    public async Task InvokeAsync_ReadAllTextThrows_ReturnsReadFailedDiagnostic() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new IOException("disk read error"));
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var context = CreateContext("sed -i 's/a/b/g' file.txt");

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.SedResult!.IsError.Should().BeTrue();
        context.SedResult.Diagnostic!.Reason.Should().Be("读取文件失败");
        context.SedResult.Diagnostic.FormattedMessage.Should().Contain("disk read error");
        context.SedResult.Diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "disk read error");
    }

    /// <summary>
    /// 路径4: 无变更(pattern 不匹配) → BuildNoChangeMessage
    /// </summary>
    [Fact]
    public async Task InvokeAsync_PatternNoMatch_ReturnsNoChangeMessage() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("hello world");
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        // pattern xyz 不匹配内容 hello world → ApplySedSubstitution 返回原内容 → oldContent == newContent
        var context = CreateContext("sed -i 's/xyz/replaced/g' file.txt");

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.SedResult!.IsError.Should().BeFalse();
        context.SedResult.GetFirstText().Should().Contain("Pattern did not match any content");
    }

    /// <summary>
    /// 路径5: 首次调用 → 返回 BuildSedPreview 预览 + 存 pending(不调用 EditFileAsync)
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FirstCall_ReturnsPreview_StoresPending() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("foo bar");
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var context = CreateContext("sed -i 's/foo/baz/g' file.txt");

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.SedResult!.IsError.Should().BeFalse();
        var text = context.SedResult.GetFirstText();
        text.Should().Contain("Sed edit preview for file.txt:");
        text.Should().Contain("Pattern: foo");
        text.Should().Contain("Replacement: baz");
        text.Should().Contain("Re-run the same sed command to confirm and apply this edit.");
        // 首次不应调用 EditFileAsync
        fs.Verify(x => x.EditFileAsync<bool>(It.IsAny<string>(), It.IsAny<Func<byte[], CancellationToken, Task<(byte[]?, bool)>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 路径6: 二次调用确认匹配 → EditFileAsync 写入成功,返回 Applied 消息
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SecondCall_MatchingSed_AppliesEdit() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("foo bar");
        fs.Setup(x => x.EditFileAsync<bool>(It.IsAny<string>(), It.IsAny<Func<byte[], CancellationToken, Task<(byte[]?, bool)>>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var command = "sed -i 's/foo/baz/g' file.txt";

        // 首次:预览
        var firstCtx = CreateContext(command);
        await sut.InvokeAsync(firstCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);
        firstCtx.SedResult!.GetFirstText().Should().Contain("Sed edit preview");

        // 二次:同命令确认匹配 → EditFileAsync 调用
        var secondCtx = CreateContext(command);
        await sut.InvokeAsync(secondCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);

        secondCtx.SedResult!.IsError.Should().BeFalse();
        secondCtx.SedResult.GetFirstText().Should().Contain("Applied sed substitution to file.txt");
        fs.Verify(x => x.EditFileAsync<bool>(It.IsAny<string>(), It.IsAny<Func<byte[], CancellationToken, Task<(byte[]?, bool)>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 路径7: 二次调用写入失败(EditFileAsync 抛异常) → BuildWriteFailedDiagnostic
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SecondCall_WriteFails_ReturnsWriteFailedDiagnostic() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("foo bar");
        fs.Setup(x => x.EditFileAsync<bool>(It.IsAny<string>(), It.IsAny<Func<byte[], CancellationToken, Task<(byte[]?, bool)>>>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new IOException("disk full"));
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);
        var command = "sed -i 's/foo/baz/g' file.txt";

        // 首次:预览
        var firstCtx = CreateContext(command);
        await sut.InvokeAsync(firstCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);

        // 二次:EditFileAsync 抛异常
        var secondCtx = CreateContext(command);
        await sut.InvokeAsync(secondCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);

        secondCtx.SedResult!.IsError.Should().BeTrue();
        secondCtx.SedResult.Diagnostic!.Reason.Should().Be("写入文件失败");
        secondCtx.SedResult.Diagnostic.FormattedMessage.Should().Contain("disk full");
        secondCtx.SedResult.Diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "disk full");
    }

    /// <summary>
    /// 路径8: 二次调用 sed 信息不匹配(同文件不同 pattern) → 清旧 pending + 重新预览(不调用 EditFileAsync)
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SecondCall_MismatchedSed_ClearsPending_Repreview() {
        var fs = CreateFileSystemMock();
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("alpha beta");
        await using var sut = new ShellSedInterceptMiddleware(fs.Object);

        // 首次:pattern=alpha → 预览并存 pending(SedPattern=alpha)
        var firstCtx = CreateContext("sed -i 's/alpha/ALPHA/g' file.txt");
        await sut.InvokeAsync(firstCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);
        firstCtx.SedResult!.GetFirstText().Should().Contain("Pattern: alpha");

        // 二次:同文件不同 pattern(beta) → pending 存在但 SedPattern 不匹配 → 清 pending → 重新走首次路径 → 新预览
        var secondCtx = CreateContext("sed -i 's/beta/BETA/g' file.txt");
        await sut.InvokeAsync(secondCtx, static (_, _) => Task.CompletedTask, CancellationToken.None);

        secondCtx.SedResult!.IsError.Should().BeFalse();
        var text = secondCtx.SedResult.GetFirstText();
        text.Should().Contain("Sed edit preview for file.txt:");
        text.Should().Contain("Pattern: beta");
        // 未进入确认写入分支,EditFileAsync 不应被调用
        fs.Verify(x => x.EditFileAsync<bool>(It.IsAny<string>(), It.IsAny<Func<byte[], CancellationToken, Task<(byte[]?, bool)>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}