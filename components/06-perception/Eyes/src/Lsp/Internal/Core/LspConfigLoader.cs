
namespace Services.Lsp.Internal;

/// <summary>
/// LSP 配置加载器接口
/// 从配置文件加载 LSP 服务器配置
/// </summary>
public interface ILspConfigLoader
{
    /// <summary>
    /// 加载配置
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>LSP 配置列表</returns>
    Task<IReadOnlyList<LspServerConfigEntry>> LoadAsync(string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存配置
    /// </summary>
    /// <param name="configs">配置列表</param>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveAsync(IEnumerable<LspServerConfigEntry> configs, string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取默认配置文件路径
    /// </summary>
    string GetDefaultConfigPath();
}

/// <summary>
/// LSP 服务器配置条目
/// </summary>
public sealed record LspServerConfigEntry
{
    /// <summary>
    /// 服务器 ID
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// 服务器名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 参数
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 工作目录
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 支持的文件扩展名
    /// </summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 语言 ID
    /// </summary>
    public required string LanguageId { get; init; }

    /// <summary>
    /// 启动超时时间
    /// </summary>
    public int StartupTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// 环境变量
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 转换为 LSP 配置
    /// </summary>
    public LspInstanceConfig ToLspInstanceConfig()
    {
        var extToLang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in FileExtensions)
        {
            extToLang[ext] = LanguageId;
        }

        return new LspInstanceConfig
        {
            Name = ServerId,
            LanguageId = LanguageId,
            Command = Command,
            Arguments = Arguments.ToList(),
            WorkingDirectory = WorkingDirectory,
            StartupTimeout = StartupTimeoutSeconds > 0 ? TimeSpan.FromSeconds(StartupTimeoutSeconds) : null,
            Environment = EnvironmentVariables.Count > 0 ? EnvironmentVariables : null,
            ExtensionToLanguage = extToLang
        };
    }
}

/// <summary>
/// LSP 配置加载器实现
/// </summary>
[Register]
public sealed partial class LspConfigLoader : ILspConfigLoader
{
    [Inject] private readonly ILogger<LspConfigLoader>? _logger;
    [Inject] private readonly IFileSystem _fs;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LspServerConfigEntry>> LoadAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        var path = configPath ?? GetDefaultConfigPath();

        if (!_fs.FileExists(path))
        {
            _logger?.LogInformation("LSP config file not found: {Path}, generating default configs", path);
            var defaults = GetDefaultConfigs();
            await SaveAsync(defaults, path, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            _logger?.LogDebug("Loading LSP configs from: {Path}", path);
            var configs = await _fs.ReadAndDeserializeAsync(path, LspJsonContext.Default.ListLspServerConfigEntry, cancellationToken).ConfigureAwait(false);

            if (configs == null || configs.Count == 0)
            {
                _logger?.LogWarning("Empty or invalid LSP config file, using defaults");
                return GetDefaultConfigs();
            }

            var resolved = configs.Select(ResolveEnvironmentVariables).ToList();

            _logger?.LogInformation("Loaded {Count} LSP server configs", resolved.Count);
            return resolved.Where(c => c.Enabled).ToList();
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse LSP config file: {Path}", path);
            return GetDefaultConfigs();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load LSP config file: {Path}", path);
            return GetDefaultConfigs();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(IEnumerable<LspServerConfigEntry> configs, string? configPath = null, CancellationToken cancellationToken = default)
    {
        var path = configPath ?? GetDefaultConfigPath();
        var directory = Path.GetDirectoryName(path);

        DirectoryHelper.EnsureDirectoryExists(_fs, directory);

        var json = JsonSerializer.Serialize(configs.ToList(), LspJsonContext.Default.ListLspServerConfigEntry);
        await _fs.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Saved {Count} LSP server configs to: {Path}", configs.Count(), path);
    }

    /// <inheritdoc />
    public string GetDefaultConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            "lsp-servers.json");
    }

    /// <summary>
    /// 获取默认配置
    /// </summary>
    private static IReadOnlyList<LspServerConfigEntry> GetDefaultConfigs()
    {
        return new List<LspServerConfigEntry>
        {
            new()
            {
                ServerId = "omnisharp",
                Name = "C# (OmniSharp)",
                Command = "omnisharp",
                Arguments = new List<string> { "-lsp" },
                FileExtensions = new List<string> { ".cs", ".csx" },
                LanguageId = "csharp"
            },
            new()
            {
                ServerId = "typescript-language-server",
                Name = "TypeScript",
                Command = "typescript-language-server",
                Arguments = new List<string> { "--stdio" },
                FileExtensions = new List<string> { ".ts", ".tsx", ".js", ".jsx", ".mjs" },
                LanguageId = "typescript"
            },
            new()
            {
                ServerId = "pylsp",
                Name = "Python (pylsp)",
                Command = "pylsp",
                Arguments = new List<string>(),
                FileExtensions = new List<string> { ".py", ".pyw" },
                LanguageId = "python"
            },
            new()
            {
                ServerId = "rust-analyzer",
                Name = "Rust",
                Command = "rust-analyzer",
                Arguments = new List<string>(),
                FileExtensions = new List<string> { ".rs" },
                LanguageId = "rust"
            },
            new()
            {
                ServerId = "gopls",
                Name = "Go",
                Command = "gopls",
                Arguments = new List<string>(),
                FileExtensions = new List<string> { ".go" },
                LanguageId = "go"
            },
            new()
            {
                ServerId = "jdtls",
                Name = "Java",
                Command = "jdtls",
                Arguments = new List<string>(),
                FileExtensions = new List<string> { ".java" },
                LanguageId = "java"
            },
            new()
            {
                ServerId = "clangd",
                Name = "C/C++ (clangd)",
                Command = "clangd",
                Arguments = new List<string>(),
                FileExtensions = new List<string> { ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp" },
                LanguageId = "cpp"
            }
        };
    }

    private static LspServerConfigEntry ResolveEnvironmentVariables(LspServerConfigEntry config)
    {
        var resolvedCommand = ResolveEnvInString(config.Command);

        var resolvedArgs = config.Arguments as List<string> ?? config.Arguments.ToList();
        for (var i = 0; i < resolvedArgs.Count; i++)
        {
            resolvedArgs[i] = ResolveEnvInString(resolvedArgs[i]);
        }

        var resolvedWorkDir = config.WorkingDirectory is not null
            ? ResolveEnvInString(config.WorkingDirectory)
            : null;

        var resolvedEnv = new Dictionary<string, string>();
        foreach (var kvp in config.EnvironmentVariables)
        {
            resolvedEnv[kvp.Key] = ResolveEnvInString(kvp.Value);
        }

        return config with
        {
            Command = resolvedCommand,
            Arguments = resolvedArgs,
            WorkingDirectory = resolvedWorkDir,
            EnvironmentVariables = resolvedEnv
        };
    }

    private static string ResolveEnvInString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var span = value.AsSpan();
        var result = new System.Text.StringBuilder(value.Length);

        while (!span.IsEmpty)
        {
            var dollarIdx = span.IndexOf('$');
            if (dollarIdx < 0)
            {
                result.Append(span);
                break;
            }

            result.Append(span[..dollarIdx]);
            span = span[dollarIdx..];

            if (span.Length >= 2 && span[1] == '{')
            {
                var closeIdx = span.IndexOf('}');
                if (closeIdx > 0)
                {
                    var varName = span[2..closeIdx].ToString();
                    var envValue = Environment.GetEnvironmentVariable(varName) ?? "";
                    result.Append(envValue);
                    span = span[(closeIdx + 1)..];
                }
                else
                {
                    result.Append('$');
                    span = span[1..];
                }
            }
            else
            {
                result.Append('$');
                span = span[1..];
            }
        }

        return result.ToString();
    }
}
