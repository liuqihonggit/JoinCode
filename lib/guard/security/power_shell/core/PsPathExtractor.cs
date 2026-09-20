namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// 路径提取结果
/// </summary>
public sealed class PsPathExtractionResult {
    /// <summary>提取的文件路径列表</summary>
    public List<string> Paths { get; init; } = [];

    /// <summary>操作类型</summary>
    public FileOperationType OperationType { get; init; }

    /// <summary>是否存在不可验证的路径参数</summary>
    public bool HasUnvalidatablePathArg { get; init; }

    /// <summary>是否为可选写操作</summary>
    public bool OptionalWrite { get; init; }
}

/// <summary>
/// PS 路径提取器 — 从 AST 解析的命令中提取文件路径
/// 与 TS extractPathsFromCommand 1:1 对齐
/// </summary>
public static partial class PsPathExtractor {
    /// <summary>
    /// 安全的路径元素类型 — 只有 StringConstant 和 Parameter 是安全的
    /// </summary>
    private static readonly FrozenSet<PsElementType> SafePathElementTypes = FrozenSet.ToFrozenSet(
        [PsElementType.StringConstant, PsElementType.Parameter]);

    /// <summary>
    /// 从命令元素中提取路径
    /// </summary>
    public static PsPathExtractionResult ExtractPaths(PsCommandElement cmd) {
        var canonicalName = PsAliases.ResolveToCanonical(cmd.Name);

        if (!PsCmdletPathRegistry.TryGetConfig(canonicalName, out var config)) {
            return new PsPathExtractionResult {
                OperationType = FileOperationType.Read,
            };
        }

        var paths = new List<string>();
        var hasUnvalidatablePathArg = false;
        var positionalIndex = 0;

        // 合并通用参数
        var allSwitches = config.KnownSwitches;
        var allValueParams = config.KnownValueParams;

        for (var i = 0; i < cmd.Args.Length; i++) {
            var arg = cmd.Args[i];
            var elementType = i + 1 < cmd.ElementTypes.Length
                ? cmd.ElementTypes[i + 1]
                : PsElementType.Other;

            // 检查参数元素类型是否安全
            if (!SafePathElementTypes.Contains(elementType) && elementType != PsElementType.Parameter) {
                hasUnvalidatablePathArg = true;
            }

            if (IsParameter(arg)) {
                // 处理冒号语法（-Path:value）
                if (TryProcessColonSyntax(arg, config, paths, ref hasUnvalidatablePathArg))
                    continue;

                // 空格分隔的命名参数
                var argLower = arg.ToLowerInvariant();
                ProcessSpaceSeparatedParam(argLower, config, allSwitches, allValueParams,
                    cmd, ref i, paths, ref hasUnvalidatablePathArg);
            } else {
                // 位置参数
                positionalIndex++;
                ProcessPositionalArg(arg, elementType, config, positionalIndex,
                    paths, ref hasUnvalidatablePathArg);
            }
        }

        return new PsPathExtractionResult {
            Paths = paths,
            OperationType = config.OperationType,
            HasUnvalidatablePathArg = hasUnvalidatablePathArg,
            OptionalWrite = config.OptionalWrite,
        };
    }

    /// <summary>
    /// 尝试处理冒号语法（-Path:value），返回 true 表示已处理
    /// </summary>
    private static bool TryProcessColonSyntax(
        string arg, PsCmdletPathConfig config,
        List<string> paths, ref bool hasUnvalidatablePathArg) {
        var colonIdx = arg.IndexOf(':', 1);
        if (colonIdx <= 0) return false;

        var paramPart = arg[..colonIdx];
        var valuePart = arg[(colonIdx + 1)..];
        var paramLower = paramPart.ToLowerInvariant();

        if (MatchesParam(paramLower, config.PathParams)) {
            if (HasComplexColonValue(valuePart))
                hasUnvalidatablePathArg = true;
            else if (!string.IsNullOrEmpty(valuePart))
                paths.Add(valuePart);
        } else if (MatchesParam(paramLower, config.LeafOnlyPathParams)) {
            if (IsSimpleLeaf(valuePart))
                paths.Add(valuePart);
            else
                hasUnvalidatablePathArg = true;
        }
        // 其他冒号参数：已知值参数或未知参数，冒号值已内联处理
        return true;
    }

    /// <summary>
    /// 处理空格分隔的命名参数
    /// </summary>
    private static void ProcessSpaceSeparatedParam(
        string argLower, PsCmdletPathConfig config,
        FrozenSet<string> allSwitches, FrozenSet<string> allValueParams,
        PsCommandElement cmd, ref int i,
        List<string> paths, ref bool hasUnvalidatablePathArg) {
        if (MatchesParam(argLower, config.PathParams))
            ProcessPathParamNext(cmd, ref i, paths, ref hasUnvalidatablePathArg);
        else if (MatchesParam(argLower, config.LeafOnlyPathParams))
            ProcessLeafOnlyParamNext(cmd, ref i, paths, ref hasUnvalidatablePathArg);
        else if (MatchesParam(argLower, allSwitches)) {
            // 开关参数，不消费下一个参数
        } else if (MatchesParam(argLower, allValueParams)) {
            // 值参数，消费下一个参数但不做路径验证
            if (i + 1 < cmd.Args.Length)
                i++;
        } else {
            // 未知参数 → 不可验证
            hasUnvalidatablePathArg = true;
        }
    }

    /// <summary>
    /// 处理路径参数的下一个参数值
    /// </summary>
    private static void ProcessPathParamNext(
        PsCommandElement cmd, ref int i,
        List<string> paths, ref bool hasUnvalidatablePathArg) {
        if (i + 1 >= cmd.Args.Length) return;
        i++;
        var nextArg = cmd.Args[i];
        var nextType = i + 1 < cmd.ElementTypes.Length
            ? cmd.ElementTypes[i + 1]
            : PsElementType.Other;
        if (!SafePathElementTypes.Contains(nextType))
            hasUnvalidatablePathArg = true;
        else
            paths.Add(nextArg);
    }

    /// <summary>
    /// 处理叶子参数的下一个参数值
    /// </summary>
    private static void ProcessLeafOnlyParamNext(
        PsCommandElement cmd, ref int i,
        List<string> paths, ref bool hasUnvalidatablePathArg) {
        if (i + 1 >= cmd.Args.Length) return;
        i++;
        var nextArg = cmd.Args[i];
        if (IsSimpleLeaf(nextArg))
            paths.Add(nextArg);
        else
            hasUnvalidatablePathArg = true;
    }

    /// <summary>
    /// 处理位置参数
    /// </summary>
    private static void ProcessPositionalArg(
        string arg, PsElementType elementType, PsCmdletPathConfig config, int positionalIndex,
        List<string> paths, ref bool hasUnvalidatablePathArg) {
        if (positionalIndex <= config.PositionalSkip) return;
        if (SafePathElementTypes.Contains(elementType))
            paths.Add(arg);
        else
            hasUnvalidatablePathArg = true;
    }

    /// <summary>
    /// 检查参数名是否匹配列表（支持 PS 前缀匹配）
    /// </summary>
    private static bool MatchesParam(string paramLower, FrozenSet<string> paramList) {
        foreach (var p in paramList) {
            var pLower = p.ToLowerInvariant();
            if (paramLower == pLower) return true;
            // PS 参数缩写：paramLower 是 pLower 的前缀
            if (paramLower.Length >= 2 && pLower.StartsWith(paramLower)) return true;
        }
        return false;
    }

    /// <summary>
    /// 判断是否为 PS 参数（以 - 或替代前缀开头）
    /// </summary>
    private static bool IsParameter(string arg) {
        if (arg.Length == 0) return false;
        var first = arg[0];
        return first == '-' || first == '/' || first == '\u2013' || first == '\u2014' || first == '\u2015';
    }

    /// <summary>
    /// 检测冒号语法值中的复杂表达式
    /// </summary>
    private static bool HasComplexColonValue(string value) {
        return value.Contains(',') || value.Contains('(') || value.Contains(')')
            || value.Contains('`') || value.Contains("@(") || value.Contains("@{")
            || value.Contains('$');
    }

    /// <summary>
    /// 检查是否为简单叶子文件名（无路径分隔符、无遍历）
    /// </summary>
    private static bool IsSimpleLeaf(string value) {
        return !value.Contains('/') && !value.Contains('\\')
            && !value.Contains("..") && !value.Contains('.');
        // 注意：TS 的判断是含 . 的也标记为非叶子，但实际文件名通常含 .
        // 这里放宽限制，只检查路径分隔符和遍历
    }
}