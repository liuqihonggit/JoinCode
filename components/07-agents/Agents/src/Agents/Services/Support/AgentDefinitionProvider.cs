namespace Core.Agents;

[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider))]
public sealed partial class AgentDefinitionProvider : JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider
{
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<AgentDefinitionProvider>? _logger;
    private volatile List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>? _cachedDefinitions;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private static readonly string[] ProjectAgentDirs =
    [
        Path.Combine(AppDataConstants.AppDataFolder, "agents"),
        Path.Combine(".trae", "agents"),
        Path.Combine(".claude", "agents")
    ];

    public async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> GetAgentDefinitionsAsync(
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (_cachedDefinitions is not null)
            return _cachedDefinitions;

        await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedDefinitions is not null)
                return _cachedDefinitions;

            var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
            definitions.AddRange(GetBuiltInDefinitions());

            // 并行加载用户定义和项目定义
            var loadTasks = new List<Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>>();
            loadTasks.Add(LoadUserDefinitionsAsync(cancellationToken));
            if (workingDirectory is not null)
            {
                loadTasks.Add(LoadProjectDefinitionsAsync(workingDirectory, cancellationToken));
            }
            var loadResults = await Task.WhenAll(loadTasks).ConfigureAwait(false);
            foreach (var loaded in loadResults)
            {
                definitions.AddRange(loaded);
            }

            _cachedDefinitions = Deduplicate(definitions);
            return _cachedDefinitions;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?> GetAgentDefinitionAsync(
        string agentType,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentType);

        var definitions = await GetAgentDefinitionsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        return definitions.FirstOrDefault(d => string.Equals(d.AgentType, agentType, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cachedDefinitions = null;
        _logger?.LogDebug("代理定义缓存已清除");
    }

    internal static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> GetBuiltInDefinitions()
    {
        // 对齐 TS builtInAgents.ts — Explore/Plan 禁止 Agent/FileEdit/FileWrite/NotebookEdit
        var readOnlyDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, NotebookToolNameConstants.NotebookEdit
        };

        // 子代理禁止嵌套创建子代理 — 只能通过 SendMessage 向主代理请求创建平行子代理
        var subAgentDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        };

        return
        [
            new()
            {
                AgentType = AgentTypeDefinition.Default.ToValue(),
                WhenToUse = "General tasks with full toolset",
                Description = "Default agent type with full toolset",
                Tools = null,
                DisallowedTools = subAgentDisallowedTools
            },
            new()
            {
                AgentType = AgentTypeDefinition.Code.ToValue(),
                WhenToUse = "Code reading, writing, editing and refactoring",
                Description = "Code agent focused on code reading, writing and editing",
                Tools = [FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.ShellExecute, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = subAgentDisallowedTools
            },
            new()
            {
                AgentType = AgentTypeDefinition.Search.ToValue(),
                WhenToUse = "Code search, navigation and exploration",
                Description = "Search agent focused on code search and navigation",
                Tools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = [FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, ShellToolNameConstants.ShellExecute]
            },
            new()
            {
                AgentType = AgentTypeDefinition.Explore.ToValue(),
                WhenToUse = "Quick codebase exploration agent for file pattern search, keyword search, and codebase Q&A. Supports thoroughness levels: quick/medium/very thorough",
                Description = "Explore agent — strictly read-only, for searching and understanding code",
                Tools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.ShellExecute],
                DisallowedTools = readOnlyDisallowedTools,
                IsBackground = false,
                OmitClaudeMd = true,
                OmitGitStatus = true
            },
            new()
            {
                AgentType = AgentTypeDefinition.Plan.ToValue(),
                WhenToUse = "Software architect agent that designs implementation plans, returns step-by-step plans, key files, and architectural trade-offs",
                Description = "Plan agent — strictly read-only, for designing implementation plans",
                Tools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.ShellExecute],
                DisallowedTools = readOnlyDisallowedTools,
                IsBackground = false,
                OmitClaudeMd = true,
                OmitGitStatus = true
            }
        ];
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadUserDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var agentsPath = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder, AppDataConstants.AgentsFolderName);

        if (_fs.DirectoryExists(agentsPath))
        {
            var loaded = await LoadDefinitionsFromDirectoryAsync(agentsPath, cancellationToken).ConfigureAwait(false);
            definitions.AddRange(loaded);
        }

        return definitions;
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadProjectDefinitionsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var currentDirPath = _fs.GetFullPath(workingDirectory);

        while (currentDirPath != null)
        {
            // 并行扫描所有项目代理目录
            var dirTasks = new List<Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>>();
            foreach (var agentDir in ProjectAgentDirs)
            {
                var fullPath = Path.Combine(currentDirPath, agentDir);
                if (_fs.DirectoryExists(fullPath))
                {
                    dirTasks.Add(LoadDefinitionsFromDirectoryAsync(fullPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var loaded in dirResults)
            {
                definitions.AddRange(loaded);
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        return definitions;
    }

    private async Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> LoadDefinitionsFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var definitions = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();

        try
        {
            var mdFiles = _fs.GetFiles(directoryPath, "*.md", SearchOption.AllDirectories);

            // 并行读取所有 md 文件
            var readTasks = new List<Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?>>();
            foreach (var filePath in mdFiles)
            {
                readTasks.Add(TryReadDefinitionFileAsync(filePath, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var definition in readResults)
            {
                if (definition is not null)
                {
                    definitions.Add(definition);
                    _logger?.LogInformation("已加载代理定义: {AgentType} ({Path})", definition.AgentType, definition.SourcePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.ScanAgentDefinitionFailedLog, directoryPath));
        }

        return definitions;
    }

    private async Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?> TryReadDefinitionFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            return ParseDefinitionFile(content, filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取代理定义文件失败: {Path}", filePath);
            return null;
        }
    }

    internal static JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? ParseDefinitionFile(string content, string sourcePath)
    {
        var result = FrontmatterParser.Parse(content);

        var agentType = GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(agentType)) return null;

        var whenToUse = GetStringFromData(result.Data, "when_to_use", "whenToUse", "description");
        if (string.IsNullOrWhiteSpace(whenToUse))
            whenToUse = $"自定义代理: {agentType}";

        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            AgentType = agentType,
            WhenToUse = whenToUse,
            Description = GetStringFromData(result.Data, "description", "desc"),
            Tools = GetStringListFromData(result.Data, "tools", "allowed_tools"),
            DisallowedTools = GetStringListFromData(result.Data, "disallowed_tools", "denied_tools"),
            ModelName = GetStringFromData(result.Data, "model", "model_name"),
            Temperature = GetFloatFromData(result.Data, "temperature"),
            MaxTokens = GetIntFromData(result.Data, "max_tokens"),
            IsBackground = GetBoolFromData(result.Data, "is_background", "background"),
            SystemPrompt = result.HasFrontmatter ? result.Content.Trim() : content.Trim(),
            SourcePath = sourcePath,
            Skills = GetStringListFromData(result.Data, "skills", "preload_skills"),
            PermissionMode = GetStringFromData(result.Data, "permission_mode", "permissionMode"),
            Hooks = ParseHooksFromData(result.Data),
            McpServers = ParseMcpServersFromData(result.Data),
            RequiredMcpServers = GetStringListFromData(result.Data, "required_mcp_servers", "requiredMcpServers"),
            Memory = ParseMemoryScopeFromData(result.Data)
        };

        var nameOverride = GetStringFromData(result.Data, "name", "agent_type", "type");
        if (!string.IsNullOrWhiteSpace(nameOverride))
            definition.AgentType = nameOverride;

        return definition;
    }

    private static string GetFileNameWithoutExtension(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(fileName)) return string.Empty;

        var dirPart = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dirPart)) return fileName;

        var baseDirLength = dirPart.Length + 1;
        if (path.Length > baseDirLength)
        {
            var relativePath = path[baseDirLength..];
            var relativeNoExt = Path.ChangeExtension(relativePath, null);
            return relativeNoExt.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        return fileName;
    }

    private static string? GetStringFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var element) && element.ValueKind == System.Text.Json.JsonValueKind.String)
                return element.GetString();
        }
        return null;
    }

    private static List<string>? GetStringListFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!data.TryGetValue(key, out var element))
                continue;

            return element.ValueKind switch
            {
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

    private static float? GetFloatFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var element))
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetSingle(out var value))
                    return value;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && float.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static int? GetIntFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var element))
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetInt32(out var value))
                    return value;
                if (element.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static bool GetBoolFromData(Dictionary<string, System.Text.Json.JsonElement> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var element))
            {
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
    private static AgentMemoryScope? ParseMemoryScopeFromData(Dictionary<string, System.Text.Json.JsonElement> data)
    {
        var memoryStr = GetStringFromData(data, "memory");
        return AgentMemoryScopeExtensions.FromValue(memoryStr);
    }

    private static Dictionary<string, List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>>? ParseHooksFromData(
        Dictionary<string, System.Text.Json.JsonElement> data)
    {
        if (!data.TryGetValue("hooks", out var hooksElement) || hooksElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        var result = new Dictionary<string, List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventProp in hooksElement.EnumerateObject())
        {
            if (eventProp.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;

            var matchers = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher>();
            foreach (var matcherElement in eventProp.Value.EnumerateArray())
            {
                var matcher = ParseHookMatcher(matcherElement);
                if (matcher is not null)
                    matchers.Add(matcher);
            }

            if (matchers.Count > 0)
                result[eventProp.Name] = matchers;
        }

        return result.Count > 0 ? result : null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher? ParseHookMatcher(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        string? matcher = null;
        if (element.TryGetProperty("matcher", out var matcherProp) && matcherProp.ValueKind == System.Text.Json.JsonValueKind.String)
            matcher = matcherProp.GetString();

        if (!element.TryGetProperty("hooks", out var hooksProp) || hooksProp.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        var hooks = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand>();
        foreach (var hookElement in hooksProp.EnumerateArray())
        {
            var hook = ParseHookCommand(hookElement);
            if (hook is not null)
                hooks.Add(hook);
        }

        if (hooks.Count == 0)
            return null;

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookMatcher { Matcher = matcher, Hooks = hooks };
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand? ParseHookCommand(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        var type = element.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == System.Text.Json.JsonValueKind.String
            ? typeProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(type))
            return null;

        var command = element.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == System.Text.Json.JsonValueKind.String
            ? cmdProp.GetString()
            : null;

        var prompt = element.TryGetProperty("prompt", out var promptProp) && promptProp.ValueKind == System.Text.Json.JsonValueKind.String
            ? promptProp.GetString()
            : null;

        var ifCondition = element.TryGetProperty("if", out var ifProp) && ifProp.ValueKind == System.Text.Json.JsonValueKind.String
            ? ifProp.GetString()
            : null;

        var timeout = element.TryGetProperty("timeout", out var timeoutProp) && timeoutProp.ValueKind == System.Text.Json.JsonValueKind.Number
            && timeoutProp.TryGetInt32(out var timeoutVal)
            ? timeoutVal
            : (int?)null;

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentHookCommand
        {
            Type = type,
            Command = command,
            Prompt = prompt,
            If = ifCondition,
            Timeout = timeout
        };
    }

    private static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec>? ParseMcpServersFromData(
        Dictionary<string, System.Text.Json.JsonElement> data)
    {
        if (!data.TryGetValue("mcpServers", out var element) && !data.TryGetValue("mcp_servers", out element))
            return null;

        if (element.ValueKind != System.Text.Json.JsonValueKind.Array)
            return null;

        var specs = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec>();
        foreach (var item in element.EnumerateArray())
        {
            var spec = ParseSingleMcpServerSpec(item);
            if (spec is not null)
                specs.Add(spec);
        }

        return specs.Count > 0 ? specs : null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec? ParseSingleMcpServerSpec(System.Text.Json.JsonElement item)
    {
        if (item.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var name = item.GetString();
            return string.IsNullOrEmpty(name) ? null : JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec.FromReference(name);
        }

        if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in item.EnumerateObject())
            {
                if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
                    continue;

                var config = ParseInlineMcpConfig(prop.Value);
                return JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerSpec.FromInline(prop.Name, config);
            }
        }

        return null;
    }

    private static JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerInlineConfig ParseInlineMcpConfig(System.Text.Json.JsonElement obj)
    {
        string? command = null;
        if (obj.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == System.Text.Json.JsonValueKind.String)
            command = cmdProp.GetString();

        List<string>? args = null;
        if (obj.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
            args = argsProp.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .ToList();

        Dictionary<string, string>? env = null;
        if (obj.TryGetProperty("env", out var envProp) && envProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            env = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var envItem in envProp.EnumerateObject())
            {
                if (envItem.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    env[envItem.Name] = envItem.Value.GetString() ?? string.Empty;
            }
        }

        string? url = null;
        if (obj.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == System.Text.Json.JsonValueKind.String)
            url = urlProp.GetString();

        string? transportType = null;
        if (obj.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == System.Text.Json.JsonValueKind.String)
            transportType = typeProp.GetString();

        Dictionary<string, string>? headers = null;
        if (obj.TryGetProperty("headers", out var headersProp) && headersProp.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var hdr in headersProp.EnumerateObject())
            {
                if (hdr.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    headers[hdr.Name] = hdr.Value.GetString() ?? string.Empty;
            }
        }

        return new JoinCode.Abstractions.Prompts.ToolPrompts.AgentMcpServerInlineConfig
        {
            Command = command,
            Args = args,
            Env = env,
            Url = url,
            TransportType = transportType,
            Headers = headers
        };
    }

    private static List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> Deduplicate(
        List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition> definitions)
    {
        var result = new List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>();
        var indexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in definitions)
        {
            if (indexMap.TryAdd(def.AgentType, result.Count))
            {
                result.Add(def);
            }
            else if (def.SourcePath is not null)
            {
                var existingIdx = indexMap[def.AgentType];
                result[existingIdx] = def;
            }
        }

        return result;
    }
}
