namespace JoinCode.Abstractions.Interfaces.Context;

/// <summary>
/// 压缩选项
/// </summary>
[RegisterOptions]
public class CompressionOptions
{
    /// <summary>
    /// 目标压缩比率 (0-1)，例如 0.5 表示压缩到原大小的50%
    /// </summary>
    public double TargetCompressionRatio { get; set; } = 0.5;

    /// <summary>
    /// 是否保留函数/方法签名
    /// </summary>
    public bool PreserveSignatures { get; set; } = true;

    /// <summary>
    /// 是否保留关键注释
    /// </summary>
    public bool PreserveComments { get; set; } = true;

    /// <summary>
    /// 最大输出token数
    /// </summary>
    public int MaxOutputTokens { get; set; } = 4000;

    /// <summary>
    /// 是否保留导入/引用语句
    /// </summary>
    public bool PreserveImports { get; set; } = true;

    /// <summary>
    /// 是否保留类型定义
    /// </summary>
    public bool PreserveTypeDefinitions { get; set; } = true;

    /// <summary>
    /// 对话历史保留的轮数
    /// </summary>
    public int DialogueRoundsToPreserve { get; set; } = 3;

    /// <summary>
    /// 是否生成摘要替代完整内容
    /// </summary>
    public bool UseSummarization { get; set; } = true;

    /// <summary>
    /// 摘要的最大长度
    /// </summary>
    public int MaxSummaryLength { get; set; } = 500;

    /// <summary>
    /// 是否保留关键决策点
    /// </summary>
    public bool PreserveKeyDecisions { get; set; } = true;

    /// <summary>
    /// 引用索引保留的最大条目数
    /// </summary>
    public int MaxReferenceEntries { get; set; } = WorkflowConstants.ContextCompression.MaxReferenceEntries;

    /// <summary>
    /// 是否启用智能压缩（根据内容自适应）
    /// </summary>
    public bool EnableSmartCompression { get; set; } = true;

    /// <summary>
    /// 最小压缩长度阈值，小于此值不压缩
    /// </summary>
    public int MinCompressionThreshold { get; set; } = WorkflowConstants.ContextCompression.MinCompressionThreshold;

    /// <summary>
    /// 压缩超时时间（毫秒）
    /// </summary>
    public int CompressionTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// 是否保留文档字符串/XML注释
    /// </summary>
    public bool PreserveDocumentation { get; set; } = true;

    /// <summary>
    /// 代码压缩时保留的方法体最大行数
    /// </summary>
    public int MaxMethodBodyLines { get; set; } = 0;

    /// <summary>
    /// 是否保留常量定义
    /// </summary>
    public bool PreserveConstants { get; set; } = true;

    /// <summary>
    /// 是否保留枚举定义
    /// </summary>
    public bool PreserveEnums { get; set; } = true;

    /// <summary>
    /// 创建默认选项
    /// </summary>
    public static CompressionOptions Default => new();

    /// <summary>
    /// 创建轻量级压缩选项
    /// </summary>
    public static CompressionOptions Light => new()
    {
        TargetCompressionRatio = 0.8,
        PreserveSignatures = true,
        PreserveComments = true,
        MaxOutputTokens = 8000,
        UseSummarization = false
    };

    /// <summary>
    /// 创建激进压缩选项
    /// </summary>
    public static CompressionOptions Aggressive => new()
    {
        TargetCompressionRatio = 0.3,
        PreserveSignatures = true,
        PreserveComments = false,
        MaxOutputTokens = 2000,
        UseSummarization = true,
        MaxSummaryLength = 200,
        PreserveImports = false,
        PreserveDocumentation = false
    };

    /// <summary>
    /// 创建代码专用压缩选项
    /// </summary>
    public static CompressionOptions ForCode => new()
    {
        TargetCompressionRatio = 0.4,
        PreserveSignatures = true,
        PreserveComments = true,
        PreserveImports = true,
        PreserveTypeDefinitions = true,
        PreserveDocumentation = true,
        PreserveConstants = true,
        PreserveEnums = true,
        MaxMethodBodyLines = 0,
        UseSummarization = false
    };

    /// <summary>
    /// 创建对话专用压缩选项
    /// </summary>
    public static CompressionOptions ForDialogue => new()
    {
        TargetCompressionRatio = 0.5,
        DialogueRoundsToPreserve = 2,
        UseSummarization = true,
        MaxSummaryLength = 400,
        PreserveKeyDecisions = true
    };

    /// <summary>
    /// 创建引用索引专用压缩选项
    /// </summary>
    public static CompressionOptions ForReferenceIndex => new()
    {
        TargetCompressionRatio = 0.6,
        MaxReferenceEntries = 50,
        PreserveSignatures = true,
        UseSummarization = false
    };
}

/// <summary>
/// 压缩选项构建器 - 支持链式配置
/// </summary>
public sealed class CompressionOptionsBuilder
{
    private readonly CompressionOptions _options;

    private CompressionOptionsBuilder(CompressionOptions initialOptions)
    {
        _options = new CompressionOptions
        {
            TargetCompressionRatio = initialOptions.TargetCompressionRatio,
            PreserveSignatures = initialOptions.PreserveSignatures,
            PreserveComments = initialOptions.PreserveComments,
            MaxOutputTokens = initialOptions.MaxOutputTokens,
            PreserveImports = initialOptions.PreserveImports,
            PreserveTypeDefinitions = initialOptions.PreserveTypeDefinitions,
            DialogueRoundsToPreserve = initialOptions.DialogueRoundsToPreserve,
            UseSummarization = initialOptions.UseSummarization,
            MaxSummaryLength = initialOptions.MaxSummaryLength,
            PreserveKeyDecisions = initialOptions.PreserveKeyDecisions,
            MaxReferenceEntries = initialOptions.MaxReferenceEntries,
            EnableSmartCompression = initialOptions.EnableSmartCompression,
            MinCompressionThreshold = initialOptions.MinCompressionThreshold,
            CompressionTimeoutMs = initialOptions.CompressionTimeoutMs,
            PreserveDocumentation = initialOptions.PreserveDocumentation,
            MaxMethodBodyLines = initialOptions.MaxMethodBodyLines,
            PreserveConstants = initialOptions.PreserveConstants,
            PreserveEnums = initialOptions.PreserveEnums
        };
    }

    /// <summary>
    /// 从默认配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder Create() => new(CompressionOptions.Default);

    /// <summary>
    /// 从轻量级配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder CreateLight() => new(CompressionOptions.Light);

    /// <summary>
    /// 从激进压缩配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder CreateAggressive() => new(CompressionOptions.Aggressive);

    /// <summary>
    /// 从代码专用配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder CreateForCode() => new(CompressionOptions.ForCode);

    /// <summary>
    /// 从对话专用配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder CreateForDialogue() => new(CompressionOptions.ForDialogue);

    /// <summary>
    /// 从引用索引专用配置开始构建
    /// </summary>
    public static CompressionOptionsBuilder CreateForReferenceIndex() => new(CompressionOptions.ForReferenceIndex);

    /// <summary>
    /// 设置目标压缩比率
    /// </summary>
    public CompressionOptionsBuilder WithCompressionRatio(double ratio)
    {
        _options.TargetCompressionRatio = ratio;
        return this;
    }

    /// <summary>
    /// 设置是否保留函数/方法签名
    /// </summary>
    public CompressionOptionsBuilder WithPreserveSignatures(bool preserve)
    {
        _options.PreserveSignatures = preserve;
        return this;
    }

    /// <summary>
    /// 设置是否保留关键注释
    /// </summary>
    public CompressionOptionsBuilder WithPreserveComments(bool preserve)
    {
        _options.PreserveComments = preserve;
        return this;
    }

    /// <summary>
    /// 设置最大输出token数
    /// </summary>
    public CompressionOptionsBuilder WithMaxOutputTokens(int tokens)
    {
        _options.MaxOutputTokens = tokens;
        return this;
    }

    /// <summary>
    /// 设置是否保留导入/引用语句
    /// </summary>
    public CompressionOptionsBuilder WithPreserveImports(bool preserve)
    {
        _options.PreserveImports = preserve;
        return this;
    }

    /// <summary>
    /// 设置是否保留类型定义
    /// </summary>
    public CompressionOptionsBuilder WithPreserveTypeDefinitions(bool preserve)
    {
        _options.PreserveTypeDefinitions = preserve;
        return this;
    }

    /// <summary>
    /// 设置对话历史保留的轮数
    /// </summary>
    public CompressionOptionsBuilder WithDialogueRoundsToPreserve(int rounds)
    {
        _options.DialogueRoundsToPreserve = rounds;
        return this;
    }

    /// <summary>
    /// 设置是否生成摘要替代完整内容
    /// </summary>
    public CompressionOptionsBuilder WithUseSummarization(bool use)
    {
        _options.UseSummarization = use;
        return this;
    }

    /// <summary>
    /// 设置摘要的最大长度
    /// </summary>
    public CompressionOptionsBuilder WithMaxSummaryLength(int length)
    {
        _options.MaxSummaryLength = length;
        return this;
    }

    /// <summary>
    /// 设置是否保留关键决策点
    /// </summary>
    public CompressionOptionsBuilder WithPreserveKeyDecisions(bool preserve)
    {
        _options.PreserveKeyDecisions = preserve;
        return this;
    }

    /// <summary>
    /// 设置引用索引保留的最大条目数
    /// </summary>
    public CompressionOptionsBuilder WithMaxReferenceEntries(int entries)
    {
        _options.MaxReferenceEntries = entries;
        return this;
    }

    /// <summary>
    /// 设置是否启用智能压缩
    /// </summary>
    public CompressionOptionsBuilder WithEnableSmartCompression(bool enable)
    {
        _options.EnableSmartCompression = enable;
        return this;
    }

    /// <summary>
    /// 设置最小压缩长度阈值
    /// </summary>
    public CompressionOptionsBuilder WithMinCompressionThreshold(int threshold)
    {
        _options.MinCompressionThreshold = threshold;
        return this;
    }

    /// <summary>
    /// 设置压缩超时时间（毫秒）
    /// </summary>
    public CompressionOptionsBuilder WithCompressionTimeoutMs(int timeoutMs)
    {
        _options.CompressionTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// 设置是否保留文档字符串/XML注释
    /// </summary>
    public CompressionOptionsBuilder WithPreserveDocumentation(bool preserve)
    {
        _options.PreserveDocumentation = preserve;
        return this;
    }

    /// <summary>
    /// 设置代码压缩时保留的方法体最大行数
    /// </summary>
    public CompressionOptionsBuilder WithMaxMethodBodyLines(int lines)
    {
        _options.MaxMethodBodyLines = lines;
        return this;
    }

    /// <summary>
    /// 设置是否保留常量定义
    /// </summary>
    public CompressionOptionsBuilder WithPreserveConstants(bool preserve)
    {
        _options.PreserveConstants = preserve;
        return this;
    }

    /// <summary>
    /// 设置是否保留枚举定义
    /// </summary>
    public CompressionOptionsBuilder WithPreserveEnums(bool preserve)
    {
        _options.PreserveEnums = preserve;
        return this;
    }

    /// <summary>
    /// 启用所有保留选项（最大保留）
    /// </summary>
    public CompressionOptionsBuilder WithMaximumPreservation()
    {
        _options.PreserveSignatures = true;
        _options.PreserveComments = true;
        _options.PreserveImports = true;
        _options.PreserveTypeDefinitions = true;
        _options.PreserveKeyDecisions = true;
        _options.PreserveDocumentation = true;
        _options.PreserveConstants = true;
        _options.PreserveEnums = true;
        return this;
    }

    /// <summary>
    /// 禁用所有保留选项（最小保留，最大压缩）
    /// </summary>
    public CompressionOptionsBuilder WithMinimumPreservation()
    {
        _options.PreserveSignatures = true; // 始终保留签名以保证代码可用
        _options.PreserveComments = false;
        _options.PreserveImports = false;
        _options.PreserveTypeDefinitions = false;
        _options.PreserveKeyDecisions = false;
        _options.PreserveDocumentation = false;
        _options.PreserveConstants = false;
        _options.PreserveEnums = false;
        return this;
    }

    /// <summary>
    /// 构建压缩选项
    /// </summary>
    public CompressionOptions Build() => _options;
}
