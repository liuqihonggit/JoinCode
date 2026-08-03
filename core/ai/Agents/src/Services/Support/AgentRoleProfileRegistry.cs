namespace Core.Agents;

using System.Collections.Frozen;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色注册表 — 管理 AgentRoleProfile 的注册和查询
/// 内置 Profile 在静态构造时注册，用户/项目自定义 Profile 通过 IAgentDefinitionProvider 运行时追加
/// </summary>
[Register(typeof(IAgentRoleRegistry))]
public sealed class AgentRoleProfileRegistry : IAgentRoleRegistry
{
    private readonly IAgentDefinitionProvider? _definitionProvider;
    private readonly ILogger<AgentRoleProfileRegistry>? _logger;
#pragma warning disable JCC4005
    private readonly SemaphoreSlim _loadLock = new(1, 1);
#pragma warning restore JCC4005
    private List<AgentRoleProfile> _profiles;
    private FrozenDictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile> _profileMap;
    private volatile bool _customLoaded;

    public AgentRoleProfileRegistry(
        IAgentDefinitionProvider? definitionProvider = null,
        ILogger<AgentRoleProfileRegistry>? logger = null)
    {
        _definitionProvider = definitionProvider;
        _logger = logger;
        _profiles = BuildBuiltInProfiles();
        _profileMap = BuildProfileMap(_profiles);
    }

    public void Register(AgentRoleProfile profile)
    {
        _loadLock.Wait();
        try
        {
            _profiles.Add(profile);
            _profileMap = BuildProfileMap(_profiles);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public AgentRoleProfile? GetProfile(AgentRole role, ExecutorVariant? variant = null)
    {
        EnsureCustomLoaded();
        return _profileMap.TryGetValue((role, variant), out var profile) ? profile : null;
    }

    public IReadOnlyList<AgentRoleProfile> GetAllProfiles()
    {
        EnsureCustomLoaded();
        return _profiles.AsReadOnly();
    }

    public IReadOnlyList<AgentRoleProfile> GetProfilesByRole(AgentRole role)
    {
        EnsureCustomLoaded();
        return _profiles.Where(p => p.Role == role).ToList().AsReadOnly();
    }

    public IReadOnlyList<ExecutorVariant> GetAvailableVariants()
    {
        EnsureCustomLoaded();
        return _profiles
            .Where(p => p.Variant.HasValue)
            .Select(p => p.Variant!.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList()
            .AsReadOnly();
    }

    public void ClearCache()
    {
        _loadLock.Wait();
        try
        {
            _customLoaded = false;
            _profiles = BuildBuiltInProfiles();
            _profileMap = BuildProfileMap(_profiles);
        }
        finally
        {
            _loadLock.Release();
        }
        _logger?.LogDebug("AgentRoleProfileRegistry 缓存已清除");
    }

    private void EnsureCustomLoaded()
    {
        if (_customLoaded || _definitionProvider is null)
            return;

        _loadLock.Wait();
        try
        {
            if (_customLoaded)
                return;

            try
            {
                var definitions = _definitionProvider.GetAgentDefinitionsAsync().GetAwaiter().GetResult();
                foreach (var def in definitions)
                {
                    var key = (def.Role, def.Variant);
                    if (!_profileMap.ContainsKey(key))
                    {
                        var profile = new AgentRoleProfile
                        {
                            Role = def.Role,
                            Variant = def.Variant,
                            WhenToUse = def.WhenToUse,
                            Description = def.Description,
                            SystemPrompt = def.SystemPrompt,
                            AllowedTools = def.Tools,
                            DisallowedTools = def.DisallowedTools,
                            PermissionMode = def.PermissionMode,
                            IsBackground = def.IsBackground,
                            OmitClaudeMd = def.OmitClaudeMd,
                            OmitGitStatus = def.OmitGitStatus,
                            IsOneShot = def.Variant.HasValue && OneShotExecutorVariants.IsOneShot(def.Variant.Value),
                            ModelName = def.ModelName,
                            Temperature = def.Temperature,
                            MaxTokens = def.MaxTokens,
                            Memory = def.Memory,
                            Skills = def.Skills,
                            SourcePath = def.SourcePath,
                        };
                        _profiles.Add(profile);
                    }
                }
                _profileMap = BuildProfileMap(_profiles);
                _customLoaded = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "加载自定义 AgentDefinition 失败，仅使用内置 Profile");
                _customLoaded = true;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static FrozenDictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile> BuildProfileMap(
        List<AgentRoleProfile> profiles)
    {
        var builder = new Dictionary<(AgentRole, ExecutorVariant?), AgentRoleProfile>();
        foreach (var p in profiles)
        {
            builder.TryAdd((p.Role, p.Variant), p);
        }
        return builder.ToFrozenDictionary();
    }

    internal static List<AgentRoleProfile> BuildBuiltInProfiles()
    {
        var readOnlyDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite, NotebookToolNameConstants.NotebookEdit
        };

        var subAgentDisallowedTools = new List<string>
        {
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        };

        return
        [
            new()
            {
                Role = AgentRole.Coordinator,
                WhenToUse = "General tasks with full toolset",
                Description = "Coordinator agent — manages Goal lifecycle, full toolset",
                AllowedTools = null,
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "Code reading, writing, editing and refactoring",
                Description = "Code agent focused on code reading, writing and editing",
                AllowedTools = [FileToolNameConstants.FileRead, FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.Bash, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = subAgentDisallowedTools,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Search,
                WhenToUse = "Code search, navigation and exploration",
                Description = "Search agent focused on code search and navigation",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase],
                DisallowedTools = [FileToolNameConstants.FileWrite, FileToolNameConstants.FileEdit, ShellToolNameConstants.Bash],
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Explore,
                WhenToUse = "Quick codebase exploration agent for file pattern search, keyword search, and codebase Q&A. Supports thoroughness levels: quick/medium/very thorough",
                Description = "Explore agent — strictly read-only, for searching and understanding code",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitClaudeMd = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Plan,
                WhenToUse = "Software architect agent that designs implementation plans, returns step-by-step plans, key files, and architectural trade-offs",
                Description = "Plan agent — strictly read-only, for designing implementation plans",
                AllowedTools = [FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash],
                DisallowedTools = readOnlyDisallowedTools,
                OmitClaudeMd = true,
                OmitGitStatus = true,
                IsOneShot = true,
            },
            new()
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
                Description = "Doctor agent — 自举修复，后台运行，Cron 调度每12h复盘",
                AllowedTools = [FileToolNameConstants.FileRead, FileToolNameConstants.FileEdit, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, ShellToolNameConstants.Bash],
                DisallowedTools = [AgentToolNameConstants.Agent],
                IsBackground = true,
                PermissionMode = "doctor",
            }
        ];
    }
}
