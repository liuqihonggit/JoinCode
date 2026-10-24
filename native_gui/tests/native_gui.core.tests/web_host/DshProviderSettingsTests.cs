namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 模型设置页供应商面（TASK066 C8）：<c>llm/listConfigurableProviders</c> 把可路由供应商
/// 声明为引擎配置文件的编辑入口，<c>settings/describe</c> 提供 <c>llm-deepseek</c> 命名空间投影，
/// <c>settings/mutate</c> 把编辑写回 <c>providers_llm/{vendor}/config.json</c> + <c>models.json</c>。
/// </summary>
public sealed class DshProviderSettingsTests {
    private const string BaseDir = "/test/home";
    private const string SettingsPath = BaseDir + "/settings.json";
    private const string ConfigPath = BaseDir + "/providers_llm/deepseek/config.json";
    private const string ModelsPath = BaseDir + "/providers_llm/deepseek/models.json";

    /// <summary>种子 settings.json（行内 vendor 形状，与引擎拆分前的主文件同构）</summary>
    private const string SeedSettings = """
        {
          "current": { "profile": "deepseek" },
          "vendor": {
            "deepseek": {
              "provider": "deepseek",
              "protocol": "openai-compatible",
              "model": "deepseek-chat",
              "endpoint": "http://127.0.0.1:8899/v1",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "models": [{ "id": "deepseek-chat" }]
            }
          }
        }
        """;

    /// <summary>建宿主与分发器（数据目录隔离到 <see cref="BaseDir"/>；模型目录注入 deepseek + stub 两条路由）</summary>
    private static async Task<(DshHostServices Services, DshRpcDispatcher Dispatcher)> CreateHostAsync(IFileSystem fs) {
        var services = new DshHostServices(
            fs,
            "/test/dsh-workspaces.json",
            "/test/journal",
            appDataPaths: AppDataPaths.CreateForTest(appDataFolder: BaseDir));
        services.Credentials.ReadEnvironment = _ => null;
        services.Models.AttachEngine("deepseek", "deepseek-chat", new Dictionary<string, IReadOnlyList<string>> {
            ["deepseek"] = ["deepseek-chat"],
            ["stub"] = ["stub-model"],
        });
        await services.Settings.InitializeAsync(CancellationToken.None);
        await fs.WriteAllTextAsync(SettingsPath, SeedSettings);
        return (services, new DshRpcDispatcher(services));
    }

    /// <summary>构造一元调用参数（DTO + JsonContext，与前端请求体同形状）</summary>
    private static JsonElement Args<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToElement(value, typeInfo);

    /// <summary>settings/mutate 参数</summary>
    private static JsonElement MutateArgs(string ns, IReadOnlyList<DshSettingsOp> ops, int? expectedRevision = null) =>
        Args(new DshSettingsMutateArgs { Ns = ns, Ops = [.. ops], ExpectedRevision = expectedRevision },
            DshJsonContext.Default.DshSettingsMutateArgs);

    /// <summary>字符串值 set 操作</summary>
    private static DshSettingsOp SetString(string path, string value) => new() {
        Op = "set",
        Path = [.. path.Split('/')],
        Value = JsonSerializer.SerializeToElement(value, DshJsonContext.Default.String),
    };

    /// <summary>模型列表 set 操作（数组值为 DTO 序列化，避免手写 JSON）</summary>
    private static DshSettingsOp SetModels(string path, params string[] ids) => new() {
        Op = "set",
        Path = [.. path.Split('/')],
        Value = Args(ids.Select(id => new DshProviderModelValue { Id = id }).ToList(),
            DshJsonContext.Default.ListDshProviderModelValue),
    };

    [Fact]
    public async Task ListConfigurableProviders_DeclaresRoutableVendorsWithEngineProfilePath() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync("llm/listConfigurableProviders", default, CancellationToken.None);

        result.Ok.Should().BeTrue(result.Error?.Message);
        var declared = ReadDto(result.Value.GetValueOrDefault(), DshJsonContext.Default.ListDshConfigurableProvider, "listConfigurableProviders");
        declared.Select(entry => entry.Provider).Should().BeEquivalentTo(["deepseek", "stub"]);
        declared.Should().OnlyContain(entry => entry.SettingsNs == "llm-deepseek");
        var deepseek = declared.Single(entry => entry.Provider == "deepseek");
        deepseek.DisplayName.Should().Be("deepseek");
        deepseek.SettingsPath.Should().Equal("profiles", "deepseek");
    }

    [Fact]
    public async Task Describe_ProjectsEngineVendorProfileIntoProviderNamespace() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync("settings/describe", default, CancellationToken.None);

        result.Ok.Should().BeTrue(result.Error?.Message);
        var describe = ReadDto(result.Value.GetValueOrDefault(), DshJsonContext.Default.DshSettingsDescribe, "settings/describe");
        describe.Writable.Should().BeTrue();
        var view = describe.Namespaces.Single(candidate => candidate.Ns == "llm-deepseek");
        var profile = view.Value.GetProperty("profiles").GetProperty("deepseek");
        profile.GetProperty("baseURL").GetString().Should().Be("http://127.0.0.1:8899/v1");
        profile.GetProperty("apiKeyEnv").GetString().Should().Be("DEEPSEEK_API_KEY");
        profile.GetProperty("model").GetString().Should().Be("deepseek-chat");
        profile.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("id").GetString())
            .Should().Equal("deepseek-chat");
        view.User.GetProperty("profiles").TryGetProperty("deepseek", out _).Should().BeTrue("编辑草稿从 user 层读取");
        view.Base.ValueKind.Should().Be(JsonValueKind.Object, "供应商生命周期归引擎配置，模型页只做就地编辑（不提供删除）");
        view.Schema.ValueKind.Should().Be(JsonValueKind.Object, "前端 ConfigForm 需要 schema 才能解析 settingsPath");
        view.Schema.GetProperty("refs").GetProperty(view.Schema.GetProperty("uid").GetInt32().ToString())
            .GetProperty("dict").TryGetProperty("profiles", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Mutate_WritesEngineVendorConfigAndModelList() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [
                SetString("profiles/deepseek/baseURL", "http://localhost:9999/v1"),
                SetModels("profiles/deepseek/models", "m1", "m2"),
            ]),
            CancellationToken.None);

        result.Ok.Should().BeTrue(result.Error?.Message);
        var view = ReadDto(result.Value.GetValueOrDefault(), DshJsonContext.Default.DshSettingsNamespaceView, "settings/mutate");
        var profile = view.Value.GetProperty("profiles").GetProperty("deepseek");
        profile.GetProperty("baseURL").GetString().Should().Be("http://localhost:9999/v1");
        profile.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("id").GetString())
            .Should().Equal("m1", "m2");

        var config = NonNull(
            JsonSerializer.Deserialize(await fs.ReadAllTextAsync(ConfigPath), ConfigJsonContext.Default.VendorConfigFile),
            "providers_llm/deepseek/config.json");
        config.Endpoint.Should().Be("http://localhost:9999/v1");
        config.ApiKeyEnvVar.Should().Be("DEEPSEEK_API_KEY", "未编辑的字段必须原样保留");
        config.Protocol.Should().Be("openai-compatible");
        config.Model.Should().Be("deepseek-chat");
        var models = NonNull(
            JsonSerializer.Deserialize(await fs.ReadAllTextAsync(ModelsPath), ConfigJsonContext.Default.VendorModelsFile),
            "providers_llm/deepseek/models.json");
        models.Models.Select(model => model.Id).Should().Equal("m1", "m2");
    }

    [Fact]
    public async Task Mutate_ReloadsModelCatalogForProviderList() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var mutate = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [SetModels("profiles/deepseek/models", "m1")]),
            CancellationToken.None);
        mutate.Ok.Should().BeTrue(mutate.Error?.Message);

        var providers = await dispatcher.HandleAsync("llm/listProviders", default, CancellationToken.None);
        var groups = ReadDto(providers.Value.GetValueOrDefault(), DshJsonContext.Default.ListDshProviderInfo, "llm/listProviders");
        groups.Single(group => group.Id == "deepseek").Name.Should().Be("deepseek");
        var catalog = await dispatcher.HandleAsync("session/modelCatalog", default, CancellationToken.None);
        var models = catalog.Value.GetValueOrDefault().GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("id").GetString() == "deepseek")
            .GetProperty("models").EnumerateArray().Select(model => model.GetProperty("id").GetString());
        models.Should().Equal("m1");
    }

    [Fact]
    public async Task Mutate_RejectsUnknownVendorWithWritableGuidance() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [SetString("profiles/nope/baseURL", "http://x")]),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/rejected");
        result.Error?.Message.Should().Contain("deepseek", "引导必须列出可编辑的供应商");
    }

    [Fact]
    public async Task Mutate_RejectsUnknownProfileFieldWithGuidance() {
        await using var inMemory = new InMemoryFileSystem();

        var result = await MutateAsync(inMemory, SetString("profiles/deepseek/protocol", "anthropic"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/rejected");
        result.Error?.Message.Should().Contain("baseURL").And.Contain("models");
    }

    [Fact]
    public async Task Mutate_HonorsExpectedRevision() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [SetString("profiles/deepseek/baseURL", "http://x")], expectedRevision: 99),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/conflict");
    }

    [Fact]
    public async Task Mutate_ConsecutiveSavesFromModelsEditorBothSucceed() {
        // 模型页编辑器取打开时的 namespace.revision 提交，保存成功后不回写 describe 镜像；
        // 虚拟命名空间（引擎文件投影）的 revision 必须对自身写入稳定，否则第二次保存必冲突
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var first = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [SetString("profiles/deepseek/baseURL", "http://first")], expectedRevision: 0),
            CancellationToken.None);
        var second = await dispatcher.HandleAsync(
            "settings/mutate",
            MutateArgs("llm-deepseek", [SetString("profiles/deepseek/apiKeyEnv", "OTHER_KEY")], expectedRevision: 0),
            CancellationToken.None);

        first.Ok.Should().BeTrue(first.Error?.Message);
        second.Ok.Should().BeTrue(second.Error?.Message);
    }

    [Fact]
    public async Task Replace_ProviderNamespaceRejectedWithNextStep() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "settings/replace",
            Args(new DshSettingsReplaceArgs {
                Ns = "llm-deepseek",
                Section = JsonSerializer.SerializeToElement<object?>(null, DshJsonContext.Default.Object),
            }, DshJsonContext.Default.DshSettingsReplaceArgs),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("settings/rejected");
        result.Error?.Message.Should().Contain("settings/mutate", "错误提示必须给出正确写法");
    }

    /// <summary>反序列化响应载荷为 DTO（失败即抛，测试不吞协议偏差）</summary>
    private static T ReadDto<T>(JsonElement element, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string method) =>
        JsonSerializer.Deserialize(element, typeInfo) ?? throw new InvalidOperationException($"{method} 响应无法反序列化为 DTO：{element.GetRawText()}");

    /// <summary>非空断言（避免测试里使用 null-forgiving）</summary>
    private static T NonNull<T>(T? value, string what) where T : class =>
        value ?? throw new InvalidOperationException($"{what} 内容无法反序列化");

    /// <summary>用一条 set 操作触发供应商命名空间 mutate（未知字段 / 未知供应商测试共用）</summary>
    private static async Task<DshRpcResult> MutateAsync(IFileSystem fs, DshSettingsOp op) {
        var (_, dispatcher) = await CreateHostAsync(fs);
        return await dispatcher.HandleAsync("settings/mutate", MutateArgs("llm-deepseek", [op]), CancellationToken.None);
    }
}
