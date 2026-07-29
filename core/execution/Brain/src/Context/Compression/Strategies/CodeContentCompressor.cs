
namespace Core.Context.Compression;

/// <summary>
/// 代码内容压缩策略
/// </summary>
[Register(JoinCode.Abstractions.Attributes.ServiceLifetime.Transient)]
public sealed partial class CodeContentCompressor : CompressionStrategyBase
{
    public override string Name => "CodeContentCompressor";
    public override string Description => "Compresses code content by removing method bodies while preserving signatures and key structures";
    public override int Priority => 100;

    private static readonly FrozenSet<ContentType> _supportedTypes = FrozenSet.Create(ContentType.Code);

    public override IReadOnlySet<ContentType> SupportedContentTypes => _supportedTypes;

    public override Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(content);
        }

        var result = new StringBuilder();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.None);
        var inMethodBody = false;
        var braceDepth = 0;
        var methodBodyStartLine = -1;



        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[i];
            var trimmedLine = line.Trim();

            if (ShouldSkipLine(trimmedLine, options))
            {
                continue;
            }

            if (IsImportStatement(trimmedLine))
            {
                if (options.PreserveImports)
                {
                    result.AppendLine(line);
                }
                // 如果不保留导入语句，则跳过
                continue;
            }

            if (IsDocumentationComment(trimmedLine) && options.PreserveDocumentation)
            {
                result.AppendLine(line);
                continue;
            }

            if (IsComment(trimmedLine) && options.PreserveComments)
            {
                if (IsKeyComment(trimmedLine))
                {
                    result.AppendLine(line);
                }
                continue;
            }

            if (IsTypeDefinition(trimmedLine) && options.PreserveTypeDefinitions)
            {
                result.AppendLine(line);
                inMethodBody = false;
                continue;
            }

            if (IsEnumDefinition(trimmedLine) && options.PreserveEnums)
            {
                result.AppendLine(line);
                inMethodBody = false;
                continue;
            }

            if (IsConstantDefinition(trimmedLine) && options.PreserveConstants)
            {
                result.AppendLine(line);
                continue;
            }

            if (IsMethodOrPropertySignature(trimmedLine))
            {
                if (options.PreserveSignatures)
                {
                    result.AppendLine(line);
                }

                if (IsExpressionBodiedMember(trimmedLine))
                {
                    continue;
                }

                inMethodBody = true;
                methodBodyStartLine = i;
                braceDepth = CountBraces(line);
                continue;
            }

            if (inMethodBody)
            {
                braceDepth += CountBraces(line);

                if (braceDepth <= 0)
                {
                    inMethodBody = false;

                    if (options.MaxMethodBodyLines > 0 &&
                        i - methodBodyStartLine <= options.MaxMethodBodyLines)
                    {
                        for (var j = methodBodyStartLine + 1; j <= i; j++)
                        {
                            result.AppendLine(lines[j]);
                        }
                    }
                    else
                    {
                        result.AppendLine("    // ... method body omitted ...");
                    }
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                result.AppendLine(line);
            }
        }

        var compressed = result.ToString().TrimEnd();
        return Task.FromResult(compressed);
    }

    public override double EstimateCompressionRatio(string content, CompressionOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 1.0;

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var totalLines = lines.Length;
        var methodBodyLines = 0;
        var inMethodBody = false;
        var braceDepth = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (IsMethodOrPropertySignature(trimmedLine) && !IsExpressionBodiedMember(trimmedLine))
            {
                inMethodBody = true;
                braceDepth = CountBraces(line);
                continue;
            }

            if (inMethodBody)
            {
                methodBodyLines++;
                braceDepth += CountBraces(line);

                if (braceDepth <= 0)
                {
                    inMethodBody = false;
                }
            }
        }

        var estimatedRatio = totalLines > 0
            ? (double)(totalLines - methodBodyLines + methodBodyLines * 0.1) / totalLines
            : 1.0;

        return Math.Max(estimatedRatio, options.TargetCompressionRatio);
    }

    private static bool ShouldSkipLine(string trimmedLine, CompressionOptions options)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine))
            return true;

        if (trimmedLine.StartsWith("#region", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("#endregion", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsImportStatement(string line)
    {
        return line.StartsWith("using ", StringComparison.Ordinal) ||
               line.StartsWith("import ", StringComparison.Ordinal) ||
               line.StartsWith("require", StringComparison.Ordinal) ||
               line.StartsWith("from ", StringComparison.Ordinal) ||
               line.StartsWith("#include", StringComparison.Ordinal);
    }

    private static bool IsDocumentationComment(string line)
    {
        return line.StartsWith("///") ||
               line.StartsWith("/**") ||
               line.StartsWith("* ") ||
               line.StartsWith("'''", StringComparison.Ordinal) ||
               line.StartsWith("\"\"\"", StringComparison.Ordinal);
    }

    private static bool IsComment(string line)
    {
        return line.StartsWith("//") ||
               line.StartsWith("#") ||
               line.StartsWith("/*") ||
               line.StartsWith("*") ||
               line.StartsWith("'", StringComparison.Ordinal);
    }

    // 使用 FrozenSet 存储关键字，O(1) 查找性能
    private static readonly FrozenSet<string> KeyIndicators = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "TODO", "FIXME", "HACK", "NOTE", "IMPORTANT",
        "WARNING", "CRITICAL", "KEY", "SUMMARY");

    private static bool IsKeyComment(string line)
    {
        // 使用 Span 避免字符串分配，逐个检查关键字
        var lineSpan = line.AsSpan();

        foreach (var indicator in KeyIndicators)
        {
            if (lineSpan.Contains(indicator.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeDefinition(string line)
    {
        var typePatterns = new[]
        {
            @"^\s*(public|private|protected|internal|static|abstract|sealed|partial)?\s*(class|interface|struct|record)\s+\w+",
            @"^\s*(export\s+)?(class|interface|type)\s+\w+",
            @"^\s*@?interface\s+\w+"
        };

        return typePatterns.Any(pattern =>
            Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase));
    }

    private static bool IsEnumDefinition(string line)
    {
        return Regex.IsMatch(line, @"^\s*(public|private|protected|internal)?\s*enum\s+\w+",
            RegexOptions.IgnoreCase);
    }

    private static bool IsConstantDefinition(string line)
    {
        return Regex.IsMatch(line, @"^\s*(public|private|protected|internal|const|readonly|static\s+readonly|final)\s+\w+",
            RegexOptions.IgnoreCase) ||
               line.Contains("=", StringComparison.Ordinal) &&
               (line.Contains("const", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("readonly", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMethodOrPropertySignature(string line)
    {
        var methodPatterns = new[]
        {
            // C# style: public void Method() {
            @"^\s*(public|private|protected|internal|static|virtual|abstract|override|async)?\s*\w+\s+\w+\s*\([^)]*\)\s*\{",
            // C# style: public void Method()
            @"^\s*(public|private|protected|internal|static|virtual|abstract|override|async)?\s*\w+\s+\w+\s*\([^)]*\)\s*$",
            // C# property: public string Name {
            @"^\s*(public|private|protected|internal|static|virtual|abstract|override)?\s*\w+\s+\w+\s*\{",
            // JavaScript/Python style
            @"^\s*(function|def|async\s+def)\s+\w+\s*\(",
            // Simple method detection
            @"^\s*\w+\s+\w+\s*\([^)]*\)\s*\{",
            @"^\s*\w+\s+\w+\s*\([^)]*\)\s*$",
            // Getter/Setter
            @"^\s*(get|set|async)\s+\w+\s*\("
        };

        return methodPatterns.Any(pattern =>
            Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase));
    }

    private static bool IsExpressionBodiedMember(string line)
    {
        return line.Contains("=>", StringComparison.Ordinal) ||
               (line.Contains("{", StringComparison.Ordinal) &&
                line.Contains("}", StringComparison.Ordinal));
    }

    private static int CountBraces(string line)
    {
        var count = 0;
        foreach (var c in line)
        {
            if (c == '{') count++;
            else if (c == '}') count--;
        }
        return count;
    }
}
