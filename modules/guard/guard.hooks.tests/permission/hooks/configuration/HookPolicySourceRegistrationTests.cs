// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// Hook policySettings 源注册测试 — 断链修复：企业管理员在 managed-settings.json 顶层配置 hooks
/// 必须被 Hook 系统消费（对齐 TS policySettings 参与钩子合并；C# 此前只注册 User/Project/Local 三源）。
/// <para>InMemoryFileSystem + JCC_APP_DATA_FOLDER 绝对路径隔离（GetManagedSettingsPath 的
/// Path.Combine 对 rooted 参数直接透传 → 指向内存路径，不触碰真实 C:\ProgramData）。</para>
/// <para>AppDataConstants 全局状态需串行 — 与 SettingsLoaderTests 同 Collection 约定。</para>
/// </summary>
[Collection("AppDataConstantsCollection")]
public sealed class HookPolicySourceRegistrationTests : IDisposable {
    private readonly string _managedDir;
    private readonly EnvVarScope _envScope;
    private bool _disposed;

    public HookPolicySourceRegistrationTests() {
        _managedDir = $"/test/hook_policy_{Guid.NewGuid().ToString("N")[..8]}";
        _envScope = EnvVarScope.Set(JccEnvVarEnumConstants.AppDataFolder, _managedDir);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _envScope.DisposeSafe();
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    [Fact]
    public async Task AddHookSystem_注册PolicySettings源_managed_settings顶层hooks生效() {
        // Given: managed-settings.json 顶层 hooks（对齐 TS 扁平结构 — 企业策略钩子）
        await using var fs = new InMemoryFileSystem();
        var managedPath = SettingsLoader.GetManagedSettingsPath();
        managedPath.Should().StartWith(_managedDir, "JCC_APP_DATA_FOLDER 绝对路径隔离生效");
        var settings = new HookSettingsFile {
            Hooks = new Dictionary<string, List<HookMatcher>> {
                ["preToolUse"] = [
                    new HookMatcher {
                        Matcher = "Bash",
                        Hooks = [new BashCommandHook { Command = "policy-echo" }]
                    }
                ]
            }
        };
        await fs.WriteAllTextAsync(managedPath, RelaxedJsonSerializer.Serialize(settings, HooksJsonContext.Default));

        // 先直接验证 provider 单体行为（隔离 DI 干扰）
        var single = await new JsonFileHookConfigurationProvider(managedPath, HookSource.PolicySettings, fs).LoadHooksAsync();
        single.Should().HaveCount(1, "provider 单体应能解析 managed-settings.json");

        // When: AddHookSystem 组装 + resolve manager + 加载全部钩子
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddHookSystem();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IHookConfigurationManager>();
        try {
            var group = await manager.LoadAllHooksAsync();

            // Then: policy 源钩子被消费
            var hooks = group.GetAllHooksForEvent(HookEvent.PreToolUse)
                .Where(h => h.Source == HookSource.PolicySettings)
                .ToList();
            hooks.Should().HaveCount(1);
            ((BashCommandHook)hooks[0].Command).Command.Should().Be("policy-echo");
        } finally {
            await ((HookConfigurationManager)manager).DisposeAsync();
        }
    }

    [Fact]
    public async Task AddHookSystem_无managed_settings文件_policy源静默为空() {
        // Given: 无 managed-settings.json（InMemory 未写入）
        await using var fs = new InMemoryFileSystem();

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fs);
        services.AddHookSystem();
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IHookConfigurationManager>();
        try {
            var group = await manager.LoadAllHooksAsync();

            // Then: policy 源存在但为空 — 缺文件不报错
            group.GetAllHooksForEvent(HookEvent.PreToolUse)
                .Where(h => h.Source == HookSource.PolicySettings)
                .Should().BeEmpty();
        } finally {
            await ((HookConfigurationManager)manager).DisposeAsync();
        }
    }
}
