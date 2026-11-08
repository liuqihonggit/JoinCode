
namespace Core.Context.Compression;

/// <summary>代码行分类 — 用于 CompressAsync 行处理决策（TASK031 阶段2.1 提取）</summary>
internal enum CodeLineKind {
    /// <summary>跳过：空行或 #region/#endregion</summary>
    Skip,
    /// <summary>导入语句：using/import/require/from/#include</summary>
    Import,
    /// <summary>文档注释：/// /** * '''</summary>
    DocComment,
    /// <summary>普通注释：// # /* * '</summary>
    Comment,
    /// <summary>类型定义：class/interface/struct/record</summary>
    TypeDefinition,
    /// <summary>枚举定义：enum</summary>
    EnumDefinition,
    /// <summary>常量定义：const/readonly</summary>
    ConstantDefinition,
    /// <summary>方法或属性签名</summary>
    MethodSignature,
    /// <summary>普通行（非方法体内）</summary>
    NormalLine,
    /// <summary>方法体行</summary>
    MethodBodyLine
}

/// <summary>
/// 代码内容压缩策略
/// </summary>
[Register(typeof(ICompressionStrategy), ServiceLifetime.Transient)]
public sealed partial class CodeContentCompressor : CompressionStrategyBase {
    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "CodeContentCompressor";
    /// <summary>
    /// 策略描述
    /// </summary>
    public override string Description => "Compresses code content by removing method bodies while preserving signatures and key structures";
    /// <summary>
    /// 策略优先级
    /// </summary>
    public override int Priority => 100;

    private static readonly FrozenSet<ContentType> _supportedTypes = FrozenSet.Create(ContentType.Code);

    private static readonly Regex[] TypePatternRegexes =
    [
        new(@"^\s*(public|private|protected|internal|static|abstract|sealed|partial)?\s*(class|interface|struct|record)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(export\s+)?(class|interface|type)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*@?interface\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex EnumDefinitionRegex =
        new(@"^\s*(public|private|protected|internal)?\s*enum\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConstantDefinitionRegex =
        new(@"^\s*(public|private|protected|internal|const|readonly|static\s+readonly|final)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex[] MethodPatternRegexes =
    [
        new(@"^\s*(public|private|protected|internal|static|virtual|abstract|override|async)?\s*\w+\s+\w+\s*\([^)]*\)\s*\{", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(public|private|protected|internal|static|virtual|abstract|override|async)?\s*\w+\s+\w+\s*\([^)]*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(public|private|protected|internal|static|virtual|abstract|override)?\s*\w+\s+\w+\s*\{", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(function|def|async\s+def)\s+\w+\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*\w+\s+\w+\s*\([^)]*\)\s*\{", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*\w+\s+\w+\s*\([^)]*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(get|set|async)\s+\w+\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>
    /// 支持的内容类型
    /// </summary>
    public override IReadOnlySet<ContentType> SupportedContentTypes => _supportedTypes;

    /// <summary>
    /// 压缩代码内容：移除方法体同时保留签名与关键结构
    /// </summary>
    /// <param name="content">原始代码内容</param>
    /// <param name="options">压缩选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩后的代码内容</returns>
    public override Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default) {
        ValidateOptions(options);

        if (string.IsNullOrWhiteSpace(content)) {
            return Task.FromResult(content);
        }

        var result = new StringBuilder();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.None);
        var inMethodBody = false;
        var braceDepth = 0;
        var methodBodyStartLine = -1;



        for (var i = 0; i < lines.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[i];
            var trimmedLine = line.Trim();
            var kind = ClassifyCodeLine(trimmedLine, options, inMethodBody);

            switch (kind) {
                case CodeLineKind.Skip:
                    continue;

                case CodeLineKind.Import:
                    if (options.PreserveImports) {
                        result.AppendLine(line);
                    }
                    continue;

                case CodeLineKind.DocComment:
                    result.AppendLine(line);
                    continue;

                case CodeLineKind.Comment:
                    if (IsKeyComment(trimmedLine)) {
                        result.AppendLine(line);
                    }
                    continue;

                case CodeLineKind.TypeDefinition:
                    result.AppendLine(line);
                    inMethodBody = false;
                    continue;

                case CodeLineKind.EnumDefinition:
                    result.AppendLine(line);
                    inMethodBody = false;
                    continue;

                case CodeLineKind.ConstantDefinition:
                    result.AppendLine(line);
                    continue;

                case CodeLineKind.MethodSignature:
                    if (options.PreserveSignatures) {
                        result.AppendLine(line);
                    }
                    if (IsExpressionBodiedMember(trimmedLine)) {
                        continue;
                    }
                    inMethodBody = true;
                    methodBodyStartLine = i;
                    braceDepth = CountBraces(line);
                    continue;

                case CodeLineKind.NormalLine:
                    if (!string.IsNullOrWhiteSpace(line)) {
                        result.AppendLine(line);
                    }
                    continue;

                case CodeLineKind.MethodBodyLine:
                    braceDepth += CountBraces(line);
                    if (braceDepth <= 0) {
                        inMethodBody = false;
                        result.Append(BuildMethodBodyContent(methodBodyStartLine, i, options.MaxMethodBodyLines, lines));
                    }
                    continue;

                default:
                    continue;
            }
        }

        var compressed = result.ToString().TrimEnd();
        return Task.FromResult(compressed);
    }

    /// <summary>
    /// 预估代码内容的压缩比率
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="options">压缩选项</param>
    /// <returns>预估压缩比率 (0-1)</returns>
    public override double EstimateCompressionRatio(string content, CompressionOptions options) {
        if (string.IsNullOrWhiteSpace(content))
            return 1.0;

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var totalLines = lines.Length;
        var methodBodyLines = 0;
        var inMethodBody = false;
        var braceDepth = 0;

        foreach (var line in lines) {
            var trimmedLine = line.Trim();

            if (IsMethodOrPropertySignature(trimmedLine) && !IsExpressionBodiedMember(trimmedLine)) {
                inMethodBody = true;
                braceDepth = CountBraces(line);
                continue;
            }

            if (inMethodBody) {
                methodBodyLines++;
                braceDepth += CountBraces(line);

                if (braceDepth <= 0) {
                    inMethodBody = false;
                }
            }
        }

        var estimatedRatio = totalLines > 0
            ? (double)(totalLines - methodBodyLines + methodBodyLines * 0.1) / totalLines
            : 1.0;

        return Math.Max(estimatedRatio, options.TargetCompressionRatio);
    }

    /// <summary>
    /// 对单行代码进行分类 — 纯函数，无副作用，用于 CompressAsync 行处理决策（TASK031 阶段2.1 提取）
    /// </summary>
    /// <param name="trimmedLine">已 Trim 的行内容</param>
    /// <param name="options">压缩选项（影响某些分类的条件判断）</param>
    /// <param name="inMethodBody">当前是否处于方法体内</param>
    /// <returns>该行的分类种类</returns>
    internal static CodeLineKind ClassifyCodeLine(string trimmedLine, CompressionOptions options, bool inMethodBody) {
        if (ShouldSkipLine(trimmedLine, options)) return CodeLineKind.Skip;
        if (IsImportStatement(trimmedLine)) return CodeLineKind.Import;
        if (options.PreserveDocumentation && IsDocumentationComment(trimmedLine)) return CodeLineKind.DocComment;
        if (options.PreserveComments && IsComment(trimmedLine)) return CodeLineKind.Comment;
        if (options.PreserveTypeDefinitions && IsTypeDefinition(trimmedLine)) return CodeLineKind.TypeDefinition;
        if (options.PreserveEnums && IsEnumDefinition(trimmedLine)) return CodeLineKind.EnumDefinition;
        if (options.PreserveConstants && IsConstantDefinition(trimmedLine)) return CodeLineKind.ConstantDefinition;
        if (IsMethodOrPropertySignature(trimmedLine)) return CodeLineKind.MethodSignature;
        return inMethodBody ? CodeLineKind.MethodBodyLine : CodeLineKind.NormalLine;
    }

    /// <summary>
    /// 构建方法体内容 — 保留或省略（TASK031 阶段2.1 提取）
    /// 当方法体行数不超过 maxMethodBodyLines 时保留原文，否则输出省略占位符
    /// </summary>
    /// <param name="methodBodyStartLine">方法体起始行索引</param>
    /// <param name="currentLine">当前行索引（方法体结束行）</param>
    /// <param name="maxMethodBodyLines">最大保留方法体行数（0 表示总是省略）</param>
    /// <param name="lines">全部行数组</param>
    /// <returns>方法体内容文本（含行尾换行符）</returns>
    internal static string BuildMethodBodyContent(int methodBodyStartLine, int currentLine, int maxMethodBodyLines, string[] lines) {
        if (maxMethodBodyLines > 0 && currentLine - methodBodyStartLine <= maxMethodBodyLines) {
            var sb = new StringBuilder();
            for (var j = methodBodyStartLine + 1; j <= currentLine; j++) {
                sb.AppendLine(lines[j]);
            }
            return sb.ToString();
        }
        return "    // ... method body omitted ..." + Environment.NewLine;
    }

    private static bool ShouldSkipLine(string trimmedLine, CompressionOptions options) {
        if (string.IsNullOrWhiteSpace(trimmedLine))
            return true;

        if (trimmedLine.StartsWith("#region", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("#endregion", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    private static bool IsImportStatement(string line) {
        return line.StartsWith("using ", StringComparison.Ordinal) ||
               line.StartsWith("import ", StringComparison.Ordinal) ||
               line.StartsWith("require", StringComparison.Ordinal) ||
               line.StartsWith("from ", StringComparison.Ordinal) ||
               line.StartsWith("#include", StringComparison.Ordinal);
    }

    private static bool IsDocumentationComment(string line) {
        return line.StartsWith("///") ||
               line.StartsWith("/**") ||
               line.StartsWith("* ") ||
               line.StartsWith("'''", StringComparison.Ordinal) ||
               line.StartsWith("\"\"\"", StringComparison.Ordinal);
    }

    private static bool IsComment(string line) {
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

    private static bool IsKeyComment(string line) {
        // 使用 Span 避免字符串分配，逐个检查关键字
        var lineSpan = line.AsSpan();

        foreach (var indicator in KeyIndicators) {
            if (lineSpan.Contains(indicator.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeDefinition(string line) {
        return TypePatternRegexes.Any(regex => regex.IsMatch(line));
    }

    private static bool IsEnumDefinition(string line) {
        return EnumDefinitionRegex.IsMatch(line);
    }

    private static bool IsConstantDefinition(string line) {
        return ConstantDefinitionRegex.IsMatch(line) ||
               line.Contains("=", StringComparison.Ordinal) &&
               (line.Contains("const", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("readonly", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMethodOrPropertySignature(string line) {
        return MethodPatternRegexes.Any(regex => regex.IsMatch(line));
    }

    private static bool IsExpressionBodiedMember(string line) {
        return line.Contains("=>", StringComparison.Ordinal) ||
               (line.Contains("{", StringComparison.Ordinal) &&
                line.Contains("}", StringComparison.Ordinal));
    }

    /// <summary>
    /// 计算行中大括号净增量（{ 计 +1，} 计 -1）— 纯函数（TASK031 阶段2.1 提取为 internal 供测试）
    /// </summary>
    /// <param name="line">代码行</param>
    /// <returns>大括号净数</returns>
    internal static int CountBraces(string line) {
        var count = 0;
        foreach (var c in line) {
            if (c == '{') count++;
            else if (c == '}') count--;
        }
        return count;
    }
}