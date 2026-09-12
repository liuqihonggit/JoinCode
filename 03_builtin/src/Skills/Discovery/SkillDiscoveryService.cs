namespace Core.Skills.Discovery;

/// <summary>
/// 技能发现服务 Actor — 基于 FileWatcherActorBase,监控技能目录变更触发技能重新加载。
/// <para>对齐 ADR 0101: 消除 _watcher/_isDisposed 手动管理,Actor Consumer 串行化文件变更处理。</para>
/// <para>死循环防护: MarkInternalWrite 继承基类,通过 Actor 邮箱串行化,无窗口竞态。</para>
/// </summary>
[Register(typeof(ISkillDiscoveryService), ServiceLifetime.Singleton)]
public sealed partial class SkillDiscoveryService : FileWatcherActorBase, ISkillDiscoveryService
{
    private readonly SkillDiscoveryOptions _options;
    private readonly IFileOperationService _files;
    private readonly ILogger<SkillDiscoveryService>? _logger;
    private readonly ConcurrentDictionary<string, DiscoveredSkill> _discoveredSkills;
    private readonly AsyncLock _discoveryLock = new();

    public event EventHandler<SkillDiscoveredEventArgs>? SkillDiscovered;
    public event EventHandler<SkillChangedEventArgs>? SkillChanged;
    public event EventHandler<SkillRemovedEventArgs>? SkillRemoved;

    public SkillDiscoveryService(
        SkillDiscoveryOptions options,
        IFileOperationService files,
        IFileSystem fs,
        ILogger<SkillDiscoveryService>? logger = null)
        : base(fs, 1000)
    {
        _options = options;
        _files = files;
        _logger = logger;
        _discoveredSkills = new ConcurrentDictionary<string, DiscoveredSkill>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DiscoveredSkill>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using var guard = await _discoveryLock.TryLockAsync(cancellationToken).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_discoveryLock.Name}' 等待超时");

        var results = new List<DiscoveredSkill>();

        if (!_files.DirectoryExists(_options.SkillsDirectory))
        {
            _files.CreateDirectory(_options.SkillsDirectory);
            _logger?.LogInformation(L.T(StringKey.SkillDiscoveryCreateDir), _options.SkillsDirectory);
            return results;
        }

        var jsonFiles = _files.GetFiles(_options.SkillsDirectory, "*.json", SearchOption.AllDirectories);
        foreach (var filePath in jsonFiles)
        {
            var skill = await LoadAndValidateFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (skill != null)
            {
                _discoveredSkills[skill.Name] = skill;
                results.Add(skill);
            }
        }

        var mdFiles = _files.GetFiles(_options.SkillsDirectory, "SKILL.md", SearchOption.AllDirectories);
        foreach (var filePath in mdFiles)
        {
            var skill = await LoadAndValidateFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (skill != null)
            {
                _discoveredSkills[skill.Name] = skill;
                results.Add(skill);
            }
        }

        _logger?.LogInformation(L.T(StringKey.SkillDiscoveryFoundCount), results.Count);
        return results;
    }

    public async Task<DiscoveredSkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);

        var jsonPath = Path.Combine(_options.SkillsDirectory, $"{skillName}.json");
        if (_files.FileExists(jsonPath))
        {
            return await LoadAndValidateFileAsync(jsonPath, cancellationToken).ConfigureAwait(false);
        }

        var mdPath = Path.Combine(_options.SkillsDirectory, skillName, "SKILL.md");
        if (_files.FileExists(mdPath))
        {
            return await LoadAndValidateFileAsync(mdPath, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<SkillValidationResult> ValidateSkillAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!_files.FileExists(filePath))
        {
            errors.Add(L.T(StringKey.SkillDiscoveryFileNotExist, filePath));
            return SkillValidationResult.Failure(filePath, errors);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        SkillDefinition? definition = null;

        if (extension == ".json")
        {
            var result = await _files.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                errors.Add(L.T(StringKey.SkillDiscoveryCannotReadFile, result.ErrorMessage));
                return SkillValidationResult.Failure(filePath, errors);
            }

            try
            {
                definition = RelaxedJsonSerializer.Deserialize(result.Content, SkillsJsonContext.Default.SkillDefinition);
                if (definition == null)
                {
                    errors.Add(L.T(StringKey.SkillDiscoveryJsonNull));
                }
            }
            catch (JsonException ex)
            {
                errors.Add(L.T(StringKey.SkillDiscoveryJsonParseError, ex.Message));
            }
        }
        else if (extension == ".md")
        {
            var result = await _files.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                errors.Add(L.T(StringKey.SkillDiscoveryCannotReadFile, result.ErrorMessage));
                return SkillValidationResult.Failure(filePath, errors);
            }

            definition = ParseMarkdownSkill(result.Content, filePath);
        }
        else
        {
            errors.Add(L.T(StringKey.SkillDiscoveryUnsupportedExtension, extension));
        }

        if (definition != null)
        {
            ValidateDefinition(definition, errors, warnings);
        }

        return errors.Count > 0
            ? SkillValidationResult.Failure(filePath, errors, warnings)
            : definition is not null
                ? SkillValidationResult.Success(filePath, definition, warnings)
                : SkillValidationResult.Failure(filePath, [L.T(StringKey.SkillDiscoveryUnsupportedExtension, extension)], warnings);
    }

    /// <summary>启动技能目录监控 — 投递 FileWatcherStartCmd 到 Actor 邮箱</summary>
    public Task StartWatchingAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableFileWatching)
        {
            return Task.CompletedTask;
        }

        if (!_files.DirectoryExists(_options.SkillsDirectory))
        {
            _files.CreateDirectory(_options.SkillsDirectory);
        }

        TrySend(new FileWatcherStartCmd(
            _options.SkillsDirectory, "*.*", TimeSpan.FromMilliseconds(500),
            IncludeSubdirectories: true,
            NotifyFilter: NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
        ));

        _logger?.LogInformation("[SkillDiscovery] 开始监视技能目录: {Dir}", _options.SkillsDirectory);

        return Task.CompletedTask;
    }

    /// <summary>停止技能目录监控 — 投递 FileWatcherStopCmd 到 Actor 邮箱</summary>
    public void StopWatching()
        => TrySend(new FileWatcherStopCmd());

    /// <summary>文件变更处理 — 由 Actor Consumer 串行调用,过滤技能文件后处理变更/删除</summary>
    protected override async ValueTask HandleFileChangedAsync(string filePath, WatcherChangeTypes kind, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (!IsSkillFile(filePath)) return;

        if (kind == WatcherChangeTypes.Deleted)
        {
            HandleFileDeleted(filePath);
            return;
        }

        await ProcessFileChangeAsync(filePath, ct).ConfigureAwait(false);
    }

    /// <summary>文件重命名处理 — 旧路径按删除处理,新路径按变更处理</summary>
    protected override async ValueTask HandleFileRenamedAsync(string oldPath, string newPath, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (IsSkillFile(oldPath))
            HandleFileDeleted(oldPath);
        if (IsSkillFile(newPath))
            await ProcessFileChangeAsync(newPath, ct).ConfigureAwait(false);
    }

    /// <summary>同步释放 — IDisposable 接口实现,委托给 DisposeAsync</summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        _discoveryLock.Dispose();
    }

    /// <summary>异步释放 — 先停 watcher(基类),再释放 discoveryLock</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _discoveryLock.Dispose();
    }

    private static bool IsSkillFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".json" || filePath.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DiscoveredSkill?> LoadAndValidateFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var validationResult = await ValidateSkillAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            _logger?.LogWarning("[SkillDiscovery] 技能文件验证失败: {Path}, 错误: {Errors}",
                filePath, string.Join(", ", validationResult.Errors));

            if (validationResult.SkillDefinition != null)
            {
                return new DiscoveredSkill
                {
                    Name = validationResult.SkillDefinition.Name,
                    SourcePath = filePath,
                    SourceFormat = filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? SkillSourceFormat.Markdown
                        : SkillSourceFormat.Json,
                    LastModified = _files.GetFileLastWriteTime(filePath),
                    Definition = validationResult.SkillDefinition,
                    ValidationErrors = validationResult.Errors,
                    ValidationWarnings = validationResult.Warnings
                };
            }

            return null;
        }

        var lastModified = _files.GetFileLastWriteTime(filePath);
        var definition = validationResult.SkillDefinition ?? throw new InvalidOperationException("Skill definition is null for valid result.");

        return new DiscoveredSkill
        {
            Name = definition.Name,
            SourcePath = filePath,
            SourceFormat = filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? SkillSourceFormat.Markdown
                : SkillSourceFormat.Json,
            LastModified = lastModified,
            Definition = definition with { SourcePath = filePath, LastModified = lastModified },
            ValidationErrors = validationResult.Errors,
            ValidationWarnings = validationResult.Warnings
        };
    }

    private SkillDefinition ParseMarkdownSkill(string content, string filePath)
    {
        var frontmatter = ParseFrontmatter(content);
        var skillName = frontmatter.TryGetValue("name", out var name) ? name
            : Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "unknown";
        var description = frontmatter.TryGetValue("description", out var desc) ? desc : string.Empty;

        return new SkillDefinition
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
            SourceFormat = SkillSourceFormat.Markdown
        };
    }

    private static Dictionary<string, string> ParseFrontmatter(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!content.StartsWith("---"))
        {
            return result;
        }

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return result;
        }

        var frontmatterBlock = content[3..endIndex].Trim();
        foreach (var line in frontmatterBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

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

    private static void ValidateDefinition(SkillDefinition definition, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add(L.T(StringKey.SkillDiscoveryNameEmpty));
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            warnings.Add(L.T(StringKey.SkillDiscoveryDescriptionEmpty));
        }

        if (definition.Steps.Count == 0)
        {
            errors.Add(L.T(StringKey.SkillDiscoveryNoSteps));
        }

        var stepIds = new HashSet<string>();
        foreach (var step in definition.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add(L.T(StringKey.SkillDiscoveryStepMissingId));
                continue;
            }

            if (!stepIds.Add(step.Id))
            {
                errors.Add(L.T(StringKey.SkillDiscoveryStepIdDuplicate, step.Id));
            }

            // JSON converter already validates enum values, but double-check for programmatic creation
            if (!SkillStepTypeExtensions.IsDefined(step.Type))
            {
                errors.Add(L.T(StringKey.SkillDiscoveryStepMissingType, step.Id));
            }
        }

        foreach (var (paramName, param) in definition.Parameters)
        {
            if (string.IsNullOrWhiteSpace(param.Type))
            {
                errors.Add(L.T(StringKey.SkillDiscoveryParamMissingType, paramName));
            }
        }
    }

    private async Task ProcessFileChangeAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var skill = await LoadAndValidateFileAsync(filePath, ct).ConfigureAwait(false);
            if (skill != null)
            {
                var wasExisting = _discoveredSkills.ContainsKey(skill.Name);
                _discoveredSkills[skill.Name] = skill;

                if (wasExisting)
                {
                    SkillChanged?.Invoke(this, new SkillChangedEventArgs { Skill = skill });
                    _logger?.LogInformation("[SkillDiscovery] 技能已变更: {Name}", skill.Name);
                }
                else
                {
                    SkillDiscovered?.Invoke(this, new SkillDiscoveredEventArgs { Skill = skill });
                    _logger?.LogInformation("[SkillDiscovery] 发现新技能: {Name}", skill.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SkillDiscovery] 处理文件变更失败: {Path}", filePath);
        }
    }

    private void HandleFileDeleted(string filePath)
    {
        var removedSkills = _discoveredSkills
            .Where(kvp => kvp.Value.SourcePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var (name, skill) in removedSkills)
        {
            _discoveredSkills.TryRemove(name, out _);
            SkillRemoved?.Invoke(this, new SkillRemovedEventArgs { SkillName = name, SourcePath = skill.SourcePath });
            _logger?.LogInformation("[SkillDiscovery] 技能已移除: {Name}", name);
        }
    }
}
