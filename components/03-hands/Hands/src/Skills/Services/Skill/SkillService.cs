using JoinCode.Abstractions.Attributes;

namespace Core.Skills;

[Register]
public sealed record SkillOptions
{
    public string SkillsDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataConstants.AppDataFolder, "skills");
    public TimeSpan CacheExpiration { get; init; } = TimeSpan.FromMinutes(5);

    public SkillOptions() { }

    public SkillOptions(WorkflowConfig? config)
    {
        SkillsDirectory = config is not null && !string.IsNullOrEmpty(config.SkillsDirectory)
            ? config.SkillsDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataConstants.AppDataFolder, "skills");
    }

    public static SkillOptions FromConfig(WorkflowConfig? config) => new(config);
}

[Register]
public sealed class SkillService : ISkillService, IDisposable
{
    private readonly SkillOptions _options;
    private readonly IFileOperationService _files;
    private readonly Core.Skills.Mcp.IMcpSkillProvider? _mcpSkillProvider;
    private readonly MiddlewarePipeline<SkillContext> _pipeline;
    private readonly SemaphoreSlim _reloadLock;
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills;
    private readonly Core.Skills.Discovery.ISkillDiscoveryService? _discoveryService;
    private DateTime _lastReloadTime = DateTime.MinValue;

    public SkillService(
        SkillOptions options,
        IFileOperationService files,
        MiddlewarePipeline<SkillContext> pipeline,
        Core.Skills.Discovery.ISkillDiscoveryService? discoveryService = null,
        Core.Skills.Mcp.IMcpSkillProvider? mcpSkillProvider = null
        )
    {
        Diag.WriteLine("[SKILL-CTOR] 1 assign fields");
        _options = options;
        _files = files;
        _pipeline = pipeline;
        _discoveryService = discoveryService;
        _mcpSkillProvider = mcpSkillProvider;
        _reloadLock = new SemaphoreSlim(1, 1);
        _skills = new ConcurrentDictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);

        Diag.WriteLine("[SKILL-CTOR] 2 LoadBuiltInSkills");
        LoadBuiltInSkills();
        Diag.WriteLine("[SKILL-CTOR] 3 done");
    }

    public async Task<SkillResult> ExecuteAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        SkillExecutionContext ctx,
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
            Parameters = parameters,
            Skill = skill,
            ExecutionContext = ctx,
            CancellationToken = cancellationToken
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        return context.Result ?? SkillResult.FailureResult(skillName, "Pipeline completed without result");
    }

    public async Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken cancellationToken = default)
    {
        var localSkills = _skills.Values.ToList();

        // 合并 MCP 远程技能 — 对齐 TS getAllCommands 合并本地+MCP技能
        if (_mcpSkillProvider is not null)
        {
            var mcpSkills = await _mcpSkillProvider.GetMcpSkillsAsync(cancellationToken).ConfigureAwait(false);
            if (mcpSkills.Count > 0)
            {
                var combined = new List<SkillDefinition>(localSkills.Count + mcpSkills.Count);
                combined.AddRange(localSkills);
                combined.AddRange(mcpSkills);
                return combined;
            }
        }

        return localSkills;
    }

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

    public bool SkillExists(string skillName)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        if (_skills.ContainsKey(skillName)) return true;

        // 检查 MCP 远程技能
        return _mcpSkillProvider is not null && _mcpSkillProvider.IsSkillAvailable(skillName);
    }

    public async Task<bool> ReloadAsync(string? skillName, SkillExecutionContext ctx, CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(ctx.CancellationToken).ConfigureAwait(false);
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
        finally
        {
            _reloadLock.Release();
        }
    }

    public void RegisterSkill(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _skills[skill.Name] = skill;
    }

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
        SkillExecutionContext ctx,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ctx.Logger?.LogInformation("执行 MCP 远程技能: {SkillName}", skillName);

        try
        {
            var result = await _mcpSkillProvider!.ExecuteMcpSkillAsync(skillName, parameters, ctx, cancellationToken).ConfigureAwait(false);
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
            System.Diagnostics.Trace.WriteLine($"SkillService.LoadExternalSkillsAsync failed: {ex.Message}");
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
                System.Diagnostics.Trace.WriteLine($"SkillService.DiscoverAsync failed: {ex.Message}");
            }
        }
    }

    private async Task<SkillDefinition?> LoadSkillFromFileAsync(string skillName, SkillExecutionContext ctx)
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

            var skill = JsonSerializer.Deserialize(result.Content, SkillsJsonContext.Default.SkillDefinition);
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

    public void Dispose() => _reloadLock.Dispose();
}
