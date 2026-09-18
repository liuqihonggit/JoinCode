namespace Core.Skills;

/// <summary>
/// 技能服务配置选项
/// </summary>
[Register(typeof(SkillOptions), ServiceLifetime.Singleton)]
public sealed record SkillOptions
{
    /// <summary>
    /// 技能目录路径
    /// </summary>
    public string SkillsDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder, "skills");
    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public SkillOptions() { }

    /// <summary>
    /// 从工作流配置创建技能选项
    /// </summary>
    /// <param name="config">工作流配置；为 null 则使用默认值</param>
    public SkillOptions(WorkflowConfig? config)
    {
        SkillsDirectory = config is not null && !string.IsNullOrEmpty(config.SkillsDirectory)
            ? config.SkillsDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppDataConstants.AppDataFolder, "skills");
    }

    /// <summary>
    /// 从工作流配置创建技能选项
    /// </summary>
    /// <param name="config">工作流配置</param>
    /// <returns>技能选项实例</returns>
    public static SkillOptions FromConfig(WorkflowConfig? config) => new(config);
}

/// <summary>
/// 技能服务 — 管理技能注册、查找、执行和重载，集成 MCP 远程技能
/// </summary>
[Register(typeof(ISkillService), ServiceLifetime.Singleton)]
public sealed partial class SkillService : ServiceEntity, ISkillService, IDisposable
{
    private readonly SkillOptions _options;
    private readonly IFileOperationService _files;
    private readonly Core.Skills.Mcp.IMcpSkillProvider? _mcpSkillProvider;
    private readonly MiddlewarePipeline<SkillContext> _pipeline;
    private readonly SkillServiceActor _actor;
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills;
    private readonly Core.Skills.Discovery.ISkillDiscoveryService? _discoveryService;
    private readonly ILogger<SkillService>? _logger;
    private DateTime _lastReloadTime = DateTime.MinValue;

    /// <summary>
    /// 创建技能服务
    /// </summary>
    /// <param name="options">技能选项</param>
    /// <param name="files">文件操作服务</param>
    /// <param name="pipeline">中间件管道</param>
    /// <param name="discoveryService">技能发现服务</param>
    /// <param name="mcpSkillProvider">MCP 技能提供者</param>
    /// <param name="logger">日志记录器</param>
    public SkillService(
        SkillOptions options,
        IFileOperationService files,
        MiddlewarePipeline<SkillContext> pipeline,
        Core.Skills.Discovery.ISkillDiscoveryService? discoveryService = null,
        Core.Skills.Mcp.IMcpSkillProvider? mcpSkillProvider = null,
        ILogger<SkillService>? logger = null
        )
    {
        Diag.WriteLine("[SKILL-CTOR] 1 assign fields");
        _options = options;
        _files = files;
        _pipeline = pipeline;
        _discoveryService = discoveryService;
        _mcpSkillProvider = mcpSkillProvider;
        _logger = logger;

        _skills = new ConcurrentDictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);

        Diag.WriteLine("[SKILL-CTOR] 2 LoadBuiltInSkills");
        LoadBuiltInSkills();
        _actor = new SkillServiceActor(this, _logger);
        Diag.WriteLine("[SKILL-CTOR] 3 done");
    }

    /// <summary>
    /// 异步执行指定技能 — 优先查找本地技能，未命中时尝试 MCP 远程技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="parameters">调用参数</param>
    /// <param name="ctx">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能执行结果</returns>
    public async Task<SkillResult> ExecuteAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        // 优先查找本地技能
        var skill = await GetSkillAsync(skillName, cancellationToken).ConfigureAwait(false);

        // 本地技能不存在时，尝试 MCP 远程技能 — 对齐 TS executeRemoteSkill
        if (skill == null && _mcpSkillProvider is not null && _mcpSkillProvider.IsSkillAvailable(skillName))
        {
            return await ExecuteMcpSkillAsync(skillName, parameters, ctx, cancellationToken).ConfigureAwait(false);
        }

        if (skill == null)
        {
            return SkillResult.FailureResult(skillName, string.Format(ContractsErrorMessages.SkillNotFound, skillName));
        }

        // 本地技能通过中间件管道执行
        var context = new SkillContext
        {
            SkillName = skillName,
            Parameters = parameters ?? [],
            Skill = skill,
            ExecutionContext = ctx,
            CancellationToken = cancellationToken
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        return context.Result ?? SkillResult.FailureResult(skillName, "Pipeline completed without result");
    }

    /// <summary>
    /// 异步获取所有可用技能 — 合并本地技能和 MCP 远程技能
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有可用技能定义列表</returns>
    public async Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken cancellationToken = default)
    {
        var localSkills = _skills.Values;

        // 合并 MCP 远程技能 — 对齐 TS getAllCommands 合并本地+MCP技能
        if (_mcpSkillProvider is not null)
        {
            var mcpSkills = await _mcpSkillProvider.GetMcpSkillsAsync(cancellationToken).ConfigureAwait(false);
            if (mcpSkills.Count > 0)
            {
                var combined = new List<SkillDefinition>(_skills.Count + mcpSkills.Count);
                combined.AddRange(localSkills);
                combined.AddRange(mcpSkills);
                return combined;
            }
        }

        return localSkills.ToList();
    }

    /// <summary>
    /// 按名称异步获取技能定义 — 先查本地，未命中再查 MCP 远程技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能定义；不存在则返回 null</returns>
    public async Task<SkillDefinition?> GetSkillAsync(string skillName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        var local = _skills.GetValueOrDefault(skillName);
        if (local is not null) return local;

        // 查找 MCP 远程技能
        if (_mcpSkillProvider is not null && _mcpSkillProvider.IsSkillAvailable(skillName))
        {
            var mcpSkills = await _mcpSkillProvider.GetMcpSkillsAsync(cancellationToken).ConfigureAwait(false);
            return mcpSkills.FirstOrDefault(s => string.Equals(s.Name, skillName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// 判断指定名称的技能是否存在 — 包含本地技能和 MCP 远程技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    public bool SkillExists(string skillName)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        if (_skills.ContainsKey(skillName)) return true;

        // 检查 MCP 远程技能
        return _mcpSkillProvider is not null && _mcpSkillProvider.IsSkillAvailable(skillName);
    }

    /// <summary>
    /// 异步重载技能 — 指定名称则重载单个技能，否则重载全部
    /// </summary>
    /// <param name="skillName">技能名称；为 null 则重载全部</param>
    /// <param name="ctx">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重载成功返回 true，否则返回 false</returns>
    public async Task<bool> ReloadAsync(string? skillName, ExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        var reply = new TaskCompletionSource<bool>();
        await _actor.SendAsync(new ReloadCmd(skillName, ctx, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重载内部实现 — 由 SkillServiceActor Consumer 串行调用，天然无竞态，无需锁
    /// </summary>
    /// <param name="skillName">技能名称；为 null 则重载全部</param>
    /// <param name="ctx">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重载成功返回 true，否则返回 false</returns>
    private async Task<bool> ReloadInternalAsync(string? skillName, ExecutionContext ctx, CancellationToken cancellationToken)
    {
        try
        {
            if (skillName != null)
            {
                var skill = await LoadSkillFromFileAsync(skillName, ctx).ConfigureAwait(false);
                if (skill != null)
                {
                    _skills[skillName] = skill;
                    ctx.Logger?.LogInformation(L.T(StringKey.SkillServiceReloaded), skillName);
                    return true;
                }
                return false;
            }

            _skills.Clear();
            LoadBuiltInSkills();
            await LoadExternalSkillsAsync(ctx.CancellationToken).ConfigureAwait(false);
            _lastReloadTime = DateTime.UtcNow;
            ctx.Logger?.LogInformation(L.T(StringKey.SkillServiceReloadAll));
            return true;
        }
        catch (Exception ex)
        {
            ctx.Logger?.LogError(ex, L.T(StringKey.SkillServiceReloadFailed));
            return false;
        }
    }

    /// <summary>
    /// 注册技能定义
    /// </summary>
    /// <param name="skill">技能定义</param>
    public void RegisterSkill(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _skills[skill.Name] = skill;
    }

    /// <summary>
    /// 注销指定名称的技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <returns>注销成功返回 true，否则返回 false</returns>
    public bool UnregisterSkill(string skillName)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        return _skills.TryRemove(skillName, out _);
    }

    #region 私有方法

    /// <summary>
    /// 执行 MCP 远程技能 — 对齐 TS executeRemoteSkill
    /// </summary>
    private async Task<SkillResult> ExecuteMcpSkillAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var mcpSkillProvider = _mcpSkillProvider ?? throw new InvalidOperationException("McpSkillProvider not available.");
        ctx.Logger?.LogInformation("执行 MCP 远程技能: {SkillName}", skillName);

        try
        {
            var result = await mcpSkillProvider.ExecuteMcpSkillAsync(skillName, parameters, ctx, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            ctx.Logger?.LogInformation("MCP 远程技能 {SkillName} 执行完成，耗时 {Ms}ms", skillName, stopwatch.ElapsedMilliseconds);
            return result with { DurationMs = stopwatch.ElapsedMilliseconds };
        }
        catch (OperationCanceledException)
        {
            return SkillResult.FailureResult(skillName, "MCP 远程技能执行被取消");
        }
        catch (Exception ex)
        {
            ctx.Logger?.LogError(ex, "MCP 远程技能 {SkillName} 执行失败", skillName);
            return SkillResult.FailureResult(skillName, $"MCP 远程技能执行失败: {ex.Message}");
        }
    }

    private void LoadBuiltInSkills()
    {
        RegisterSkill(BuiltIn.VerifySkill.CreateDefinition());
        RegisterSkill(BuiltIn.DebugSkill.CreateDefinition());
        RegisterSkill(BuiltIn.BatchSkill.CreateDefinition());
        RegisterSkill(BuiltIn.StuckSkill.CreateDefinition());
        RegisterSkill(BuiltIn.HunterSkill.CreateDefinition());
        RegisterSkill(BuiltIn.LoopSkill.CreateDefinition());
        RegisterSkill(BuiltIn.RememberSkill.CreateDefinition());
        RegisterSkill(BuiltIn.SimplifySkill.CreateDefinition());
        RegisterSkill(BuiltIn.SkillifySkill.CreateDefinition());
        RegisterSkill(BuiltIn.UpdateConfigSkill.CreateDefinition());
        RegisterSkill(BuiltIn.KeybindingsSkill.CreateDefinition());
    }

    private async Task LoadExternalSkillsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_files.DirectoryExists(_options.SkillsDirectory))
            {
                _files.CreateDirectory(_options.SkillsDirectory);
                return;
            }

            var jsonFiles = _files.GetFiles(_options.SkillsDirectory, "*.json", System.IO.SearchOption.AllDirectories);
            var jsonTasks = jsonFiles.Select(filePath => LoadSkillFromJsonFileAsync(filePath, cancellationToken));
            await Task.WhenAll(jsonTasks).ConfigureAwait(false);

            var mdFiles = _files.GetFiles(_options.SkillsDirectory, "SKILL.md", System.IO.SearchOption.AllDirectories);
            var mdTasks = mdFiles.Select(filePath => LoadSkillFromMarkdownFileAsync(filePath, cancellationToken));
            await Task.WhenAll(mdTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 技能加载失败不应阻止应用启动，但需记录日志
            _logger?.LogWarning(ex, "SkillService.LoadExternalSkillsAsync 失败");
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await LoadExternalSkillsAsync(cancellationToken).ConfigureAwait(false);

        if (_discoveryService != null)
        {
            try
            {
                var discovered = await _discoveryService.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                foreach (var skill in discovered)
                {
                    if (skill.Definition != null)
                    {
                        _skills.TryAdd(skill.Definition.Name, skill.Definition);
                    }
                }
            }
            catch (Exception ex)
            {
                // 技能发现失败不应阻止应用启动，但需记录日志
                _logger?.LogWarning(ex, "SkillService.DiscoverAsync 失败");
            }
        }
    }

    private async Task<SkillDefinition?> LoadSkillFromFileAsync(string skillName, ExecutionContext ctx)
    {
        var jsonPath = System.IO.Path.Combine(_options.SkillsDirectory, $"{skillName}.json");
        if (_files.FileExists(jsonPath))
        {
            return await LoadSkillFromJsonFileAsync(jsonPath, ctx.CancellationToken).ConfigureAwait(false);
        }

        var mdPath = System.IO.Path.Combine(_options.SkillsDirectory, skillName, "SKILL.md");
        if (_files.FileExists(mdPath))
        {
            return await LoadSkillFromMarkdownFileAsync(mdPath, ctx.CancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<SkillDefinition?> LoadSkillFromJsonFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _files.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return null;
            }

            var skill = RelaxedJsonSerializer.Deserialize(result.Content, SkillsJsonContext.Default.SkillDefinition);
            if (skill != null)
            {
                var lastModified = await _files.GetLastWriteTimeUtcAsync(filePath, cancellationToken).ConfigureAwait(false);
                skill = skill with { SourcePath = filePath, SourceFormat = SkillSourceFormat.Json, LastModified = lastModified };
                _skills[skill.Name] = skill;
            }
            return skill;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SkillDefinition?> LoadSkillFromMarkdownFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _files.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return null;
            }

            var content = result.Content;
            var frontmatter = ParseFrontmatter(content);

            var skillName = frontmatter.TryGetValue("name", out var name) ? name : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(filePath)) ?? "unknown";
            var description = frontmatter.TryGetValue("description", out var desc) ? desc : string.Empty;

            var skill = new SkillDefinition
            {
                Name = skillName,
                Description = description,
                Parameters = new Dictionary<string, SkillParameter>(),
                Steps = new List<SkillStep>
                {
                    new() { Id = "execute", Type = SkillStepType.Prompt, Prompt = content, Description = description }
                },
                Context = frontmatter.TryGetValue("context", out var ctx) && SkillExecutionModeExtensions.FromValue(ctx) is { } contextMode
                    ? contextMode : SkillExecutionMode.Inline,
                Isolation = frontmatter.TryGetValue("isolation", out var iso) && AgentIsolationModeExtensions.FromValue(iso) is { } isolationMode
                    ? isolationMode : AgentIsolationMode.None,
                AllowedTools = frontmatter.TryGetValue("allowed_tools", out var tools) ? ParseListField(tools) : Array.Empty<string>(),
                Model = frontmatter.TryGetValue("model", out var model) ? model : null,
                Effort = frontmatter.TryGetValue("effort", out var effort) ? effort : null,
                DisableModelInvocation = frontmatter.TryGetValue("disable_model_invocation", out var disable) && bool.TryParse(disable, out var disableVal) && disableVal,
                Agent = frontmatter.TryGetValue("agent", out var agent) ? agent : null,
                SourcePath = filePath,
                SourceFormat = SkillSourceFormat.Markdown,
                LastModified = await _files.GetLastWriteTimeUtcAsync(filePath, cancellationToken).ConfigureAwait(false)
            };

            _skills[skill.Name] = skill;
            return skill;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseFrontmatter(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!content.StartsWith("---")) return result;

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0) return result;

        var frontmatterBlock = content[3..endIndex].Trim();
        foreach (var line in frontmatterBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim().Trim('"', '\'');
            result[key] = value;
        }

        return result;
    }

    private static IReadOnlyList<string> ParseListField(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    #endregion

    /// <summary>
    /// 异步释放资源 — await Actor 完全退出，消除显式锁 — ADR 0115
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 技能服务 Actor — 串行化 ReloadAsync 操作，消除显式锁 — ADR 0115
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class SkillServiceActor : ActorBase<ReloadCmd, Unit>
    {
        private readonly SkillService _owner;
        private readonly ILogger<SkillService>? _logger;

        public SkillServiceActor(SkillService owner, ILogger<SkillService>? logger) : base()
        {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复 — 暴露 protected AskAwait 供 SkillService 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override async ValueTask HandleAsync(ReloadCmd cmd, CancellationToken ct)
        {
            try
            {
                cmd.Reply.SetResult(await _owner.ReloadInternalAsync(cmd.SkillName, cmd.Ctx, ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { cmd.Reply.SetException(ex); }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "SkillServiceActor 命令处理异常");
    }
}

/// <summary>
/// 技能重载 Actor 命令 — ReloadAsync 的 Actor 化封装 — ADR 0115
/// <para>消除 AsyncLock，改用 Actor 邮箱管道串行化重载操作（含文件 I/O）。</para>
/// </summary>
public sealed record ReloadCmd(
    string? SkillName,
    ExecutionContext Ctx,
    TaskCompletionSource<bool> Reply);
