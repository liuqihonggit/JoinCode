namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookConfigurationManager 确定性测试 — mock IHookConfigurationProvider / InMemoryFileSystem 消除 IO 依赖
/// <para>测试缓存+锁逻辑、CRUD 操作、provider 异常隔离、JSON 文件解析。不依赖时序。</para>
/// </summary>
public sealed class HookConfigurationManagerTest {

    #region 辅助构造

    private static HookCommand CreateHook(string command = "echo test") {
        return new BashCommandHook { Command = command };
    }

    private static SourcedHookConfig CreateSourcedHook(
        HookEvent evt = HookEvent.PreToolUse,
        string? matcher = null,
        HookSource source = HookSource.UserSettings,
        string? command = null) {
        return new SourcedHookConfig {
            Event = evt,
            Matcher = matcher,
            Command = CreateHook(command ?? "echo test"),
            Source = source
        };
    }

    private static Mock<IHookConfigurationProvider> CreateProviderMock(params SourcedHookConfig[] hooks) {
        var mock = new Mock<IHookConfigurationProvider>();
        mock.Setup(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(hooks.ToList());
        return mock;
    }

    #endregion

    #region LoadAllHooksAsync — 缓存 + 锁

    [Fact]
    public async Task LoadAllHooksAsync_无provider注册_返回空分组() {
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        var group = await manager.LoadAllHooksAsync();
        group.GetAllHooksForEvent(HookEvent.PreToolUse).Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAllHooksAsync_首次加载_调用provider并缓存() {
        var providerMock = CreateProviderMock(CreateSourcedHook());
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        var group1 = await manager.LoadAllHooksAsync();
        group1.GetAllHooksForEvent(HookEvent.PreToolUse).Should().HaveCount(1);

        // 第二次调用应命中缓存,不再调用 provider
        var group2 = await manager.LoadAllHooksAsync();
        group2.GetAllHooksForEvent(HookEvent.PreToolUse).Should().HaveCount(1);

        providerMock.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadAllHooksAsync_多provider_按优先级合并() {
        var userHook = CreateSourcedHook(source: HookSource.UserSettings, command: "user-cmd");
        var projectHook = CreateSourcedHook(source: HookSource.ProjectSettings, command: "project-cmd");

        var userProvider = CreateProviderMock(userHook);
        var projectProvider = CreateProviderMock(projectHook);

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, userProvider.Object);
        manager.RegisterProvider(HookSource.ProjectSettings, projectProvider.Object);

        var group = await manager.LoadAllHooksAsync();

        // 两个 provider 的钩子都应加载
        var allHooks = group.GetAllHooksForEvent(HookEvent.PreToolUse);
        allHooks.Should().HaveCount(2);
        allHooks.Select(h => h.Source).Should().Contain(HookSource.UserSettings).And.Contain(HookSource.ProjectSettings);
    }

    [Fact]
    public async Task LoadAllHooksAsync_provider抛异常_不崩溃且继续其他provider() {
        var failingProvider = new Mock<IHookConfigurationProvider>();
        failingProvider.Setup(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider boom"));

        var goodProvider = CreateProviderMock(CreateSourcedHook(command: "good"));

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, failingProvider.Object);
        manager.RegisterProvider(HookSource.ProjectSettings, goodProvider.Object);

        var group = await manager.LoadAllHooksAsync();

        // 失败 provider 的钩子被跳过,成功 provider 的钩子正常加载
        group.GetAllHooksForEvent(HookEvent.PreToolUse).Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAllHooksAsync_InvalidateCache后_重新加载() {
        var providerMock = CreateProviderMock(CreateSourcedHook());
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        await manager.LoadAllHooksAsync();
        await manager.InvalidateCacheAsync();
        await manager.LoadAllHooksAsync();

        // 缓存清除后应再次调用 provider
        providerMock.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetHooksForEventAsync_返回匹配事件的钩子() {
        var preHook = CreateSourcedHook(evt: HookEvent.PreToolUse, matcher: "Bash");
        var postHook = CreateSourcedHook(evt: HookEvent.PostToolUse, matcher: "Bash");
        var providerMock = CreateProviderMock(preHook, postHook);

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        var preHooks = await manager.GetHooksForEventAsync(HookEvent.PreToolUse, "Bash");
        preHooks.Should().HaveCount(1);
        preHooks[0].Event.Should().Be(HookEvent.PreToolUse);

        var postHooks = await manager.GetHooksForEventAsync(HookEvent.PostToolUse, "Bash");
        postHooks.Should().HaveCount(1);
        postHooks[0].Event.Should().Be(HookEvent.PostToolUse);
    }

    [Fact]
    public async Task GetSortedMatchersAsync_返回排序后匹配器() {
        var hook1 = CreateSourcedHook(evt: HookEvent.PreToolUse, matcher: "Bash");
        var hook2 = CreateSourcedHook(evt: HookEvent.PreToolUse, matcher: "Grep", source: HookSource.ProjectSettings);
        var providerMock = CreateProviderMock(hook1, hook2);

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        var matchers = await manager.GetSortedMatchersAsync(HookEvent.PreToolUse);
        matchers.Should().Contain("Bash").And.Contain("Grep");
    }

    #endregion

    #region AddHookAsync / RemoveHookAsync — CRUD

    [Fact]
    public async Task AddHookAsync_无provider注册_抛InvalidOperationException() {
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        var act = () => manager.AddHookAsync(HookSource.UserSettings, HookEvent.PreToolUse, null, CreateHook());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No provider registered*");
    }

    [Fact]
    public async Task AddHookAsync_不可编辑source_抛InvalidOperationException() {
        var providerMock = CreateProviderMock();
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.PolicySettings, providerMock.Object);

        var act = () => manager.AddHookAsync(HookSource.PolicySettings, HookEvent.PreToolUse, null, CreateHook());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not editable*");
    }

    [Fact]
    public async Task AddHookAsync_成功_调用provider并清缓存() {
        var providerMock = CreateProviderMock(CreateSourcedHook());
        providerMock.Setup(p => p.AddHookAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        // 首次加载填充缓存
        await manager.LoadAllHooksAsync();
        // AddHook 应清除缓存
        await manager.AddHookAsync(HookSource.UserSettings, HookEvent.PreToolUse, "Bash", CreateHook());
        // 再次加载应重新调用 provider
        await manager.LoadAllHooksAsync();

        providerMock.Verify(p => p.AddHookAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        providerMock.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RemoveHookAsync_无provider注册_抛InvalidOperationException() {
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        var act = () => manager.RemoveHookAsync(HookSource.UserSettings, HookEvent.PreToolUse, null, CreateHook());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No provider registered*");
    }

    [Fact]
    public async Task RemoveHookAsync_不可编辑source_抛InvalidOperationException() {
        var providerMock = CreateProviderMock();
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.PluginHook, providerMock.Object);

        var act = () => manager.RemoveHookAsync(HookSource.PluginHook, HookEvent.PreToolUse, null, CreateHook());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not editable*");
    }

    [Fact]
    public async Task RemoveHookAsync_成功_调用provider并清缓存() {
        var providerMock = CreateProviderMock(CreateSourcedHook());
        providerMock.Setup(p => p.RemoveHookAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        await manager.LoadAllHooksAsync();
        await manager.RemoveHookAsync(HookSource.UserSettings, HookEvent.PreToolUse, null, CreateHook());
        await manager.LoadAllHooksAsync();

        providerMock.Verify(p => p.RemoveHookAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        providerMock.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task InvalidateCacheAsync_清除缓存_后续加载重新调用provider() {
        var providerMock = CreateProviderMock(CreateSourcedHook());
        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, providerMock.Object);

        await manager.LoadAllHooksAsync();
        await manager.InvalidateCacheAsync();
        await manager.LoadAllHooksAsync();

        providerMock.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region RegisterProvider

    [Fact]
    public async Task RegisterProvider_重复注册_覆盖旧provider() {
        var oldProvider = CreateProviderMock(CreateSourcedHook(command: "old"));
        var newProvider = CreateProviderMock(CreateSourcedHook(command: "new"));

        await using var manager = new HookConfigurationManager(new InMemoryFileSystem());
        manager.RegisterProvider(HookSource.UserSettings, oldProvider.Object);
        manager.RegisterProvider(HookSource.UserSettings, newProvider.Object);

        var group = await manager.LoadAllHooksAsync();
        var hooks = group.GetAllHooksForEvent(HookEvent.PreToolUse);
        hooks.Should().HaveCount(1);

        // 只有新 provider 被调用
        oldProvider.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Never);
        newProvider.Verify(p => p.LoadHooksAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

/// <summary>
/// JsonFileHookConfigurationProvider 确定性测试 — InMemoryFileSystem 消除磁盘 IO
/// <para>测试文件不存在、有效 JSON 解析、无效 JSON 容错、多事件多匹配器解析。不依赖时序。</para>
/// </summary>
public sealed class JsonFileHookConfigurationProviderTest {

    private static string SerializeHooks(HookSettingsFile settings) {
        return RelaxedJsonSerializer.Serialize(settings, HooksJsonContext.Default);
    }

    [Fact]
    public async Task LoadHooksAsync_文件不存在_返回空列表() {
        var fs = new InMemoryFileSystem();
        var provider = new JsonFileHookConfigurationProvider("/nonexistent/hooks.json", HookSource.UserSettings, fs);

        var hooks = await provider.LoadHooksAsync();

        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadHooksAsync_有效JSON_正确解析钩子() {
        var fs = new InMemoryFileSystem();
        var filePath = "/test/hooks.json";
        var settings = new HookSettingsFile {
            Hooks = new Dictionary<string, List<HookMatcher>> {
                ["preToolUse"] = new List<HookMatcher> {
                    new HookMatcher {
                        Matcher = "Bash",
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "echo hello" }
                        }
                    }
                }
            }
        };
        await fs.WriteAllTextAsync(filePath, SerializeHooks(settings));

        var provider = new JsonFileHookConfigurationProvider(filePath, HookSource.UserSettings, fs);
        var hooks = await provider.LoadHooksAsync();

        hooks.Should().HaveCount(1);
        hooks[0].Event.Should().Be(HookEvent.PreToolUse);
        hooks[0].Matcher.Should().Be("Bash");
        hooks[0].Source.Should().Be(HookSource.UserSettings);
        hooks[0].Command.Should().BeOfType<BashCommandHook>();
        ((BashCommandHook)hooks[0].Command).Command.Should().Be("echo hello");
    }

    [Fact]
    public async Task LoadHooksAsync_多事件多匹配器_全部解析() {
        var fs = new InMemoryFileSystem();
        var filePath = "/test/hooks.json";
        var settings = new HookSettingsFile {
            Hooks = new Dictionary<string, List<HookMatcher>> {
                ["preToolUse"] = new List<HookMatcher> {
                    new HookMatcher {
                        Matcher = "Bash",
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "cmd1" },
                            new BashCommandHook { Command = "cmd2" }
                        }
                    },
                    new HookMatcher {
                        Matcher = "Grep",
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "cmd3" }
                        }
                    }
                },
                ["postToolUse"] = new List<HookMatcher> {
                    new HookMatcher {
                        Matcher = null,
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "cmd4" }
                        }
                    }
                }
            }
        };
        await fs.WriteAllTextAsync(filePath, SerializeHooks(settings));

        var provider = new JsonFileHookConfigurationProvider(filePath, HookSource.ProjectSettings, fs);
        var hooks = await provider.LoadHooksAsync();

        hooks.Should().HaveCount(4);
        hooks.Count(h => h.Event == HookEvent.PreToolUse).Should().Be(3);
        hooks.Count(h => h.Event == HookEvent.PostToolUse).Should().Be(1);
        hooks.All(h => h.Source == HookSource.ProjectSettings).Should().BeTrue();
    }

    [Fact]
    public async Task LoadHooksAsync_无效JSON_返回空列表不抛异常() {
        var fs = new InMemoryFileSystem();
        var filePath = "/test/hooks.json";
        await fs.WriteAllTextAsync(filePath, "{ invalid json }}}");

        var provider = new JsonFileHookConfigurationProvider(filePath, HookSource.UserSettings, fs);
        var hooks = await provider.LoadHooksAsync();

        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadHooksAsync_空hooks字段_返回空列表() {
        var fs = new InMemoryFileSystem();
        var filePath = "/test/hooks.json";
        await fs.WriteAllTextAsync(filePath, SerializeHooks(new HookSettingsFile()));

        var provider = new JsonFileHookConfigurationProvider(filePath, HookSource.UserSettings, fs);
        var hooks = await provider.LoadHooksAsync();

        hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadHooksAsync_未知事件名_跳过该条目() {
        var fs = new InMemoryFileSystem();
        var filePath = "/test/hooks.json";
        // 用序列化构造包含未知事件键和已知事件键的 JSON
        var settings = new HookSettingsFile {
            Hooks = new Dictionary<string, List<HookMatcher>> {
                ["unknownEvent"] = new List<HookMatcher> {
                    new HookMatcher {
                        Matcher = "Bash",
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "echo unknown" }
                        }
                    }
                },
                ["preToolUse"] = new List<HookMatcher> {
                    new HookMatcher {
                        Matcher = "Bash",
                        Hooks = new List<HookCommand> {
                            new BashCommandHook { Command = "echo valid" }
                        }
                    }
                }
            }
        };
        await fs.WriteAllTextAsync(filePath, SerializeHooks(settings));

        var provider = new JsonFileHookConfigurationProvider(filePath, HookSource.UserSettings, fs);
        var hooks = await provider.LoadHooksAsync();

        // 未知事件被跳过,只解析已知事件
        hooks.Should().HaveCount(1);
        hooks[0].Event.Should().Be(HookEvent.PreToolUse);
    }
}
