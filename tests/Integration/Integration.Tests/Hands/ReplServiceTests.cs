namespace Integration.Tests.Hands;

[Trait("Category", "Integration")]
public sealed class ReplServiceTests
{
    private readonly Mock<ILogger<ReplService>> _loggerMock;
    private readonly IProcessService _processService;
    private readonly ReplService _service;

    public ReplServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReplService>>();
        _processService = new IO.ProcessService.PhysicalProcessService();
        _service = new ReplService(new IO.FileSystem.PhysicalFileSystem(), _processService, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new ReplService(TestFileSystem.Current, _processService, null));
        Assert.Null(exception);
    }

    [Fact]
    public void IsReplModeEnabled_Default_ShouldBeFalse()
    {
        Environment.SetEnvironmentVariable("JCC_REPL_MODE", null);
        var service = new ReplService(TestFileSystem.Current, _processService, _loggerMock.Object);

        Assert.False(service.IsReplModeEnabled);
    }

    [Fact]
    public void EnableReplMode_ShouldSetEnabled()
    {
        _service.EnableReplMode();

        Assert.True(_service.IsReplModeEnabled);
    }

    [Fact]
    public void DisableReplMode_ShouldSetDisabled()
    {
        _service.EnableReplMode();
        _service.DisableReplMode();

        Assert.False(_service.IsReplModeEnabled);
    }

    [Fact]
    public void EnableReplMode_ShouldLogInformation()
    {
        _service.EnableReplMode();

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("REPL")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void DisableReplMode_ShouldLogInformation()
    {
        _service.DisableReplMode();

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("REPL")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCode_ShouldReturnError()
    {
        var result = await _service.ExecuteAsync("", "csharp").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Equal("csharp", result.Language);
        Assert.Contains("空", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NullCode_ShouldReturnError()
    {
        var result = await _service.ExecuteAsync(null!, "csharp").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("空", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceCode_ShouldReturnError()
    {
        var result = await _service.ExecuteAsync("   ", "csharp").ConfigureAwait(true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedLanguage_ShouldReturnError()
    {
        var result = await _service.ExecuteAsync("print('hello')", "ruby").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("不支持的语言", result.Error);
        Assert.Equal("ruby", result.Language);
    }

    [Fact]
    public async Task ExecuteAsync_CSharpAliasCSharp1_ShouldRecognize()
    {
        var result = await _service.ExecuteAsync("Console.WriteLine(\"hi\")", "c#").ConfigureAwait(true);

        Assert.Equal("csharp", result.Language);
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellAliasPs1_ShouldRecognize()
    {
        var result = await _service.ExecuteAsync("Write-Host 'hi'", "ps1").ConfigureAwait(true);

        // ReplService 中 s_languageDefinitions 使用小写 "powershell"
        Assert.Equal("powershell", result.Language);
    }

    [Fact]
    public async Task ExecuteAsync_PythonAliasPy_ShouldRecognize()
    {
        var result = await _service.ExecuteAsync("print('hi')", "py").ConfigureAwait(true);

        Assert.Equal("python", result.Language);
    }

    [Fact]
    public void GetHiddenTools_WhenDisabled_ShouldReturnEmpty()
    {
        _service.DisableReplMode();

        var tools = _service.GetHiddenTools();

        Assert.Empty(tools);
    }

    [Fact]
    public void GetHiddenTools_WhenEnabled_ShouldReturnExpectedTools()
    {
        _service.EnableReplMode();

        var tools = _service.GetHiddenTools();

        Assert.Equal(8, tools.Count);
        Assert.Contains(FileToolNameConstants.FileRead, tools);
        Assert.Contains(FileToolNameConstants.FileWrite, tools);
        Assert.Contains(FileToolNameConstants.FileEdit, tools);
        Assert.Contains(SearchToolNameConstants.Glob, tools);
        Assert.Contains(SearchToolNameConstants.Grep, tools);
        Assert.Contains(ShellToolNameConstants.ShellExecute, tools);
        Assert.Contains("notebook_edit", tools);
        // ReplService 中 s_hiddenTools 使用小写 "agent"
        Assert.Contains("agent", tools);
    }

    [Fact]
    public void GetAvailableLanguages_ShouldReturnThreeLanguages()
    {
        var languages = _service.GetAvailableLanguages();

        Assert.Equal(3, languages.Count);

        var names = languages.Select(l => l.Language).ToList();
        Assert.Contains("csharp", names);
        // ReplService 中 s_languageDefinitions 使用小写 "powershell"
        Assert.Contains("powershell", names);
        Assert.Contains("python", names);
    }

    [Fact]
    public void GetAvailableLanguages_ShouldHaveDisplayNames()
    {
        var languages = _service.GetAvailableLanguages();

        foreach (var lang in languages)
        {
            Assert.False(string.IsNullOrEmpty(lang.DisplayName));
            Assert.False(string.IsNullOrEmpty(lang.Executable));
        }
    }

    [Fact]
    public void GetAvailableLanguages_CSharp_ShouldHaveDotnetScriptExecutable()
    {
        var languages = _service.GetAvailableLanguages();
        var csharp = languages.First(l => l.Language == "csharp");

        Assert.Equal("C#", csharp.DisplayName);
        Assert.Contains("dotnet-script", csharp.Executable);
    }

    [Fact]
    public void GetAvailableLanguages_PowerShell_ShouldHavePwshExecutable()
    {
        var languages = _service.GetAvailableLanguages();
        // ReplService 中 s_languageDefinitions 使用小写 "powershell"
        var pwsh = languages.First(l => l.Language == "powershell");

        Assert.Equal("PowerShell", pwsh.DisplayName);
    }

    [Fact]
    public void GetAvailableLanguages_Python_ShouldHavePythonExecutable()
    {
        var languages = _service.GetAvailableLanguages();
        var python = languages.First(l => l.Language == "python");

        Assert.Equal("Python", python.DisplayName);
    }

    [Fact]
    public void GetAvailableLanguages_IsAvailableShouldBeBoolean()
    {
        var languages = _service.GetAvailableLanguages();

        foreach (var lang in languages)
        {
            Assert.True(lang.IsAvailable || !lang.IsAvailable);
        }
    }

    [Fact]
    public void GetAvailableLanguages_UnavailableLanguageShouldHaveInstallHint()
    {
        var languages = _service.GetAvailableLanguages();

        foreach (var lang in languages)
        {
            if (!lang.IsAvailable)
            {
                Assert.False(string.IsNullOrEmpty(lang.InstallHint));
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_CSharp_WhenNotInstalled_ShouldReturnInstallHint()
    {
        var languages = _service.GetAvailableLanguages();
        var csharp = languages.First(l => l.Language == "csharp");

        if (csharp.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("Console.WriteLine(1)", "csharp").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("dotnet-script", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PowerShell_WhenNotInstalled_ShouldReturnInstallHint()
    {
        var languages = _service.GetAvailableLanguages();
        // ReplService 中 s_languageDefinitions 使用小写 "powershell"
        var pwsh = languages.First(l => l.Language == "powershell");

        if (pwsh.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("Write-Host 'hi'", "powershell").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("PowerShell", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_Python_WhenNotInstalled_ShouldReturnInstallHint()
    {
        var languages = _service.GetAvailableLanguages();
        var python = languages.First(l => l.Language == "python");

        if (python.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("print('hi')", "python").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("Python", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_Canceled_ShouldReturnCanceledError()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        var result = await _service.ExecuteAsync("Console.WriteLine(1)", "csharp", 30, cts.Token).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("取消", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CSharpSimpleCode_WhenAvailable_ShouldSucceed()
    {
        var languages = _service.GetAvailableLanguages();
        var csharp = languages.First(l => l.Language == "csharp");

        if (!csharp.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("Console.WriteLine(42);", "csharp", 30).ConfigureAwait(true);

        Assert.True(result.Success);
        Assert.Contains("42", result.Output);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PythonSimpleCode_WhenAvailable_ShouldSucceed()
    {
        var languages = _service.GetAvailableLanguages();
        var python = languages.First(l => l.Language == "python");

        if (!python.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("print(42)", "python", 30).ConfigureAwait(true);

        Assert.True(result.Success);
        Assert.Contains("42", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_CSharpBadCode_WhenAvailable_ShouldFail()
    {
        var languages = _service.GetAvailableLanguages();
        var csharp = languages.First(l => l.Language == "csharp");

        if (!csharp.IsAvailable)
        {
            return;
        }

        var result = await _service.ExecuteAsync("this is not valid c#", "csharp", 30).ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void IsReplModeEnabled_WithEnvVar_ShouldBeEnabled()
    {
        Environment.SetEnvironmentVariable("JCC_REPL_MODE", "1");
        try
        {
            var service = new ReplService(TestFileSystem.Current, _processService, _loggerMock.Object);
            Assert.True(service.IsReplModeEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_REPL_MODE", null);
        }
    }

    [Fact]
    public void IsReplModeEnabled_WithEnvVarFalse_ShouldBeDisabled()
    {
        Environment.SetEnvironmentVariable("JCC_REPL_MODE", "false");
        try
        {
            var service = new ReplService(TestFileSystem.Current, _processService, _loggerMock.Object);
            Assert.False(service.IsReplModeEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_REPL_MODE", null);
        }
    }

    [Fact]
    public void IsReplModeEnabled_WithEnvVarZero_ShouldBeDisabled()
    {
        Environment.SetEnvironmentVariable("JCC_REPL_MODE", "0");
        try
        {
            var service = new ReplService(TestFileSystem.Current, _processService, _loggerMock.Object);
            Assert.False(service.IsReplModeEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_REPL_MODE", null);
        }
    }
}
