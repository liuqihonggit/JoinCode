namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// TASK066 C8 <c>sessionReferenceResolver/candidates</c> + <c>credentials/*</c> + <c>llm/discoverModels</c> 测试：
/// @ 菜单列出同工作区会话（mention/label/时间），模型页密钥写入/移除走共享 auth.json，
/// 模型发现按协议推导端点与鉴权头。
/// </summary>
public sealed class DshReferenceAndCredentialsTests {
    private const string SessionsDir = "/test/sessions";
    private const string JournalDir = "/test/dsh_journal";
    private static readonly string AppDataFolder = Path.Combine(Path.GetTempPath(), "jcc_ref_cred_test", ".jcc");
    private static readonly string AuthPath = Path.Combine(AppDataFolder, "auth.json");
    private static readonly string WorkA = Path.Combine(Path.GetTempPath(), "jcc_ref_test", "work_a");
    private static readonly string WorkB = Path.Combine(Path.GetTempPath(), "jcc_ref_test", "work_b");

    /// <summary>建一个带真实转录与事件日志的宿主服务与分发器（模型目录注入 deepseek 供应商）</summary>
    private static async Task<(DshHostServices Services, DshRpcDispatcher Dispatcher, State.TranscriptService Transcripts)> CreateHostAsync(IFileSystem fs) {
        var transcripts = new State.TranscriptService(fs, SessionsDir);
        var services = new DshHostServices(
            fs,
            "/test/dsh-workspaces.json",
            JournalDir,
            appDataPaths: AppDataPaths.CreateForTest(appDataFolder: AppDataFolder));
        services.Sessions.AttachTranscripts(transcripts);
        services.Credentials.ReadEnvironment = _ => null;
        services.Models.AttachEngine("deepseek", "deepseek-chat", new Dictionary<string, IReadOnlyList<string>> {
            ["deepseek"] = ["deepseek-chat"],
        });
        await services.Settings.InitializeAsync(CancellationToken.None);
        return (services, new DshRpcDispatcher(services), transcripts);
    }

    private static JsonElement Args<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToElement(value, typeInfo);

    /// <summary>断言成功并取出 value 反序列化（失败时打印网关错误）</summary>
    private static TValue ReadValue<TValue>(DshRpcResult result, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> typeInfo) {
        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.HasValue.Should().BeTrue("成功结果必须带 value");
        var value = JsonSerializer.Deserialize(result.Value.GetValueOrDefault(), typeInfo);
        return value ?? throw new InvalidOperationException($"无法反序列化 {typeof(TValue).Name}");
    }

    /// <summary>建一个会话实体（含工作目录与创建时间）</summary>
    private static Task AddSessionAsync(State.TranscriptService transcripts, string sessionId, string cwd, DateTime? createdAt = null) =>
        transcripts.EnsureSessionAsync(sessionId, new SessionInfo {
            Id = sessionId,
            ProjectPath = cwd,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        }, CancellationToken.None);

    [Fact]
    public async Task Candidates_ListsMatchingSessionsWithMentionAndWorkspaceFlag() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, transcripts) = await CreateHostAsync(fs);
        await AddSessionAsync(transcripts, "current", WorkA);
        await AddSessionAsync(transcripts, "s1", WorkA, new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        await AddSessionAsync(transcripts, "s2", WorkB, new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc));
        await transcripts.SaveCustomTitleAsync("s1", "修复登录", CancellationToken.None);

        var result = await dispatcher.HandleAsync(
            "sessionReferenceResolver/candidates",
            Args(new DshSessionReferenceCandidatesArgs { AgentId = "current", Query = "登录" }, DshJsonContext.Default.DshSessionReferenceCandidatesArgs));

        var candidates = ReadValue(result, DshJsonContext.Default.ListDshSessionReferenceCandidate);
        candidates.Should().ContainSingle();
        var candidate = candidates[0];
        candidate.SessionId.Should().Be("s1");
        candidate.Label.Should().Be("修复登录");
        candidate.DisplayTitle.Should().Be("修复登录");
        candidate.Mention.Should().Be("@修复登录");
        candidate.Cwd.Should().Be(WorkA);
        candidate.SameWorkspace.Should().BeTrue();
        candidate.CreatedAt.Should().Be(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Candidates_EmptyQueryExcludesCurrentSessionAndOrdersSameWorkspaceFirst() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, transcripts) = await CreateHostAsync(fs);
        await AddSessionAsync(transcripts, "current", WorkA);
        await AddSessionAsync(transcripts, "other", WorkB, new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc));
        await AddSessionAsync(transcripts, "same", WorkA, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await transcripts.SaveCustomTitleAsync("other", "另一个会话", CancellationToken.None);

        var result = await dispatcher.HandleAsync(
            "sessionReferenceResolver/candidates",
            Args(new DshSessionReferenceCandidatesArgs { AgentId = "current" }, DshJsonContext.Default.DshSessionReferenceCandidatesArgs));

        var candidates = ReadValue(result, DshJsonContext.Default.ListDshSessionReferenceCandidate);
        candidates.Select(candidate => candidate.SessionId).Should().Equal("same", "other");
        candidates[0].SameWorkspace.Should().BeTrue();
        candidates[1].SameWorkspace.Should().BeFalse();
        candidates[0].DisplayTitle.Should().BeNull();
        candidates[0].Label.Should().Be("same");
    }

    [Fact]
    public async Task Candidates_UnknownAgentReturnsContextNotFound() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, _) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "sessionReferenceResolver/candidates",
            Args(new DshSessionReferenceCandidatesArgs { AgentId = "missing", Query = "x" }, DshJsonContext.Default.DshSessionReferenceCandidatesArgs));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("gateway/context-not-found");
        result.Error?.Message.Should().Contain("missing");
    }

    [Fact]
    public async Task CredentialsSet_StoresUnderProviderKeyAndDescribeReportsConfigured() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, _) = await CreateHostAsync(fs);

        var set = await dispatcher.HandleAsync(
            "credentials/set",
            Args(new DshCredentialsSetArgs { Ref = "DEEPSEEK_API_KEY", Value = "sk-gui-secret" }, DshJsonContext.Default.DshCredentialsSetArgs));
        set.Ok.Should().BeTrue(set.Error?.Message);

        var authJson = await fs.ReadAllTextAsync(AuthPath);
        authJson.Should().Contain("\"deepseek\"", "密钥按引擎供应商键写入共享 auth.json");
        if (OperatingSystem.IsWindows()) {
            authJson.Should().NotContain("sk-gui-secret", "Windows 落盘必须 DPAPI 加密");
        } else {
            authJson.Should().Contain("sk-gui-secret", "非 Windows 无 DPAPI，ApiKeyProtector 降级明文落盘");
        }

        var describe = await dispatcher.HandleAsync(
            "credentials/describe",
            Args(new DshCredentialsDescribeArgs { Refs = ["DEEPSEEK_API_KEY", "UNKNOWN_API_KEY"] }, DshJsonContext.Default.DshCredentialsDescribeArgs));
        var states = ReadValue(describe, DshJsonContext.Default.DictionaryStringDshCredentialState);
        states["DEEPSEEK_API_KEY"].Configured.Should().BeTrue();
        states["DEEPSEEK_API_KEY"].Source.Should().Be("stored");
        states["DEEPSEEK_API_KEY"].Writable.Should().BeTrue();
        states["UNKNOWN_API_KEY"].Configured.Should().BeFalse();
        states["UNKNOWN_API_KEY"].Writable.Should().BeFalse();
    }

    [Fact]
    public async Task CredentialsUnset_RemovesStoredKeyAndRejectsUnknownRef() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, _) = await CreateHostAsync(fs);
        await dispatcher.HandleAsync(
            "credentials/set",
            Args(new DshCredentialsSetArgs { Ref = "DEEPSEEK_API_KEY", Value = "sk-to-remove" }, DshJsonContext.Default.DshCredentialsSetArgs));

        var unset = await dispatcher.HandleAsync(
            "credentials/unset",
            Args(new DshCredentialsUnsetArgs { Ref = "DEEPSEEK_API_KEY" }, DshJsonContext.Default.DshCredentialsUnsetArgs));
        unset.Ok.Should().BeTrue(unset.Error?.Message);

        var describe = await dispatcher.HandleAsync(
            "credentials/describe",
            Args(new DshCredentialsDescribeArgs { Refs = ["DEEPSEEK_API_KEY"] }, DshJsonContext.Default.DshCredentialsDescribeArgs));
        var states = ReadValue(describe, DshJsonContext.Default.DictionaryStringDshCredentialState);
        states["DEEPSEEK_API_KEY"].Configured.Should().BeFalse();

        var rejected = await dispatcher.HandleAsync(
            "credentials/unset",
            Args(new DshCredentialsUnsetArgs { Ref = "NOT_A_ROUTE" }, DshJsonContext.Default.DshCredentialsUnsetArgs));
        rejected.Ok.Should().BeFalse();
        rejected.Error?.Code.Should().Be("credentials/rejected");
        rejected.Error?.Message.Should().Contain("DEEPSEEK_API_KEY");
    }

    [Fact]
    public async Task CredentialsUnset_EnvironmentKeyWinsEvenWhenStoredKeyExists() {
        await using var fs = new InMemoryFileSystem();
        var (services, dispatcher, _) = await CreateHostAsync(fs);
        await dispatcher.HandleAsync(
            "credentials/set",
            Args(new DshCredentialsSetArgs { Ref = "DEEPSEEK_API_KEY", Value = "sk-stored-shadowed" }, DshJsonContext.Default.DshCredentialsSetArgs));
        services.Credentials.ReadEnvironment = name => name == "DEEPSEEK_API_KEY" ? "sk-from-env" : null;

        var unset = await dispatcher.HandleAsync(
            "credentials/unset",
            Args(new DshCredentialsUnsetArgs { Ref = "DEEPSEEK_API_KEY" }, DshJsonContext.Default.DshCredentialsUnsetArgs));

        unset.Ok.Should().BeFalse("环境变量遮蔽已存密钥，删除已存密钥也不会改变生效密钥");
        unset.Error?.Code.Should().Be("credentials/rejected");
        unset.Error?.Message.Should().Contain("DEEPSEEK_API_KEY");
        var authJson = await fs.ReadAllTextAsync(AuthPath);
        authJson.Should().Contain("\"deepseek\"", "拒绝移除时不得改动已存密钥");
    }

    [Fact]
    public async Task CredentialsDescribe_ReportsEnvironmentSourcedKey() {
        await using var fs = new InMemoryFileSystem();
        var (services, dispatcher, _) = await CreateHostAsync(fs);
        services.Credentials.ReadEnvironment = name => name == "DEEPSEEK_API_KEY" ? "sk-from-env" : null;

        var describe = await dispatcher.HandleAsync(
            "credentials/describe",
            Args(new DshCredentialsDescribeArgs { Refs = ["DEEPSEEK_API_KEY"] }, DshJsonContext.Default.DshCredentialsDescribeArgs));

        var states = ReadValue(describe, DshJsonContext.Default.DictionaryStringDshCredentialState);
        states["DEEPSEEK_API_KEY"].Configured.Should().BeTrue();
        states["DEEPSEEK_API_KEY"].Source.Should().Be("env");
    }

    [Fact]
    public async Task DiscoverModels_OpenAiProtocolUsesBearersAndMapsContextLength() {
        await using var fs = new InMemoryFileSystem();
        var (services, dispatcher, _) = await CreateHostAsync(fs);
        using var handler = new RecordingHandler("""
            {"data":[{"id":"m1","context_length":128000},{"id":"m2","name":"模型二"}]}
            """);
        services.Discovery.HandlerFactory = () => handler;

        var result = await dispatcher.HandleAsync(
            "llm/discoverModels",
            Args(new DshDiscoverModelsArgs {
                SettingsNs = "",
                Request = new DshModelDiscoveryRequest {
                    BaseUrl = "https://api.example.com/v1",
                    Api = "openai-completions",
                    ApiKey = "sk-discover",
                },
            }, DshJsonContext.Default.DshDiscoverModelsArgs));

        var models = ReadValue(result, DshJsonContext.Default.ListDshDiscoveredModel);
        handler.RequestUri.Should().Be("https://api.example.com/v1/models");
        handler.Authorization.Should().Be("Bearer sk-discover");
        handler.AnthropicVersion.Should().BeNull();

        models.Should().HaveCount(2);
        models[0].Id.Should().Be("m1");
        models[0].ContextWindow.Should().Be(128000);
        models[0].Name.Should().BeNull();
        models[1].Name.Should().Be("模型二");
    }

    [Fact]
    public async Task DiscoverModels_AnthropicProtocolUsesApiKeyHeaderAndStoredCredential() {
        await using var fs = new InMemoryFileSystem();
        var (services, dispatcher, _) = await CreateHostAsync(fs);
        await dispatcher.HandleAsync(
            "credentials/set",
            Args(new DshCredentialsSetArgs { Ref = "DEEPSEEK_API_KEY", Value = "sk-stored" }, DshJsonContext.Default.DshCredentialsSetArgs));
        using var handler = new RecordingHandler("""
            {"data":[{"id":"claude-x","display_name":"Claude X","max_tokens":8192}]}
            """);
        services.Discovery.HandlerFactory = () => handler;

        var result = await dispatcher.HandleAsync(
            "llm/discoverModels",
            Args(new DshDiscoverModelsArgs {
                Request = new DshModelDiscoveryRequest {
                    Provider = "deepseek",
                    BaseUrl = "https://api.anthropic.com",
                    Api = "anthropic-messages",
                },
            }, DshJsonContext.Default.DshDiscoverModelsArgs));

        var models = ReadValue(result, DshJsonContext.Default.ListDshDiscoveredModel);
        handler.RequestUri.Should().Be("https://api.anthropic.com/v1/models");
        handler.ApiKeyHeader.Should().Be("sk-stored");
        handler.Authorization.Should().BeNull();
        handler.AnthropicVersion.Should().Be("2023-06-01");

        models.Should().ContainSingle();
        models[0].Id.Should().Be("claude-x");
        models[0].Name.Should().Be("Claude X");
        models[0].MaxTokens.Should().Be(8192);
    }

    [Fact]
    public async Task DiscoverModels_WithoutBaseUrlReturnsGuidance() {
        await using var fs = new InMemoryFileSystem();
        var (_, dispatcher, _) = await CreateHostAsync(fs);

        var result = await dispatcher.HandleAsync(
            "llm/discoverModels",
            Args(new DshDiscoverModelsArgs { Request = new DshModelDiscoveryRequest { Provider = "deepseek" } }, DshJsonContext.Default.DshDiscoverModelsArgs));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("llm/discoverModels-rejected");
        result.Error?.Message.Should().Contain("baseURL");
    }

    /// <summary>记录请求并以固定 JSON 应答的 HTTP 处理器（模型发现测试用）</summary>
    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler {
        internal string? RequestUri { get; private set; }
        internal string? Authorization { get; private set; }
        internal string? ApiKeyHeader { get; private set; }
        internal string? AnthropicVersion { get; private set; }

        /// <summary>捕获请求头并返回固定 JSON</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.FirstOrDefault() : null;
            ApiKeyHeader = request.Headers.TryGetValues("x-api-key", out var apiKey) ? apiKey.FirstOrDefault() : null;
            AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var version) ? version.FirstOrDefault() : null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }
}
