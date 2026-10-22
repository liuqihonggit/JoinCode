namespace Host.Tests.Engine;

/// <summary>
/// SlashCommandModule 引擎模块测试（TASK065 F4）— GUI/TUI 引擎链的 ChatCommandRegistry 注册点：
/// 引擎域模块注册单例（工厂内 GeneratedCommandRegistration 全量注册）+ ISlashCommandRegistry 转发，
/// 消除 SlashCommandRunner 每命令 new + 全量重建的纯开销（Runner DI 优先解析）；
/// CLI 链不经此模块（CliModule 宿主侧自有注册，两处注册形态一致互不干扰）。
/// </summary>
public sealed class SlashCommandModuleTests {
    /// <summary>模块上下文（Options/Config 最小面）。</summary>
    private static AppModuleContext Context() => new() {
        Options = new CommandLineOptions(),
        Config = new WorkflowConfig(),
    };

    /// <summary>注册描述符：ChatCommandRegistry 单例（工厂注册形态）+ ISlashCommandRegistry 转发（对齐 CliModule 先例）。</summary>
    [Fact]
    public void ConfigureServices_RegistersCommandRegistrySingleton() {
        var services = new ServiceCollection();

        new SlashCommandModule().ConfigureServices(services, Context());

        Assert.Contains(services, d => d.ServiceType == typeof(ChatCommandRegistry)
            && d.Lifetime == Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(ISlashCommandRegistry));
    }

    /// <summary>构建解析：registry 单例含全量命令（生成器注册），ISlashCommandRegistry 转发解析同实例。</summary>
    [Fact]
    public void Build_ResolvesRegistryWithFullCommands() {
        var services = new ServiceCollection();
        new SlashCommandModule().ConfigureServices(services, Context());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<ChatCommandRegistry>();
        Assert.NotNull(registry);
        Assert.True(registry.HasCommand("help"));
        Assert.True(registry.HasCommand("exit"));
        Assert.Same(registry, provider.GetService<ISlashCommandRegistry>());
    }
}
