namespace Core.Agents;

/// <summary>
/// 代理定义提供者 — 加载内置、用户、项目及插件代理定义，支持缓存与变更刷新
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider), ServiceLifetime.Singleton)]
public sealed partial class AgentDefinitionProvider : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider {

    /// <summary>
    /// 构造 AgentDefinitionProvider 实例，注入文件系统、日志器及可选的插件代理加载器
    /// </summary>
    public AgentDefinitionProvider(IFileSystem fs, ILogger<AgentDefinitionProvider>? logger = null, IPluginAgentLoader? pluginAgentLoader = null) {
        _fs = fs;
        _logger = logger;
        _pluginAgentLoader = pluginAgentLoader;
        _actor = new DefinitionLoaderActor(this, logger);
        if (pluginAgentLoader is not null) {
            pluginAgentLoader.Changed += OnPluginAgentLoaderChanged;
        }
    }
    private readonly IFileSystem _fs;
    private readonly ILogger<AgentDefinitionProvider>? _logger;
    private readonly IPluginAgentLoader? _pluginAgentLoader;
    private readonly DefinitionLoaderActor _actor;
    private volatile List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> _cachedDefinitions = [];
    private volatile ILookup<(JoinCode.Abstractions.Models.Agent.AgentRole Role, JoinCode.Abstractions.Models.Agent.ExecutorVariant? Variant), JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> _cachedDefinitionMap
        = Array.Empty<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>().ToLookup(d => (d.Role, d.Variant));
    private volatile bool _cacheLoaded;

    private static readonly string[] ProjectAgentDirs =
    new[] {
        Path.Combine(AppDataConstants.AppDataFolder, "agents"),
        Path.Combine(".trae", "agents"),
        Path.Combine(".claude", "agents")
     };

    /// <summary>
    /// 获取所有代理定义列表，合并内置、用户、项目及插件定义并去重，结果缓存
    /// </summary>
    /// <param name="workingDirectory">工作目录（用于加载项目级定义，可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>去重后的代理定义列表</returns>
    public async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> GetAgentDefinitionsAsync(
        string? workingDirectory = null,
        CancellationToken cancellationToken = default) {
        if (_cacheLoaded)
            return _cachedDefinitions;

        var key = new IdempotencyKey("get-definitions", Guid.NewGuid().ToString());
        var tcs = new TaskCompletionSource<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>();
        GetDefinitionsCmd? cmd = null;
        cmd = new GetDefinitionsCmd(workingDirectory, key, tcs.SetResult, tcs.SetException,
            ActorBase<GetDefinitionsCmd, Unit>.CreateBackpressureHandler(() => { if (cmd is not null) _actor.TrySend(cmd); }));
        _actor.Tell(cmd);
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 加载代理定义内部实现 — 由 Actor Consumer 串行调用，天然无竞态，无需锁
    /// </summary>
    /// <param name="workingDirectory">工作目录（用于加载项目级定义，可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>去重后的代理定义列表</returns>
    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> GetDefinitionsInternalAsync(
        string? workingDirectory,
        CancellationToken cancellationToken) {
        if (_cacheLoaded)
            return _cachedDefinitions;

        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        definitions.AddRange(GetBuiltInDefinitions());

        if (_pluginAgentLoader is not null) {
            definitions.AddRange(_pluginAgentLoader.GetAll());
        }

        // 并行加载用户定义和项目定义
        var loadTasks = new List<Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>>();
        loadTasks.Add(LoadUserDefinitionsAsync(cancellationToken));
        if (workingDirectory is not null) {
            loadTasks.Add(LoadProjectDefinitionsAsync(workingDirectory, cancellationToken));
        }
        var loadResults = await Task.WhenAll(loadTasks).ConfigureAwait(false);
        foreach (var loaded in loadResults) {
            definitions.AddRange(loaded);
        }

        _cachedDefinitions = Deduplicate(definitions);
        _cachedDefinitionMap = _cachedDefinitions.ToLookup(d => (d.Role, d.Variant));
        _cacheLoaded = true;
        return _cachedDefinitions;
    }

    /// <summary>
    /// 按角色与变体获取单个代理定义
    /// </summary>
    /// <param name="role">代理角色</param>
    /// <param name="variant">执行变体（可选）</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的代理定义；未找到时返回 null</returns>
    public async Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?> GetAgentDefinitionAsync(
        JoinCode.Abstractions.Models.Agent.AgentRole role,
        JoinCode.Abstractions.Models.Agent.ExecutorVariant? variant = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default) {
        await GetAgentDefinitionsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        return _cachedDefinitionMap[(role, variant)].FirstOrDefault();
    }

    /// <inheritdoc />
    public void ClearCache() {
        _cachedDefinitions = [];
        _cachedDefinitionMap = Array.Empty<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>().ToLookup(d => (d.Role, d.Variant));
        _cacheLoaded = false;
        _logger?.LogDebug("代理定义缓存已清除");
    }

    /// <summary>
    /// 获取内置代理定义列表 — 对齐 TS builtInAgents.ts，包含 Coordinator/Executor 各变体
    /// </summary>
    /// <returns>内置代理定义列表</returns>
    internal static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> GetBuiltInDefinitions() {
        // 对齐 TS builtInAgents.ts — Explore/Plan 禁止 Agent/FileEdit/FileWrite/NotebookEdit
        var readOnlyDisallowedTools = new List<string>
        {
            AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, NotebookToolNameEnumConstants.NotebookEdit
        };

        // 子代理禁止嵌套创建子代理 — 只能通过 SendMessage 向主代理请求创建平行子代理
        var subAgentDisallowedTools = new List<string>
        {
            AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSpawn
        };

        return
        [
            new()
            {
                Role = AgentRole.Coordinator,
                WhenToUse = "General tasks with full toolset",
                Description = "Coordinator agent — manages Goal lifecycle, full toolset",
                Tools = [],
                DisallowedTools = subAgentDisallowedTools
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "Code reading, writing, editing and refactoring",
                Description = "Code agent focused on code reading, writing and editing",
                Tools = [FileToolNameEnumConstants.FileRead, FileToolNameEnumConstants.FileWrite, FileToolNameEnumConstants.FileEdit, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, ShellToolNameEnumConstants.Bash, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = subAgentDisallowedTools
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Search,
                WhenToUse = "Code search, navigation and exploration",
                Description = "Search agent focused on code search and navigation",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = [FileToolNameEnumConstants.FileWrite, FileToolNameEnumConstants.FileEdit, ShellToolNameEnumConstants.Bash]
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Explore,
                WhenToUse = "Quick codebase exploration agent for file pattern search, keyword search, and codebase Q&A. Supports thoroughness levels: quick/medium/very thorough",
                Description = "Explore agent — strictly read-only, for searching and understanding code",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                IsBackground = false,
                OmitProjectRules = true,
                OmitGitStatus = true
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Plan,
                WhenToUse = "Software architect agent that designs implementation plans, returns step-by-step plans, key files, and architectural trade-offs",
                Description = "Plan agent — strictly read-only, for designing implementation plans",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                IsBackground = false,
                OmitProjectRules = true,
                OmitGitStatus = true
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
                Description = "Doctor agent — 自举修复，后台运行，Cron 调度每12h复盘",
                Tools = [FileToolNameEnumConstants.FileRead, FileToolNameEnumConstants.FileEdit, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, ShellToolNameEnumConstants.Bash],
                DisallowedTools = [AgentToolNameEnumConstants.Agent],
                IsBackground = true,
                PermissionMode = "doctor"
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Verification,
                WhenToUse = "Verify code correctness, quality and security",
                Description = "Verification agent — checks code for errors, vulnerabilities and best practice violations",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite]
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.JoinCodeGuide,
                WhenToUse = $"Guide users on how to use {BrandConstants.ProductName} features and best practices",
                Description = $"{BrandConstants.ProductName} Guide agent — helps users understand and use {BrandConstants.ProductName}",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, ShellToolNameEnumConstants.Bash]
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.ContextCompression,
                WhenToUse = "Intelligently compress and manage conversation context to optimize Token usage",
                Description = "Context Compression agent — compresses context while preserving key information",
                Tools = [FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep],
                DisallowedTools = [AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite, ShellToolNameEnumConstants.Bash]
            }
        ];
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadUserDefinitionsAsync(
        CancellationToken cancellationToken) {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agentsPath = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder, AppDataConstants.AgentsFolderName);

        if (_fs.DirectoryExists(agentsPath)) {
            var loaded = await LoadDefinitionsFromDirectoryAsync(agentsPath, cancellationToken).ConfigureAwait(false);
            definitions.AddRange(loaded);
        }

        return definitions;
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadProjectDefinitionsAsync(
        string workingDirectory,
        CancellationToken cancellationToken) {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var currentDirPath = _fs.GetFullPath(workingDirectory);

        while (currentDirPath != null) {
            // 并行扫描所有项目代理目录
            var dirTasks = new List<Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>>();
            foreach (var agentDir in ProjectAgentDirs) {
                var fullPath = Path.Combine(currentDirPath, agentDir);
                if (_fs.DirectoryExists(fullPath)) {
                    dirTasks.Add(LoadDefinitionsFromDirectoryAsync(fullPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var loaded in dirResults) {
                definitions.AddRange(loaded);
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        return definitions;
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadDefinitionsFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken) {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();

        try {
            var mdFiles = _fs.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories);

            // 并行读取所有 md 文件
            var readTasks = new List<Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?>>();
            foreach (var filePath in mdFiles) {
                readTasks.Add(TryReadDefinitionFileAsync(filePath, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var definition in readResults) {
                if (definition is not null) {
                    definitions.Add(definition);
                    _logger?.LogInformation("已加载代理定义: {DisplayId} ({Path})", definition.DisplayId, definition.SourcePath);
                }
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, L.T(StringKey.ScanAgentDefinitionFailedLog, directoryPath));
        }

        return definitions;
    }

    private async Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?> TryReadDefinitionFileAsync(string filePath, CancellationToken cancellationToken) {
        try {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            return ParseDefinitionFile(content, filePath);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "读取代理定义文件失败: {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// 解析代理定义文件内容 — 从 frontmatter 与正文提取角色、变体、工具、模型等字段
    /// </summary>
    /// <param name="content">文件内容</param>
    /// <param name="sourcePath">源文件路径</param>
    /// <returns>解析后的代理定义；解析失败时返回 null</returns>
    internal static JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? ParseDefinitionFile(string content, string sourcePath) {
        var result = FrontmatterParser.Parse(content);

        var agentType = GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(agentType)) return null;

        var nameOverride = GetStringFromData(result.Data, "name", "agent_type", "type");
        var effectiveName = !string.IsNullOrWhiteSpace(nameOverride) ? nameOverride : agentType;

        var (role, variant) = ParseRoleAndVariant(effectiveName);

        var whenToUse = GetStringFromData(result.Data, "when_to_use", "whenToUse", "description");
        if (string.IsNullOrWhiteSpace(whenToUse))
            whenToUse = $"自定义代理: {effectiveName}";

        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition {
            Role = role,
            Variant = variant,
            WhenToUse = whenToUse,
            Description = GetStringFromData(result.Data, "description", "desc"),
            Tools = GetStringListFromData(result.Data, "tools", "allowed_tools") ?? [],
            DisallowedTools = GetStringListFromData(result.Data, "disallowed_tools", "denied_tools") ?? [],
            ModelName = NormalizeModelName(GetStringFromData(result.Data, "model", "model_name")),
            Temperature = GetFloatFromData(result.Data, "temperature"),
            MaxTokens = GetIntFromData(result.Data, "max_tokens"),
            IsBackground = GetBoolFromData(result.Data, "is_background", "background"),
            SystemPrompt = result.HasFrontmatter ? result.Content.Trim() : content.Trim(),
            SourcePath = sourcePath,
            Skills = GetStringListFromData(result.Data, "skills", "preload_skills") ?? [],
            PermissionMode = GetStringFromData(result.Data, "permission_mode", "permissionMode"),
            Hooks = ParseHooksFromData(result.Data) ?? [],
            McpServers = ParseMcpServersFromData(result.Data) ?? [],
            RequiredMcpServers = GetStringListFromData(result.Data, "required_mcp_servers", "requiredMcpServers") ?? [],
            Memory = ParseMemoryScopeFromData(result.Data),
            CriticalSystemReminder = GetStringFromData(result.Data, "criticalSystemReminder_EXPERIMENTAL", "critical_system_reminder"),
            InitialPrompt = GetStringFromData(result.Data, "initialPrompt", "initial_prompt"),
        };

        return definition;
    }

    private static string GetFileNameWithoutExtension(string path) {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(fileName)) return string.Empty;

        var dirPart = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dirPart)) return fileName;

        var baseDirLength = dirPart.Length + 1;
        if (path.Length > baseDirLength) {
            var relativePath = path[baseDirLength..];
            var relativeNoExt = Path.ChangeExtension(relativePath, null);
            return relativeNoExt.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        return fileName;
    }

    /// <summary>
    /// 归一化模型名称 — 对齐 TS 原版 AgentJsonSchema model.transform
    /// <para>"inherit" 不区分大小写归一化为小写 "inherit",其他值原样返回</para>
    /// <para>避免配置写 "Inherit"/"INHERIT" 时解析失败</para>
    /// </summary>
    private static string? NormalizeModelName(string? model) {
        if (string.IsNullOrWhiteSpace(model))
            return model;
        return SubAgentModelResolver.IsInheritKeyword(model)
            ? SubAgentModelResolver.DefaultSubagentModel
            : model;
    }

    private static string? GetStringFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys) {
        foreach (var key in keys) {
            if (data.TryGetValue(key, out var element) && element.ValueKind == System.Text.Json.JsonValueKind.String)
                return element.GetString();
        }
        return null;
    }

    private static List<string>? GetStringListFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys) {
        foreach (var key in keys) {
            if (!data.TryGetValue(key, out var element))
                continue;

            return element.ValueKind switch {
                System.Text.Json.JsonValueKind.String => [element.GetString() ?? string.Empty],
                System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
                    .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .ToList(),
                _ => null
            };
        }
        return null;
    }

    private static float? GetFloatFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys) {
        foreach (var key in keys) {
            if (data.TryGetValue(key, out var element)) {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetSingle(out var value))
                    return value;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && float.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static int? GetIntFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys) {
        foreach (var key in keys) {
            if (data.TryGetValue(key, out var element)) {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetInt32(out var value))
                    return value;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static bool GetBoolFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys) {
        foreach (var key in keys) {
            if (data.TryGetValue(key, out var element)) {
                if (element.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                if (element.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                    return element.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            }
        }
        return false;
    }

    /// <summary>
    /// 解析 memory 作用域 — 对齐 TS builtInAgents.ts 的 memory 字段
    /// </summary>
    private static AgentMemoryScope? ParseMemoryScopeFromData(Dictionary<string, System.Text.Json.JsonElement> data) {
        var memoryStr = GetStringFromData(data, "memory");
        return AgentMemoryScopeExtensions.FromValue(memoryStr);
    }

    private static Dictionary<string, List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>>? ParseHooksFromData(
        Dictionary<string, System.Text.Json.JsonElement> data) {
        if (!data.TryGetValue("hooks", out var hooksElement) || hooksElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        var result = new Dictionary<string, List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventProp in hooksElement.EnumerateObject()) {
            if (eventProp.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;

            var matchers = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>();
            foreach (var matcherElement in eventProp.Value.EnumerateArray()) {
                var matcher = ParseHookMatcher(matcherElement);
                if (matcher is not null)
                    matchers.Add(matcher);
            }

            if (matchers.Count > 0)
                result[eventProp.Name] = matchers;
        }

        return result.Count > 0 ? result : null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher? ParseHookMatcher(System.Text.Json.JsonElement element) {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        string? matcher = null;
        if (element.TryGetProperty("matcher", out var matcherProp) && matcherProp.ValueKind == System.Text.Json.JsonValueKind.String)
            matcher = matcherProp.GetString();

        if (!element.TryGetProperty("hooks", out var hooksProp) || hooksProp.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        var hooks = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand>();
        foreach (var hookElement in hooksProp.EnumerateArray()) {
            var hook = ParseHookCommand(hookElement);
            if (hook is not null)
                hooks.Add(hook);
        }

        if (hooks.Count == 0)
            return null;

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher { Matcher = matcher, Hooks = hooks };
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand? ParseHookCommand(System.Text.Json.JsonElement element) {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        var dto = System.Text.Json.JsonSerializer.Deserialize(
            element, AgentHookConfigJsonContext.Default.AgentHookCommandDto);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Type))
            return null;

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand {
            Type = dto.Type,
            Command = dto.Command,
            Prompt = dto.Prompt,
            If = dto.If,
            Timeout = dto.Timeout
        };
    }

    private static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec>? ParseMcpServersFromData(
        Dictionary<string, System.Text.Json.JsonElement> data) {
        if (!data.TryGetValue("mcpServers", out var element) && !data.TryGetValue("mcp_servers", out element))
            return null;

        if (element.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        var specs = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec>();
        foreach (var item in element.EnumerateArray()) {
            var spec = ParseSingleMcpServerSpec(item);
            if (spec is not null)
                specs.Add(spec);
        }

        return specs.Count > 0 ? specs : null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec? ParseSingleMcpServerSpec(System.Text.Json.JsonElement item) {
        if (item.ValueKind == System.Text.Json.JsonValueKind.String) {
            var name = item.GetString();
            return string.IsNullOrEmpty(name) ? null : JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec.FromReference(name);
        }

        if (item.ValueKind == System.Text.Json.JsonValueKind.Object) {
            foreach (var prop in item.EnumerateObject()) {
                if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                    continue;

                var config = ParseInlineMcpConfig(prop.Value);
                return JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec.FromInline(prop.Name, config);
            }
        }

        return null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerInlineConfig ParseInlineMcpConfig(System.Text.Json.JsonElement obj) {
        var dto = System.Text.Json.JsonSerializer.Deserialize(
            obj, AgentHookConfigJsonContext.Default.AgentMcpServerInlineConfigDto) ?? new AgentMcpServerInlineConfigDto();

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerInlineConfig {
            Command = dto.Command,
            Args = dto.Args ?? [],
            Env = dto.Env ?? [],
            Url = dto.Url,
            TransportType = dto.TransportType,
            Headers = dto.Headers ?? []
        };
    }

    private static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> Deduplicate(
        List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> definitions) {
        var result = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var indexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in definitions) {
            if (indexMap.TryAdd(def.DisplayId, result.Count)) {
                result.Add(def);
            } else if (def.SourcePath is not null) {
                var existingIdx = indexMap[def.DisplayId];
                result[existingIdx] = def;
            }
        }

        return result;
    }

    /// <summary>
    /// 从字符串解析角色和变体 — 支持 "coordinator"、"executor:code"、"code"（简写）等格式
    /// </summary>
    private static (AgentRole Role, ExecutorVariant? Variant) ParseRoleAndVariant(string name) {
        if (name.Contains(':')) {
            var parts = name.Split(':', 2);
            var roleStr = parts[0].Trim();
            var variantStr = parts[1].Trim();

            var role = AgentRoleExtensions.FromValue(roleStr);
            var variant = ExecutorVariantExtensions.FromValue(variantStr);
            return (role ?? AgentRole.Executor, variant);
        }

        var existingVariant = ExecutorVariantExtensions.FromValue(name);
        if (existingVariant.HasValue)
            return (AgentRole.Executor, existingVariant.Value);

        var existingRole = AgentRoleExtensions.FromValue(name);
        if (existingRole.HasValue)
            return (existingRole.Value, null);

        return (AgentRole.Executor, null);
    }

    private void OnPluginAgentLoaderChanged(object? sender, EventArgs e) => ClearCache();

    /// <summary>异步释放资源 — await Actor 完全退出</summary>
    public override async ValueTask DisposeAsync() {
        if (_pluginAgentLoader is not null) _pluginAgentLoader.Changed -= OnPluginAgentLoaderChanged;
        await _actor.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 代理定义加载 Actor — 串行化 GetAgentDefinitionsAsync 加载操作，消除 AsyncLock + double-check 锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// <para>读快速路径（_cacheLoaded volatile 检查）不经 Actor，命中缓存直接返回。</para>
    /// </summary>
    private sealed class DefinitionLoaderActor : ActorBase<GetDefinitionsCmd, Unit> {
        private readonly AgentDefinitionProvider _owner;
        private readonly ILogger<AgentDefinitionProvider>? _logger;

        /// <summary>构造代理定义加载 Actor。</summary>
        /// <param name="owner">所属的代理定义提供者。</param>
        /// <param name="logger">可选的日志记录器。</param>
        public DefinitionLoaderActor(AgentDefinitionProvider owner, ILogger<AgentDefinitionProvider>? logger) : base(idempotencyStore: new IdempotencyStore()) {
            _owner = owner;
            _logger = logger;
        }

        protected override void Handle(GetDefinitionsCmd cmd, CancellationToken ct) { RegisterInFlight(HandleAsyncImpl(cmd, ct).AsTask()); }

        private async ValueTask HandleAsyncImpl(GetDefinitionsCmd cmd, CancellationToken ct) {
            try {
                var result = await _owner.GetDefinitionsInternalAsync(cmd.WorkingDirectory, ct).ConfigureAwait(false);
                IdempotencyStore?.TryRegister(cmd.IdempotencyKey, result);
                cmd.OnSuccess(result);
            } catch (OperationCanceledException) { throw; } catch (Exception ex) { cmd.OnFailure(ex); }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger?.LogWarning(ex, "DefinitionLoaderActor 命令处理异常");
    }
}

/// <summary>
/// Agent Hook 命令 JSON DTO — 对应 frontmatter hooks[].hooks[] 元素，用于 JsonSerializer.Deserialize 双向转换
/// </summary>
public sealed class AgentHookCommandDto {
    /// <summary>钩子类型（command/webhook 等）— discriminated union 判别字段</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>要执行的命令</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>提示词</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>执行条件表达式</summary>
    [JsonPropertyName("if")]
    public string? If { get; set; }

    /// <summary>超时时间（毫秒）</summary>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}

/// <summary>
/// Agent MCP 服务器内联配置 JSON DTO — 对应 mcpServers 条目中的 command/args/env/url/type/headers，用于 JsonSerializer.Deserialize 双向转换
/// </summary>
public sealed class AgentMcpServerInlineConfigDto {
    /// <summary>启动命令</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>命令参数列表</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>环境变量字典</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>服务器 URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>传输类型</summary>
    [JsonPropertyName("type")]
    public string? TransportType { get; set; }

    /// <summary>请求头字典</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = [];
}

/// <summary>
/// Agent Hook 配置 JSON 序列化上下文 — AOT 源码生成，覆盖 hook 命令与 MCP 内联配置 DTO
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AgentHookCommandDto))]
[JsonSerializable(typeof(AgentMcpServerInlineConfigDto))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class AgentHookConfigJsonContext : JsonSerializerContext;