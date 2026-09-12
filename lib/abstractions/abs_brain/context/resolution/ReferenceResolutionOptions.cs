
namespace JoinCode.Abstractions.Brain.Context.Resolution;

/// <summary>
/// 引用解析选项
/// </summary>
[RegisterOptions]
public sealed record ReferenceResolutionOptions
{
    /// <summary>
    /// 搜索深度限制（目录层级）
    /// </summary>
    public int SearchDepth { get; init; } = 10;

    /// <summary>
    /// 包含的文件模式列表（Glob 模式）
    /// </summary>
    public IReadOnlyList<string> IncludePatterns { get; init; } = DefaultIncludePatterns;

    /// <summary>
    /// 排除的文件模式列表（Glob 模式）
    /// </summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = DefaultExcludePatterns;

    /// <summary>
    /// 最小相关度评分阈值 (0.0 - 1.0)
    /// </summary>
    public double MinRelevanceScore { get; init; } = 0.3;

    /// <summary>
    /// 最大返回结果数
    /// </summary>
    public int MaxResults { get; init; } = 50;

    /// <summary>
    /// 是否启用模糊匹配
    /// </summary>
    public bool EnableFuzzyMatching { get; init; } = true;

    /// <summary>
    /// 模糊匹配的相似度阈值 (0.0 - 1.0)
    /// </summary>
    public double FuzzyMatchThreshold { get; init; } = 0.6;

    /// <summary>
    /// 项目根目录（可选，默认为当前目录）
    /// </summary>
    public string? ProjectRoot { get; init; }

    /// <summary>
    /// 是否包含子目录
    /// </summary>
    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>
    /// 默认包含模式
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultIncludePatterns = new[]
    {
        "**/*.cs",
        "**/*.ts",
        "**/*.tsx",
        "**/*.js",
        "**/*.jsx",
        "**/*.py",
        "**/*.java",
        "**/*.go",
        "**/*.rs",
        "**/*.cpp",
        "**/*.c",
        "**/*.h",
        "**/*.hpp",
        "**/*.md",
        "**/*.json",
        "**/*.xml",
        "**/*.yaml",
        "**/*.yml"
    };

    /// <summary>
    /// 默认排除模式 — VCS 内部路径由 SecurityPatterns.SensitiveFilePattern.VcsInternal 分类派生
    /// 确保与 SensitiveFilePattern 枚举保持单一数据源
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultExcludePatterns = BuildDefaultExcludePatterns();

    /// <summary>
    /// 构造默认排除模式:固定列表 + 从 VcsInternal 分类派生的 **/segment/** 模式
    /// </summary>
    private static IReadOnlyList<string> BuildDefaultExcludePatterns()
    {
        var fixedPatterns = new[]
        {
            "**/node_modules/**",
            "**/bin/**",
            "**/obj/**",
            "**/.vs/**",
            "**/dist/**",
            "**/build/**",
            "**/target/**",
            "**/*.min.js",
            "**/*.min.css",
        };
        var vcsGlobs = SecurityPatterns
            .GetPatternsForCategory(SensitiveFilePattern.VcsInternal)
            .Where(p => !p.Contains("**"))
            .Select(p => $"**/{p}/**");
        return fixedPatterns.Concat(vcsGlobs).ToArray();
    }

    /// <summary>
    /// 创建默认选项
    /// </summary>
    public static ReferenceResolutionOptions Default => new();

    /// <summary>
    /// 创建精确匹配选项
    /// </summary>
    public static ReferenceResolutionOptions ExactMatch => new()
    {
        EnableFuzzyMatching = false,
        MinRelevanceScore = 1.0
    };

    /// <summary>
    /// 创建模糊匹配选项
    /// </summary>
    public static ReferenceResolutionOptions FuzzyMatch => new()
    {
        EnableFuzzyMatching = true,
        FuzzyMatchThreshold = 0.5,
        MinRelevanceScore = 0.2
    };
}

/// <summary>
/// 引用解析选项构建器 - 支持链式配置
/// </summary>
public sealed class ReferenceResolutionOptionsBuilder
{
    private int _searchDepth = 10;
    private List<string> _includePatterns = new()
    {
        "**/*.cs", "**/*.ts", "**/*.tsx", "**/*.js", "**/*.jsx",
        "**/*.py", "**/*.java", "**/*.go", "**/*.rs",
        "**/*.cpp", "**/*.c", "**/*.h", "**/*.hpp",
        "**/*.md", "**/*.json", "**/*.xml", "**/*.yaml", "**/*.yml"
    };
    private List<string> _excludePatterns = new()
    {
        "**/node_modules/**", "**/bin/**", "**/obj/**",
        "**/.vs/**", "**/dist/**",
        "**/build/**", "**/target/**",
        "**/*.min.js", "**/*.min.css"
    };
    private double _minRelevanceScore = 0.3;
    private int _maxResults = 50;
    private bool _enableFuzzyMatching = true;
    private double _fuzzyMatchThreshold = 0.6;
    private string? _projectRoot;
    private bool _includeSubdirectories = true;

    private ReferenceResolutionOptionsBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static ReferenceResolutionOptionsBuilder Create() => new();

    /// <summary>
    /// 从默认选项开始
    /// </summary>
    public static ReferenceResolutionOptionsBuilder CreateFromDefault() => Create();

    /// <summary>
    /// 从精确匹配选项开始
    /// </summary>
    public static ReferenceResolutionOptionsBuilder CreateExactMatch() => Create()
        .DisableFuzzyMatching()
        .WithMinRelevanceScore(1.0);

    /// <summary>
    /// 从模糊匹配选项开始
    /// </summary>
    public static ReferenceResolutionOptionsBuilder CreateFuzzyMatch() => Create()
        .EnableFuzzyMatching()
        .WithFuzzyMatchThreshold(0.5)
        .WithMinRelevanceScore(0.2);

    /// <summary>
    /// 设置搜索深度限制
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithSearchDepth(int depth)
    {
        _searchDepth = depth;
        return this;
    }

    /// <summary>
    /// 设置包含的文件模式列表（替换现有）
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithIncludePatterns(params string[] patterns)
    {
        _includePatterns = patterns.ToList();
        return this;
    }

    /// <summary>
    /// 添加包含的文件模式
    /// </summary>
    public ReferenceResolutionOptionsBuilder AddIncludePattern(string pattern)
    {
        _includePatterns.Add(pattern);
        return this;
    }

    /// <summary>
    /// 设置排除的文件模式列表（替换现有）
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithExcludePatterns(params string[] patterns)
    {
        _excludePatterns = patterns.ToList();
        return this;
    }

    /// <summary>
    /// 添加排除的文件模式
    /// </summary>
    public ReferenceResolutionOptionsBuilder AddExcludePattern(string pattern)
    {
        _excludePatterns.Add(pattern);
        return this;
    }

    /// <summary>
    /// 设置最小相关度评分阈值
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithMinRelevanceScore(double score)
    {
        _minRelevanceScore = score;
        return this;
    }

    /// <summary>
    /// 设置最大返回结果数
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithMaxResults(int maxResults)
    {
        _maxResults = maxResults;
        return this;
    }

    /// <summary>
    /// 启用模糊匹配
    /// </summary>
    public ReferenceResolutionOptionsBuilder EnableFuzzyMatching()
    {
        _enableFuzzyMatching = true;
        return this;
    }

    /// <summary>
    /// 禁用模糊匹配
    /// </summary>
    public ReferenceResolutionOptionsBuilder DisableFuzzyMatching()
    {
        _enableFuzzyMatching = false;
        return this;
    }

    /// <summary>
    /// 设置是否启用模糊匹配
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithFuzzyMatching(bool enable)
    {
        _enableFuzzyMatching = enable;
        return this;
    }

    /// <summary>
    /// 设置模糊匹配的相似度阈值
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithFuzzyMatchThreshold(double threshold)
    {
        _fuzzyMatchThreshold = threshold;
        return this;
    }

    /// <summary>
    /// 设置项目根目录
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithProjectRoot(string projectRoot)
    {
        _projectRoot = projectRoot;
        return this;
    }

    /// <summary>
    /// 清除项目根目录（使用当前目录）
    /// </summary>
    public ReferenceResolutionOptionsBuilder ClearProjectRoot()
    {
        _projectRoot = null;
        return this;
    }

    /// <summary>
    /// 启用子目录搜索
    /// </summary>
    public ReferenceResolutionOptionsBuilder IncludeSubdirectories()
    {
        _includeSubdirectories = true;
        return this;
    }

    /// <summary>
    /// 禁用子目录搜索（仅搜索根目录）
    /// </summary>
    public ReferenceResolutionOptionsBuilder ExcludeSubdirectories()
    {
        _includeSubdirectories = false;
        return this;
    }

    /// <summary>
    /// 设置是否包含子目录
    /// </summary>
    public ReferenceResolutionOptionsBuilder WithIncludeSubdirectories(bool include)
    {
        _includeSubdirectories = include;
        return this;
    }

    /// <summary>
    /// 使用 C# 项目配置
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseCSharpProject()
    {
        _includePatterns = new List<string>
        {
            "**/*.cs",
            "**/*.csproj",
            "**/*.sln"
        };
        return this;
    }

    /// <summary>
    /// 使用 TypeScript/JavaScript 项目配置
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseTypeScriptProject()
    {
        _includePatterns = new List<string>
        {
            "**/*.ts",
            "**/*.tsx",
            "**/*.js",
            "**/*.jsx",
            "**/package.json",
            "**/tsconfig.json"
        };
        return this;
    }

    /// <summary>
    /// 使用 Python 项目配置
    /// </summary>
    public ReferenceResolutionOptionsBuilder UsePythonProject()
    {
        _includePatterns = new List<string>
        {
            "**/*.py",
            "**/requirements.txt",
            "**/pyproject.toml",
            "**/setup.py"
        };
        return this;
    }

    /// <summary>
    /// 使用文档配置（仅 Markdown）
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseDocumentationMode()
    {
        _includePatterns = new List<string>
        {
            "**/*.md",
            "**/*.mdx"
        };
        _excludePatterns = new List<string>();
        return this;
    }

    /// <summary>
    /// 使用宽松搜索配置（更多结果，更低阈值）
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseLooseSearch()
    {
        _minRelevanceScore = 0.1;
        _fuzzyMatchThreshold = 0.4;
        _maxResults = WorkflowConstants.ContextCompression.MaxReferenceEntries;
        _enableFuzzyMatching = true;
        return this;
    }

    /// <summary>
    /// 使用严格搜索配置（更少结果，更高阈值）
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseStrictSearch()
    {
        _minRelevanceScore = 0.7;
        _fuzzyMatchThreshold = 0.8;
        _maxResults = 20;
        _enableFuzzyMatching = false;
        return this;
    }

    /// <summary>
    /// 使用浅层搜索配置（搜索深度较浅）
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseShallowSearch()
    {
        _searchDepth = 3;
        _includeSubdirectories = false;
        return this;
    }

    /// <summary>
    /// 使用深层搜索配置（搜索深度较深）
    /// </summary>
    public ReferenceResolutionOptionsBuilder UseDeepSearch()
    {
        _searchDepth = 20;
        _includeSubdirectories = true;
        return this;
    }

    /// <summary>
    /// 清除所有包含模式
    /// </summary>
    public ReferenceResolutionOptionsBuilder ClearIncludePatterns()
    {
        _includePatterns.Clear();
        return this;
    }

    /// <summary>
    /// 清除所有排除模式
    /// </summary>
    public ReferenceResolutionOptionsBuilder ClearExcludePatterns()
    {
        _excludePatterns.Clear();
        return this;
    }

    /// <summary>
    /// 构建引用解析选项
    /// </summary>
    public ReferenceResolutionOptions Build()
    {
        return new ReferenceResolutionOptions
        {
            SearchDepth = _searchDepth,
            IncludePatterns = _includePatterns,
            ExcludePatterns = _excludePatterns,
            MinRelevanceScore = _minRelevanceScore,
            MaxResults = _maxResults,
            EnableFuzzyMatching = _enableFuzzyMatching,
            FuzzyMatchThreshold = _fuzzyMatchThreshold,
            ProjectRoot = _projectRoot,
            IncludeSubdirectories = _includeSubdirectories
        };
    }
}
