using JoinCode.Abstractions.Attributes;

namespace Core.Skills.Discovery;

[Register]
public sealed partial class SkillDiscoveryService : ISkillDiscoveryService
{
    private readonly SkillDiscoveryOptions _options;
    private readonly IFileOperationService _files;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<SkillDiscoveryService>? _logger;
    private readonly ConcurrentDictionary<string, DiscoveredSkill> _discoveredSkills;
    private readonly SemaphoreSlim _discoveryLock;
    private IFileSystemWatcher? _watcher;
    private bool _isDisposed;

    public event EventHandler<SkillDiscoveredEventArgs>? SkillDiscovered;
    public event EventHandler<SkillChangedEventArgs>? SkillChanged;
    public event EventHandler<SkillRemovedEventArgs>? SkillRemoved;

    public SkillDiscoveryService(
        SkillDiscoveryOptions options,
        IFileOperationService files,
        IFileSystem fs,
        ILogger<SkillDiscoveryService>? logger = null)
    {
        _options = options;
        _files = files;
        _fs = fs;
        _logger = logger;
        _discoveredSkills = new ConcurrentDictionary<string, DiscoveredSkill>(StringComparer.OrdinalIgnoreCase);
        _discoveryLock = new SemaphoreSlim(1, 1);
    }

    public async Task<IReadOnlyList<DiscoveredSkill>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _discoveryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _discoveryLock.Release();
        }
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
                definition = JsonSerializer.Deserialize(result.Content, SkillsJsonContext.Default.SkillDefinition);
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
            : SkillValidationResult.Success(filePath, definition!, warnings);
    }

    public Task StartWatchingAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableFileWatching || _watcher != null)
        {
            return Task.CompletedTask;
        }

        if (!_files.DirectoryExists(_options.SkillsDirectory))
        {
            _files.CreateDirectory(_options.SkillsDirectory);
        }

        _watcher = _fs.Watch(_options.SkillsDirectory);
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        _watcher.Filter = "*.*";

        _watcher.DebouncedCreated += OnFileChanged;
        _watcher.DebouncedChanged += OnFileChanged;
        _watcher.DebouncedDeleted += OnFileDeleted;
        _watcher.DebouncedRenamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        _logger?.LogInformation("[SkillDiscovery] 开始监视技能目录: {Dir}", _options.SkillsDirectory);

        return Task.CompletedTask;
    }

    public void StopWatching()
    {
        // P1-11: 移除死代码 — 之前 _watcher?.Dispose() 和 _watcher = null 在 if 块后永不执行有效操作
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _logger?.LogInformation("[SkillDiscovery] 停止监视技能目录");
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            StopWatching();
            _discoveryLock.Dispose();
        }
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
        var definition = validationResult.SkillDefinition!;

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

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (extension != ".json" && !e.FullPath.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // P1-11: 添加异常观察，避免 fire-and-forget 触发 UnobservedTaskException
        _ = ProcessFileChangeAsync(e.FullPath).ContinueWith(
            t => _logger?.LogError(t.Exception, "[SkillDiscovery] OnFileChanged 未观察异常: {Path}", e.FullPath),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task ProcessFileChangeAsync(string filePath)
    {
        try
        {
            var skill = await LoadAndValidateFileAsync(filePath, CancellationToken.None).ConfigureAwait(false);
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

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
        var removedSkills = _discoveredSkills
            .Where(kvp => kvp.Value.SourcePath.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var (name, skill) in removedSkills)
        {
            _discoveredSkills.TryRemove(name, out _);
            SkillRemoved?.Invoke(this, new SkillRemovedEventArgs { SkillName = name, SourcePath = skill.SourcePath });
            _logger?.LogInformation("[SkillDiscovery] 技能已移除: {Name}", name);
        }
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        OnFileDeleted(sender, new FileChangedEventArgs
        {
            ChangeType = WatcherChangeTypes.Deleted,
            FullPath = e.OldFullPath,
            Name = Path.GetFileName(e.OldFullPath)
        });

        OnFileChanged(sender, new FileChangedEventArgs
        {
            ChangeType = WatcherChangeTypes.Created,
            FullPath = e.FullPath,
            Name = Path.GetFileName(e.FullPath)
        });
    }
}
