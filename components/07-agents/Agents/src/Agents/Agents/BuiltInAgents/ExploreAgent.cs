
namespace Core.Agents;

/// <summary>
/// 探索 Agent - 探索代码库结构
/// </summary>
public sealed class ExploreAgent : BuiltInAgentBase
{
    private readonly IFileSystem _fs;
    private readonly IReferenceResolver? _referenceResolver;
    private readonly IProgressiveDisclosure? _progressiveDisclosure;

    public override string Name => "ExploreAgent";
    public override string Description => "探索代码库结构，识别关键模块和组件，理解代码之间的关系";
    public override BuiltInAgentType AgentType => BuiltInAgentType.Explore;
    public override string SystemPrompt => AgentPrompts.ExploreAgentSystemPrompt;

    public ExploreAgent(
        IChatClient kernel,
        IClockService clock,
        IFileSystem fs,
        ILogger<ExploreAgent>? logger = null)
        : base(kernel, clock, logger)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public ExploreAgent(
        IChatClient kernel,
        IClockService clock,
        IFileSystem fs,
        IReferenceResolver referenceResolver,
        ILogger<ExploreAgent>? logger = null)
        : base(kernel, clock, logger)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _referenceResolver = referenceResolver;
    }

    public ExploreAgent(
        IChatClient kernel,
        IClockService clock,
        IFileSystem fs,
        IReferenceResolver referenceResolver,
        IProgressiveDisclosure progressiveDisclosure,
        ILogger<ExploreAgent>? logger = null)
        : base(kernel, clock, logger)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _referenceResolver = referenceResolver;
        _progressiveDisclosure = progressiveDisclosure;
    }

    /// <summary>
    /// 探索代码库
    /// </summary>
    public async Task<ExploreResult> ExploreCodebaseAsync(
        ExploreRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildExplorePrompt(request);
        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ExploreResult
        {
            Success = true,
            ExploreId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 分析特定组件
    /// </summary>
    public async Task<ExploreResult> AnalyzeComponentAsync(
        string componentName,
        string componentType,
        CancellationToken cancellationToken = default)
    {
        var prompt = $"""
请深入分析以下组件：

## 组件名称
{componentName}

## 组件类型
{componentType}

请提供：
1. 组件职责和用途
2. 公共接口和方法
3. 依赖关系
4. 使用示例
5. 相关测试（如果有）
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ExploreResult
        {
            Success = true,
            ExploreId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 查找代码示例
    /// </summary>
    public async Task<ExploreResult> FindExamplesAsync(
        string topic,
        CancellationToken cancellationToken = default)
    {
        // 如果存在引用解析器，先尝试解析引用
        if (_referenceResolver != null)
        {
            return await FindExamplesWithReferenceAsync(topic, cancellationToken).ConfigureAwait(false);
        }

        var prompt = $"""
请在代码库中查找与以下主题相关的示例代码：

## 主题
{topic}

请提供：
1. 相关的代码文件路径
2. 关键代码片段
3. 使用场景说明
4. 最佳实践建议
""";

        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ExploreResult
        {
            Success = true,
            ExploreId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 基于引用探索代码
    /// </summary>
    public async Task<ExploreReferenceResult> ExploreWithReferenceAsync(
        ExploreReferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_referenceResolver == null)
        {
            return ExploreReferenceResult.FailureResult("未配置引用解析器，无法解析代码引用");
        }

        var stopwatch = Stopwatch.StartNew();
        var exploreId = Guid.NewGuid().ToString("N")[..8];

        try
        {
            if (_progressiveDisclosure is not null)
            {
                return await ExploreWithProgressiveDisclosureAsync(request, exploreId, stopwatch, cancellationToken).ConfigureAwait(false);
            }

            var resolvedReference = await _referenceResolver.ResolveCodeReferenceAsync(
                request.ReferencePath,
                new ReferenceResolutionOptions
                {
                    MaxResults = request.MaxFiles,
                    EnableFuzzyMatching = true
                },
                cancellationToken).ConfigureAwait(false);

            if (!resolvedReference.IsResolved)
            {
                return ExploreReferenceResult.FailureResult($"无法解析引用: {request.ReferencePath}");
            }

            var exploredFiles = await CollectExploredFilesAsync(
                resolvedReference,
                request,
                cancellationToken).ConfigureAwait(false);

            var prompt = BuildReferencePrompt(request, resolvedReference, exploredFiles);
            var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var codeSnippets = ExtractCodeSnippets(exploredFiles, request.FocusArea);

            return new ExploreReferenceResult
            {
                Success = true,
                ResolvedReference = resolvedReference,
                Files = exploredFiles,
                Summary = response.Content,
                RelevantCodeSnippets = codeSnippets,
                ExploreId = exploreId,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                TokenUsage = response.TokenUsage
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "基于引用探索代码时出错: {ReferencePath}", request.ReferencePath);
            return ExploreReferenceResult.FailureResult($"探索失败: {ex.Message}");
        }
    }

    private async Task<ExploreReferenceResult> ExploreWithProgressiveDisclosureAsync(
        ExploreReferenceRequest request,
        string exploreId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var disclosureLevel = request.SearchDepth switch
        {
            1 => DisclosureLevel.Index,
            2 => DisclosureLevel.Relationships,
            _ => DisclosureLevel.Source
        };

        var disclosureResult = await _progressiveDisclosure!.DiscloseAsync(
            request.ReferencePath, disclosureLevel, cancellationToken).ConfigureAwait(false);

        if (disclosureResult.Symbols.Count == 0)
        {
            var resolvedRef = await _referenceResolver!.ResolveCodeReferenceAsync(
                request.ReferencePath,
                new ReferenceResolutionOptions { MaxResults = request.MaxFiles, EnableFuzzyMatching = true },
                cancellationToken).ConfigureAwait(false);

            if (!resolvedRef.IsResolved)
            {
                return ExploreReferenceResult.FailureResult($"无法解析引用: {request.ReferencePath}");
            }

            var fallbackFiles = await CollectExploredFilesAsync(resolvedRef, request, cancellationToken).ConfigureAwait(false);
            var fallbackPrompt = BuildReferencePrompt(request, resolvedRef, fallbackFiles);
            var fallbackResponse = await ProcessAsync(fallbackPrompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ExploreReferenceResult
            {
                Success = true,
                ResolvedReference = resolvedRef,
                Files = fallbackFiles,
                Summary = fallbackResponse.Content,
                RelevantCodeSnippets = ExtractCodeSnippets(fallbackFiles, request.FocusArea),
                ExploreId = exploreId,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                TokenUsage = fallbackResponse.TokenUsage
            };
        }

        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请基于代码索引的渐进式探索结果进行分析：");
        prompt.AppendLine();
        prompt.AppendLine($"## 原始引用\n{request.ReferencePath}");
        prompt.AppendLine($"## 披露层级\n{disclosureResult.Level}");

        if (!string.IsNullOrWhiteSpace(request.FocusArea))
        {
            prompt.AppendLine($"## 关注领域\n{request.FocusArea}");
        }

        prompt.AppendLine();
        prompt.AppendLine(disclosureResult.FormattedContent);

        prompt.AppendLine();
        prompt.AppendLine("请提供：");
        prompt.AppendLine("1. 代码结构和架构概述");
        prompt.AppendLine("2. 关键组件和职责说明");
        prompt.AppendLine("3. 调用关系和依赖分析");
        prompt.AppendLine("4. 使用示例和最佳实践");

        var response = await ProcessAsync(prompt.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var codeSnippets = disclosureResult.SourceSnippets?.Select(s => new CodeSnippet
        {
            SourceFile = s.FilePath,
            Content = s.Content,
            StartLine = s.StartLine,
            EndLine = s.EndLine,
            Language = "csharp",
            Description = $"符号 {s.SymbolName} 的源代码",
            RelevanceScore = 0.95
        }).ToList() as IReadOnlyList<CodeSnippet> ?? [];

        var resolvedReference = CodeReference.ExactMatch(
            request.ReferencePath,
            disclosureResult.Symbols.FirstOrDefault()?.FilePath ?? "",
            disclosureResult.Symbols.Select(s => FileMatch.Create(
                s.FilePath,
                ReferenceMatchType.Exact,
                0.95,
                $"CodeIndex: {s.Kind} {s.Name}")).ToList());

        return new ExploreReferenceResult
        {
            Success = true,
            ResolvedReference = resolvedReference,
            Files = [],
            Summary = response.Content,
            RelevantCodeSnippets = codeSnippets,
            ExploreId = exploreId,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            TokenUsage = response.TokenUsage
        };
    }

    /// <summary>
    /// 解析引用并探索
    /// </summary>
    public async Task<ExploreReferenceResult> ResolveAndExploreAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var request = ExploreReferenceRequest.Create(reference);
        return await ExploreWithReferenceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析引用并探索（带关注领域）
    /// </summary>
    public async Task<ExploreReferenceResult> ResolveAndExploreAsync(
        string reference,
        string focusArea,
        CancellationToken cancellationToken = default)
    {
        var request = ExploreReferenceRequest.CreateWithFocus(reference, focusArea);
        return await ExploreWithReferenceAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExploreResult> FindExamplesWithReferenceAsync(
        string topic,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. 尝试解析主题作为引用
        var resolvedReference = await _referenceResolver!.ResolveCodeReferenceAsync(
            topic,
            new ReferenceResolutionOptions { MaxResults = 10 },
            cancellationToken).ConfigureAwait(false);

        // 2. 如果解析失败，尝试查找匹配文件
        IReadOnlyList<CodeReference> matchingFiles = Array.Empty<CodeReference>();
        if (!resolvedReference.IsResolved)
        {
            matchingFiles = await _referenceResolver.FindMatchingFilesAsync(
                topic,
                new ReferenceResolutionOptions { MaxResults = 10 },
                cancellationToken).ConfigureAwait(false);
        }

        // 3. 构建增强提示词
        var prompt = BuildExamplePromptWithReferences(topic, resolvedReference, matchingFiles);
        var response = await ProcessAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return new ExploreResult
        {
            Success = true,
            ExploreId = Guid.NewGuid().ToString("N")[..8],
            Content = response.Content,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds + response.ExecutionTimeMs,
            TokenUsage = response.TokenUsage
        };
    }

    private async Task<IReadOnlyList<ExploredFile>> CollectExploredFilesAsync(
        CodeReference resolvedReference,
        ExploreReferenceRequest request,
        CancellationToken cancellationToken)
    {
        var fileMatches = resolvedReference.FileMatches.Take(request.MaxFiles).ToArray();

        var readTasks = fileMatches.Select(async fileMatch =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!_fs.FileExists(fileMatch.FilePath))
                {
                    return null;
                }

                var lastModified = _fs.GetLastWriteTimeUtc(fileMatch.FilePath);
                long fileSize;
                await using (var sizeStream = _fs.OpenRead(fileMatch.FilePath))
                {
                    fileSize = sizeStream.Length;
                }

                var exploredFile = new ExploredFile
                {
                    FilePath = fileMatch.FilePath,
                    RelativePath = Path.GetRelativePath(_fs.GetCurrentDirectory(), fileMatch.FilePath),
                    FileType = Path.GetExtension(fileMatch.FilePath).TrimStart('.'),
                    FileSize = fileSize,
                    LastModified = lastModified,
                    RelevanceScore = fileMatch.RelevanceScore,
                    MatchDescription = fileMatch.MatchDescription
                };

                // 如果请求包含文件内容且文件不太大，读取内容
                if (request.IncludeFileContent && fileSize < 100_000) // 限制 100KB
                {
                    var content = await _fs.ReadAllTextAsync(fileMatch.FilePath, cancellationToken).ConfigureAwait(false);
                    exploredFile = exploredFile with { Content = content };
                }

                return exploredFile;
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "读取文件失败: {FilePath}", fileMatch.FilePath);
                return null;
            }
        }).ToArray();

        var results = await Task.WhenAll(readTasks).ConfigureAwait(false);
        return results.Where(r => r is not null).ToList()!;
    }

    private static string BuildReferencePrompt(
        ExploreReferenceRequest request,
        CodeReference resolvedReference,
        IReadOnlyList<ExploredFile> exploredFiles)
    {
        var prompt = new System.Text.StringBuilder();

        prompt.AppendLine("请基于以下解析后的代码引用进行探索分析：");
        prompt.AppendLine();
        prompt.AppendLine($"## 原始引用\n{request.ReferencePath}");
        prompt.AppendLine();
        prompt.AppendLine($"## 解析后的路径\n{resolvedReference.ResolvedPath}");
        prompt.AppendLine($"## 匹配类型\n{resolvedReference.MatchType}");
        prompt.AppendLine($"## 相关度评分\n{resolvedReference.RelevanceScore:P}");

        if (!string.IsNullOrWhiteSpace(request.FocusArea))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 关注领域\n{request.FocusArea}");
        }

        if (request.Questions != null && request.Questions.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 需要回答的问题");
            for (int i = 0; i < request.Questions.Count; i++)
            {
                prompt.AppendLine($"{i + 1}. {request.Questions[i]}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine($"## 探索到的文件 ({exploredFiles.Count} 个)");
        foreach (var file in exploredFiles.OrderByDescending(f => f.RelevanceScore))
        {
            prompt.AppendLine($"- {file.RelativePath ?? file.FilePath} (相关度: {file.RelevanceScore:P})");
        }

        // 添加关键文件内容
        var keyFiles = exploredFiles
            .Where(f => !string.IsNullOrEmpty(f.Content))
            .OrderByDescending(f => f.RelevanceScore)
            .Take(5);

        foreach (var file in keyFiles)
        {
            prompt.AppendLine();
            prompt.AppendLine($"### {file.RelativePath ?? file.FilePath}");
            prompt.AppendLine($"```{file.FileType}");
            // 限制内容长度
            var content = file.Content!.Length > WorkflowConstants.Limits.FileContentTruncateLength
                ? file.Content[..WorkflowConstants.Limits.FileContentTruncateLength] + "\n... (内容已截断)"
                : file.Content;
            prompt.AppendLine(content);
            prompt.AppendLine("```");
        }

        prompt.AppendLine();
        prompt.AppendLine("请提供：");
        prompt.AppendLine("1. 代码结构和架构概述");
        prompt.AppendLine("2. 关键组件和职责说明");
        prompt.AppendLine("3. 文件之间的关系和依赖");
        prompt.AppendLine("4. 使用示例和最佳实践");
        prompt.AppendLine("5. 探索建议和下一步行动");

        return prompt.ToString();
    }

    private static string BuildExamplePromptWithReferences(
        string topic,
        CodeReference resolvedReference,
        IReadOnlyList<CodeReference> matchingFiles)
    {
        var prompt = new System.Text.StringBuilder();

        prompt.AppendLine($"请在代码库中查找与以下主题相关的示例代码：\n\n## 主题\n{topic}\n");

        // 添加解析的引用信息
        if (resolvedReference.IsResolved)
        {
            prompt.AppendLine("## 解析到的相关文件");
            foreach (var fileMatch in resolvedReference.FileMatches.Take(10))
            {
                prompt.AppendLine($"- {fileMatch.FilePath}");
            }
            prompt.AppendLine();
        }
        else if (matchingFiles.Count > 0)
        {
            prompt.AppendLine("## 匹配的文件");
            foreach (var match in matchingFiles.Take(10))
            {
                prompt.AppendLine($"- {match.ResolvedPath} (匹配类型: {match.MatchType})");
            }
            prompt.AppendLine();
        }

        prompt.AppendLine("请提供：");
        prompt.AppendLine("1. 相关的代码文件路径");
        prompt.AppendLine("2. 关键代码片段");
        prompt.AppendLine("3. 使用场景说明");
        prompt.AppendLine("4. 最佳实践建议");

        return prompt.ToString();
    }

    private static IReadOnlyList<CodeSnippet> ExtractCodeSnippets(
        IReadOnlyList<ExploredFile> exploredFiles,
        string? focusArea)
    {
        var snippets = new List<CodeSnippet>();

        foreach (var file in exploredFiles.Where(f => !string.IsNullOrEmpty(f.Content)))
        {
            var lines = file.Content!.Split('\n');
            var fileType = file.FileType?.ToLowerInvariant() ?? "text";

            // 简单提取：查找包含关键词的行及其上下文
            if (!string.IsNullOrWhiteSpace(focusArea))
            {
                var keywords = focusArea.Split([' ', ',', '，'], StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (keywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        var startLine = Math.Max(0, i - 3);
                        var endLine = Math.Min(lines.Length - 1, i + 3);
                        var snippetLines = lines[startLine..(endLine + 1)];

                        snippets.Add(new CodeSnippet
                        {
                            SourceFile = file.FilePath,
                            Content = string.Join("\n", snippetLines),
                            StartLine = startLine + 1,
                            EndLine = endLine + 1,
                            Language = fileType,
                            Description = $"包含关键词的代码片段",
                            RelevanceScore = file.RelevanceScore
                        });

                        // 限制每个文件的片段数量
                        if (snippets.Count(s => s.SourceFile == file.FilePath) >= 3)
                        {
                            break;
                        }
                    }
                }
            }
        }

        return snippets.OrderByDescending(s => s.RelevanceScore).Take(20).ToList();
    }

    private static string BuildExplorePrompt(ExploreRequest request)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("请探索以下代码库/目录：");
        prompt.AppendLine();
        prompt.AppendLine($"## 目标路径\n{request.TargetPath}");

        if (!string.IsNullOrWhiteSpace(request.FocusArea))
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 关注领域\n{request.FocusArea}");
        }

        if (request.Questions != null && request.Questions.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## 需要回答的问题");
            for (int i = 0; i < request.Questions.Count; i++)
            {
                prompt.AppendLine($"{i + 1}. {request.Questions[i]}");
            }
        }

        if (request.Depth.HasValue)
        {
            prompt.AppendLine();
            prompt.AppendLine($"## 探索深度\n{request.Depth.Value switch
            {
                ExploreDepth.Overview => "概览 - 只关注高层架构",
                ExploreDepth.Standard => "标准 - 平衡深度和广度",
                ExploreDepth.Detailed => "详细 - 深入每个组件",
                _ => "标准"
            }}");
        }

        prompt.AppendLine();
        prompt.AppendLine("请按照系统提示词中指定的格式输出探索结果。");

        return prompt.ToString();
    }

    protected override float GetTemperature() => 0.6f;
}

/// <summary>
/// 探索请求
/// </summary>
public sealed record ExploreRequest
{
    public required string TargetPath { get; init; }
    public string? FocusArea { get; init; }
    public List<string>? Questions { get; init; }
    public ExploreDepth? Depth { get; init; }
}

/// <summary>
/// 探索深度
/// </summary>
public enum ExploreDepth
{
    [EnumValue("overview")] Overview,
    [EnumValue("standard")] Standard,
    [EnumValue("detailed")] Detailed
}

/// <summary>
/// 探索结果
/// </summary>
public sealed record ExploreResult
{
    public required bool Success { get; init; }
    public string? ExploreId { get; init; }
    public string? Content { get; init; }
    public long ExecutionTimeMs { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public string? ErrorMessage { get; init; }
}
