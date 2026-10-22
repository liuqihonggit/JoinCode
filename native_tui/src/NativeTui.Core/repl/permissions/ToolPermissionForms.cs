namespace NativeTui.Core.Repl;

/// <summary>
/// 权限对话框形态派生器（S8）— 按工具名分发形态数据的唯一入口（对齐基准 PermissionRequest.tsx:47-82
/// permissionComponentForTool 十三种分发收敛三档）：
/// shell 类（Bash/PowerShell/Shell）+ 描述含可提取命令段 → Bash 形态（命令全文折行 + 危险警告）；
/// 文件类（Read/Write/Edit 及 File 前缀别名）+ 描述含可提取路径 → File 形态（路径 + patch 摘要）；
/// 其余或提取失败 → Fallback 现形态（既有渲染零行为变化）。
/// 命令/路径从引擎确认提示文本启发式提取（引号段/冒号段 — bridge 通道仅工具名+提示两字符串，
/// 结构化 tool input 未透传引擎接口，见未决问题）；危险检测跑提示全文（宁误报不漏报 — 安全纵深）。
/// </summary>
public static class ToolPermissionForms
{
    /// <summary>命令区渲染缩进（3 空格 — 工具行同款；折行宽 = width - 缩进）。</summary>
    public const int CommandIndent = 3;

    /// <summary>
    /// 派生形态数据 — 工具名分发 + 描述文本提取（纯函数，每次调用重新派生：
    /// Request 生命周期内不变，渲染/测高双调用一致性由同 width 保证）。
    /// </summary>
    /// <param name="toolName">工具名（OrdinalIgnoreCase 匹配分发集合）。</param>
    /// <param name="description">引擎确认提示文本（命令/路径/patch 统计的信息载体）。</param>
    /// <param name="width">对话框内容宽（命令区折行依据）。</param>
    /// <returns>形态数据（提取失败降级 Fallback 常量形态）。</returns>
    public static ToolPermissionForm Derive(string toolName, string description, int width) =>
        IsTool(toolName, "bash", "powershell", "shell") ? DeriveBash(toolName, description, width)
        : IsTool(toolName, "read", "write", "edit", "fileread", "filewrite", "fileedit") ? DeriveFile(toolName, description)
        : ToolPermissionForm.Fallback;

    /// <summary>Bash 形态派生 — 命令段提取失败降级 Fallback；危险检测跑提示全文。</summary>
    private static ToolPermissionForm DeriveBash(string toolName, string description, int width) =>
        ExtractCommand(description) is { } command
            ? new ToolPermissionForm
            {
                Kind = PermissionFormKind.Bash,
                Title = TitleForShell(toolName),
                CommandLines = PermissionTextWrap.Wrap(command, Math.Max(1, width - CommandIndent)),
                DangerWarning = DangerousCommandCatalog.MatchWarning(description),
            }
            : ToolPermissionForm.Fallback;

    /// <summary>File 形态派生 — 路径提取失败降级 Fallback；patch 统计尽力解析（无统计为 0 隐藏）。</summary>
    private static ToolPermissionForm DeriveFile(string toolName, string description)
    {
        if (ExtractPath(description) is not { } path)
        {
            return ToolPermissionForm.Fallback;
        }

        var patch = PatchSummary(description);
        return new ToolPermissionForm
        {
            Kind = PermissionFormKind.File,
            Title = TitleForFile(toolName),
            CommandLines = [],
            FilePath = path,
            PatchAdded = patch?.Added ?? 0,
            PatchRemoved = patch?.Removed ?? 0,
        };
    }

    /// <summary>
    /// 提取命令文本 — 候选段 = 全部引号段（'..'/".." 精确载体）+ 首个冒号后段（':'/'：'，
    /// 引擎句式 "需要确认: xxx"）；首个满足命令特征（≥2 词或含 shell 元字符/路径字符）的候选即命令。
    /// 中文提示句（无空格单 token）天然排除 — 无命令载体返回 null。
    /// </summary>
    /// <param name="description">引擎确认提示文本。</param>
    /// <returns>命令文本，无可识别载体为 null。</returns>
    internal static string? ExtractCommand(string description) =>
        QuotedSegments(description).Concat(ColonSegment(description)).FirstOrDefault(IsCommandLike);

    /// <summary>
    /// 提取文件路径 — 候选 = 全部引号段 + 分词 token；首个满足路径特征
    /// （含 / 或 \、~ 前缀、非纯点号的点分扩展名）的候选即路径。无路径特征返回 null。
    /// </summary>
    /// <param name="description">引擎确认提示文本。</param>
    /// <returns>文件路径，无可识别路径为 null。</returns>
    internal static string? ExtractPath(string description) =>
        QuotedSegments(description).Concat(description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(IsPathLike);

    /// <summary>patch 摘要解析 — '+'/'-' 后紧跟数字的首个命中对（"+12 -3" / "(+12/-3)"）；无统计返回 null。</summary>
    /// <param name="text">提示文本。</param>
    /// <returns>(新增行数, 删除行数)，无统计为 null。</returns>
    internal static (int Added, int Removed)? PatchSummary(string text)
    {
        var added = ScanNumberAfter(text, '+');
        var removed = ScanNumberAfter(text, '-');
        return added is null && removed is null ? null : (added ?? 0, removed ?? 0);
    }

    /// <summary>引号段提取 — '单引号' 与 "双引号" 各自配对扫描（未闭合忽略）。</summary>
    private static IEnumerable<string> QuotedSegments(string description)
    {
        var singles = SegmentsBetween(description, '\'');
        var doubles = SegmentsBetween(description, '"');
        return singles.Concat(doubles);
    }

    /// <summary>指定引号字符的配对段扫描 — 遇开引号记录到下一个同种闭引号（空段跳过）。</summary>
    private static IEnumerable<string> SegmentsBetween(string text, char quote)
    {
        for (var open = text.IndexOf(quote); open >= 0; open = text.IndexOf(quote, open + 1))
        {
            var close = text.IndexOf(quote, open + 1);
            if (close < 0)
            {
                yield break;
            }

            var segment = text[(open + 1)..close].Trim();
            if (segment.Length > 0)
            {
                yield return segment;
            }

            open = close;
        }
    }

    /// <summary>首个冒号后段 — 英文 ':' / 中文 '：' 取最先出现者，其后文本 trim。</summary>
    private static string? ExtractAfterColon(string description)
    {
        var index = IndexOfEither(description, ':', '：');
        return index < 0 || index + 1 >= description.Length ? null : description[(index + 1)..].Trim();
    }

    /// <summary>冒号段单元素序列（无冒号段为空序列 — null 安全拼接）。</summary>
    private static IEnumerable<string> ColonSegment(string description) =>
        ExtractAfterColon(description) is { } segment ? [segment] : [];

    /// <summary>两字符中最先出现位置（均未出现返回 -1）。</summary>
    private static int IndexOfEither(string text, char first, char second)
    {
        var i1 = text.IndexOf(first);
        var i2 = text.IndexOf(second);
        return i1 < 0 ? i2 : i2 < 0 ? i1 : Math.Min(i1, i2);
    }

    /// <summary>命令特征 — 空格分词多词或含 shell 元字符（&amp; | ; &gt; &lt; $）/路径字符（/ \）/连字符
    ///（PowerShell cmdlet 词形 Get-ChildItem）。单词纯字母段（工具名/标签）与中文单 token 句子天然排除。</summary>
    private static bool IsCommandLike(string candidate) =>
        candidate.Contains(' ') || ContainsAny(candidate, '&', '|', ';', '<', '>', '$', '/', '\\', '-');

    /// <summary>路径特征 — 含 / 或 \、~ 前缀、或点分扩展名（非纯点号 token）。</summary>
    private static bool IsPathLike(string candidate) =>
        candidate.Contains('/') || candidate.Contains('\\') || candidate.StartsWith('~')
        || (candidate.Contains('.') && candidate.Trim('.').Length > 0);

    /// <summary>标记符后紧跟的数字段解析（'+12'/'-3' — 标记后首字符非数字不命中）。</summary>
    private static int? ScanNumberAfter(string text, char marker)
    {
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != marker || !char.IsDigit(text[i + 1]))
            {
                continue;
            }

            var end = i + 1;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            return int.Parse(text[(i + 1)..end]);
        }

        return null;
    }

    /// <summary>工具名集合匹配（OrdinalIgnoreCase）。</summary>
    private static bool IsTool(string toolName, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (string.Equals(toolName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>shell 类工具标题（基准 BashPermissionRequest.tsx:435 title="Bash command"）。</summary>
    private static string TitleForShell(string toolName) =>
        IsTool(toolName, "powershell") ? "PowerShell command"
        : IsTool(toolName, "shell") ? "Shell command"
        : "Bash command";

    /// <summary>文件类工具标题（基准 FilesystemPermissionRequest — isReadOnly ? "Read file" : "Edit file"；写侧扩展 Write）。</summary>
    private static string TitleForFile(string toolName) =>
        IsTool(toolName, "write", "filewrite") ? "Write file"
        : IsTool(toolName, "edit", "fileedit") ? "Edit file"
        : "Read file";

    /// <summary>任一指定字符包含检查。</summary>
    private static bool ContainsAny(string text, params ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            if (text.Contains(c))
            {
                return true;
            }
        }

        return false;
    }
}
