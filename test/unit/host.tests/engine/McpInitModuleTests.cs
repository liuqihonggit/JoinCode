namespace JoinCode.App.Modules.Tests;

/// <summary>
/// McpInitModule 刷新错误降级测试（修复 CI E2E：McpCommand_ShouldListServers 撞
/// ObjectDisposedException"锁 'AsyncLock#2' 已释放"）— 根因：CLI 单命令模式退出释放容器
/// （LocalToolRegistry.DisposeAsync 释放内部 AsyncLock#2）与后台插件链就绪钩子
/// RefreshKernelPluginsAsync 竞态；原 catch 一律 LogError（fail 级）进 stderr 触发 E2E NoErrors 断言红。
/// 降级语义：ObjectDisposedException = 退出竞态良性噪音（进程将亡刷新无意义）→ LogDebug 静默，
/// 对齐 McpPluginBootstrap"宿主关闭取消静默"先例；其余异常保持 LogError。
/// </summary>
public class McpInitModuleTests
{
    /// <summary>ObjectDisposedException — 降级 Debug 不打 Error（退出竞态良性）。</summary>
    [Fact]
    public void HandleRefreshError_ObjectDisposed_DowngradedToDebug()
    {
        var logger = new SpyLogger();
        var ex = new ObjectDisposedException(nameof(AsyncLock), "锁 'AsyncLock#2' 已释放");

        McpInitModule.HandleRefreshError(ex, logger);

        Assert.Empty(logger.ErrorCalls);
        Assert.Contains(logger.DebugCalls, c => c.Contains("退出竞态"));
    }

    /// <summary>其他异常 — 保持 Error 级（真实故障不得静默；消息模板固定，异常文本经 state 传递）。</summary>
    [Fact]
    public void HandleRefreshError_OtherException_KeepsErrorLevel()
    {
        var logger = new SpyLogger();
        var ex = new InvalidOperationException("工具注册表状态损坏");

        McpInitModule.HandleRefreshError(ex, logger);

        Assert.Contains("[MCP] 刷新 kernel.Plugins 失败", logger.ErrorCalls);
        Assert.Empty(logger.DebugCalls);
    }

    /// <summary>logger null — 两分支均不抛（DI 无日志场景）。</summary>
    [Fact]
    public void HandleRefreshError_NullLogger_NoThrow()
    {
        McpInitModule.HandleRefreshError(new ObjectDisposedException("x"), null);
        McpInitModule.HandleRefreshError(new InvalidOperationException("y"), null);
    }

    /// <summary>--json-schema 参数不存在时不注册工具（直接 return，不调用 RegisterToolAsync）。</summary>
    [Fact]
    public async Task RegisterSyntheticOutputToolAsync_NoJsonSchema_DoesNotRegister()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        var services = new ServiceCollection()
            .AddSingleton(new CommandLineOptions())
            .AddSingleton(toolRegistry.Object)
            .BuildServiceProvider();
        var logger = new SpyLogger();

        await McpInitModule.RegisterSyntheticOutputToolAsync(services, logger, CancellationToken.None);

        toolRegistry.Verify(
            r => r.RegisterToolAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolSchema>(),
                It.IsAny<ToolHandler>(), It.IsAny<CancellationToken>(),
                It.IsAny<ToolKind>(), It.IsAny<string?>(), It.IsAny<ToolTimeoutPolicy?>(), It.IsAny<string?>()),
            Times.Never,
            "无 --json-schema 参数时不应注册工具");
        Assert.Empty(logger.ErrorCalls);
    }

    /// <summary>Schema 验证失败时不注册工具，记 Error 日志。</summary>
    [Fact]
    public async Task RegisterSyntheticOutputToolAsync_SchemaValidationFails_DoesNotRegister()
    {
        var validator = new Mock<IJsonSchemaValidator>();
        validator.Setup(v => v.ValidateSchema(It.IsAny<string>()))
            .Returns(new SchemaValidationResult {
                IsValid = false,
                Errors = [new ValidationError { Path = "$", Message = "Invalid type" }]
            });
        var toolRegistry = new Mock<IToolRegistry>();
        var options = new CommandLineOptions { JsonSchema = """{"type":"invalid"}""" };
        var services = new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton(toolRegistry.Object)
            .AddSingleton(validator.Object)
            .BuildServiceProvider();
        var logger = new SpyLogger();

        await McpInitModule.RegisterSyntheticOutputToolAsync(services, logger, CancellationToken.None);

        toolRegistry.Verify(
            r => r.RegisterToolAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolSchema>(),
                It.IsAny<ToolHandler>(), It.IsAny<CancellationToken>(),
                It.IsAny<ToolKind>(), It.IsAny<string?>(), It.IsAny<ToolTimeoutPolicy?>(), It.IsAny<string?>()),
            Times.Never,
            "Schema 验证失败时不应注册工具");
        Assert.Contains(logger.ErrorCalls, c => c.Contains("JSON Schema 验证失败"));
    }

    /// <summary>Schema 验证通过时注册工具到 IToolRegistry。</summary>
    [Fact]
    public async Task RegisterSyntheticOutputToolAsync_SchemaValid_RegistersTool()
    {
        var validator = new Mock<IJsonSchemaValidator>();
        validator.Setup(v => v.ValidateSchema(It.IsAny<string>()))
            .Returns(new SchemaValidationResult { IsValid = true });
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(r => r.RegisterToolAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolSchema>(),
                It.IsAny<ToolHandler>(), It.IsAny<CancellationToken>(),
                It.IsAny<ToolKind>(), It.IsAny<string?>(), It.IsAny<ToolTimeoutPolicy?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var options = new CommandLineOptions { JsonSchema = """{"type":"object","properties":{"name":{"type":"string"}}}""" };
        var services = new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton(toolRegistry.Object)
            .AddSingleton(validator.Object)
            .BuildServiceProvider();
        var logger = new SpyLogger();

        await McpInitModule.RegisterSyntheticOutputToolAsync(services, logger, CancellationToken.None);

        toolRegistry.Verify();
        Assert.Contains(logger.InformationCalls, c => c.Contains("合成工具已注册"));
    }

    /// <summary>注册过程异常时记日志不崩溃（catch 兜底，Error 级日志）。</summary>
    [Fact]
    public async Task RegisterSyntheticOutputToolAsync_Exception_LogsAndDoesNotCrash()
    {
        var validator = new Mock<IJsonSchemaValidator>();
        validator.Setup(v => v.ValidateSchema(It.IsAny<string>()))
            .Returns(new SchemaValidationResult { IsValid = true });
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(r => r.RegisterToolAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolSchema>(),
                It.IsAny<ToolHandler>(), It.IsAny<CancellationToken>(),
                It.IsAny<ToolKind>(), It.IsAny<string?>(), It.IsAny<ToolTimeoutPolicy?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("注册表已锁定"));
        var options = new CommandLineOptions { JsonSchema = """{"type":"object"}""" };
        var services = new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton(toolRegistry.Object)
            .AddSingleton(validator.Object)
            .BuildServiceProvider();
        var logger = new SpyLogger();

        await McpInitModule.RegisterSyntheticOutputToolAsync(services, logger, CancellationToken.None);

        Assert.Contains(logger.ErrorCalls, c => c.Contains("合成工具注册失败"));
    }

    /// <summary>BuildSyntheticOutputSchema 正确构建含 input 属性的 ToolSchema，Required 包含 "input"。</summary>
    [Fact]
    public void BuildSyntheticOutputSchema_ReturnsCorrectSchema()
    {
        var schemaJson = """{"type":"object","properties":{"name":{"type":"string"}}}""";

        var toolSchema = McpInitModule.BuildSyntheticOutputSchema(schemaJson);

        Assert.Equal("object", toolSchema.Type);
        Assert.Single(toolSchema.Properties);
        Assert.True(toolSchema.Properties.ContainsKey("input"));
        Assert.Equal("string", toolSchema.Properties["input"].Type);
        var description = toolSchema.Properties["input"].Description;
        Assert.NotNull(description);
        Assert.Contains(schemaJson, description);
        Assert.Contains("input", toolSchema.Required);
    }

    /// <summary>记录型 logger 假体 — 按级别分桶捕获消息。</summary>
    private sealed class SpyLogger : ILogger<McpInitModule>
    {
        public List<string> ErrorCalls { get; } = [];
        public List<string> DebugCalls { get; } = [];
        public List<string> InformationCalls { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Error) ErrorCalls.Add(message);
            if (logLevel == LogLevel.Debug) DebugCalls.Add(message);
            if (logLevel == LogLevel.Information) InformationCalls.Add(message);
        }
    }
}
