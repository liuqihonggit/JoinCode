namespace Composition.Tests.Commands;

/// <summary>
/// /doctor 命令单元测试 — 覆盖全诊断段渲染(版本/运行时/工具/环境变量/磁盘/网络/API/权限/MCP/ONNX)/
/// 服务缺失降级(网络/配置/信任/注册表不可用)/MCP 注册表统计(本地计数/远程计数/已连接+断开状态)/
/// 信任工作目录+额外工作目录状态
/// <para>IMcpToolRegistry/ITrustFolderManager 经 DI 注入;外部工具检查走真实进程探测(未安装时输出未找到,不抛错)</para>
/// </summary>
[Collection("command-output")]
public sealed class DoctorCommandTest {
    private static (DoctorCommand Command, ChatCommandContext Context) Arrange(
        Action<IServiceCollection>? configureDi = null,
        IWorkspaceService? workspaceService = null,
        ITrustFolderManager? trustManager = null) {
        var services = new ServiceCollection();
        configureDi?.Invoke(services);
        if (trustManager is not null) services.AddSingleton(trustManager);
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = services.BuildServiceProvider(),
            WorkspaceService = workspaceService,
        };
        var context = CommandTestHarness.CreateContext(commandServices);
        return (new DoctorCommand(), context);
    }

    [Fact]
    public async Task Execute_WithoutOptionalServices_RendersAllSectionsWithDegrations() {
        var (cmd, context) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("环境诊断", "诊断内容经 Dialog 标题渲染");
        output.Should().Contain("[应用版本]");
        output.Should().Contain("[.NET 运行时]");
        output.Should().Contain("[Git]");
        output.Should().Contain("[常用工具]");
        output.Should().Contain("[搜索工具]");
        output.Should().Contain("[环境变量]");
        output.Should().Contain("[磁盘空间]");
        output.Should().Contain("[网络状态]");
        output.Should().Contain("网络检测服务不可用", "网络服务缺失应降级提示");
        output.Should().Contain("[API 连接]");
        output.Should().Contain("配置服务不可用", "配置服务缺失应降级提示");
        output.Should().Contain("[权限配置]");
        output.Should().Contain("信任管理器不可用", "信任管理器缺失应降级提示");
        output.Should().Contain("[MCP 服务]");
        output.Should().Contain("MCP 工具注册表不可用", "MCP 注册表缺失应降级提示");
        output.Should().Contain("[ONNX 嵌入模型]");
        output.Should().Contain("模型: 未下载", "内存文件系统中模型必然缺失");
        output.Should().Contain("词表: 未下载");
    }

    [Fact]
    public async Task Execute_WithMcpRegistryLocalOnly_ShowsCountsAndNoRemote() {
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.GetLocalToolCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5);
        registry.Setup(r => r.GetRemoteClientCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var (cmd, context) = Arrange(configureDi: di => di.AddSingleton(registry.Object));

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("本地工具: 5 个");
        output.Should().Contain("远程 MCP 服务器: 无连接");
    }

    [Fact]
    public async Task Execute_WithRemoteClients_ShowsConnectionStatusPerServer() {
        var connected = new Mock<IMcpClient>();
        connected.Setup(c => c.IsConnected).Returns(true);
        connected.Setup(c => c.ServerInfo).Returns(new Implementation { Name = "alpha-mcp", Version = "1.2.0" });
        connected.Setup(c => c.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("list timeout"));

        var disconnected = new Mock<IMcpClient>();
        disconnected.Setup(c => c.IsConnected).Returns(false);
        disconnected.Setup(c => c.ServerInfo).Returns((Implementation?)null);

        var clients = new Dictionary<string, IMcpClient> {
            ["alpha"] = connected.Object,
            ["beta"] = disconnected.Object,
        };
        var registry = new Mock<IMcpToolRegistry>();
        registry.Setup(r => r.GetLocalToolCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        registry.Setup(r => r.GetRemoteClientCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        registry.Setup(r => r.GetAllRemoteClientsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clients);
        var (cmd, context) = Arrange(configureDi: di => di.AddSingleton(registry.Object));

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("远程 MCP 服务器: 2 个");
        output.Should().Contain("alpha-mcp —");
        output.Should().Contain("已连接");
        output.Should().Contain("工具: 获取失败", "ListTools 抛异常应展示获取失败而非中断诊断");
        output.Should().Contain("beta —");
        output.Should().Contain("断开", "ServerInfo 为 null 时回退显示 clientId");
    }

    [Fact]
    public async Task Execute_WithTrustedWorkspaceAndAdditionalDirs_ShowsTrustStates() {
        var cwd = TestFileSystem.Current.GetCurrentDirectory();
        var trustManager = new Mock<ITrustFolderManager>();
        trustManager.Setup(t => t.IsTrusted(cwd)).Returns(true);
        trustManager.Setup(t => t.IsTrusted(It.Is<string>(p => p != cwd))).Returns(false);
        var workspace = new Mock<IWorkspaceService>();
        workspace.Setup(w => w.GetAdditionalDirectories()).Returns(["/extra/dir-a"]);

        var (cmd, context) = Arrange(workspaceService: workspace.Object, trustManager: trustManager.Object);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain($"已信任 ({cwd})");
        output.Should().Contain("额外工作目录: 1 个");
        output.Should().Contain("/extra/dir-a —");
        output.Should().Contain("未信任");
    }

    [Fact]
    public async Task Execute_WithUntrustedWorkspace_ShowsTrustHint() {
        var trustManager = new Mock<ITrustFolderManager>();
        trustManager.Setup(t => t.IsTrusted(It.IsAny<string>())).Returns(false);
        var (cmd, context) = Arrange(trustManager: trustManager.Object);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("未信任");
        output.Should().Contain("使用 /trust add 添加信任", "未信任工作区应引导用户添加信任");
    }

    [Fact]
    public async Task Execute_WithConfigService_ShowsProviderWithoutApiKey() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("profile", It.IsAny<CancellationToken>())).ReturnsAsync("openai");
        var (cmd, context) = Arrange(configureDi: di => di.AddSingleton(config.Object));

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("Provider: openai");
        output.Should().Contain("API Key 未设置", "无 ProviderDefinitionRegistry 时无法解析密钥应明确提示");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new DoctorCommand();
        cmd.Name.Should().Be("doctor");
        cmd.Usage.Should().Be("/doctor");
        cmd.Aliases.Should().Contain("dr", "doctor 支持 dr 别名");
    }
}
