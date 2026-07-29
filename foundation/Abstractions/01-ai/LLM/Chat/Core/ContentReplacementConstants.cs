namespace JoinCode.Abstractions.LLM.Chat;

public static class ContentReplacementConstants
{
    public const int DefaultMaxResultSizeChars = 50000;
    public const int MaxToolResultsPerMessageChars = 200000;
    public const int PreviewSizeChars = 2000;
    public const string PersistedOutputOpen = "<persisted-output>";
    public const string PersistedOutputClose = "</persisted-output>";
    public const string NoOutputTemplate = "({0} completed with no output)";

    /// <summary>
    /// 对齐 TS TOOL_RESULT_CLEARED_MESSAGE — 工具结果内容被清除但未持久化时的替换消息
    /// 场景: 上下文压缩(compact)清除旧工具结果时使用
    /// </summary>
    public const string ToolResultClearedMessage = "[Old tool result content cleared]";

    /// <summary>
    /// 截断标记前缀 — ToolResultTruncator 产出的截断消息以此开头
    /// ContentReplacementService.IsContentAlreadyCompacted 引用此常量检测已截断内容
    /// </summary>
    public const string TruncatedPrefix = "[Result truncated:";

    /// <summary>
    /// 工具级 maxResultSizeChars 映射 — 对齐 TS 各工具的 maxResultSizeChars 声明
    /// Infinity(-1) 表示永不持久化（如 Read，防止 Read→file→Read 循环）
    /// 使用枚举常量，避免硬编码字符串
    /// FrozenDictionary: 初始化后不可变，NativeAOT 友好，无锁读取
    /// </summary>
    private static readonly FrozenDictionary<string, int> ToolMaxResultSizeChars = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [FileToolNameConstants.FileRead] = -1,           // TS: Infinity — 永不持久化
        [ShellToolNameConstants.Bash] = 30000,   // TS: 30_000
        [SearchToolNameConstants.Grep] = 20000,          // TS: 20_000
        [SearchToolNameConstants.Glob] = 100000,         // TS: 100_000
        [FileToolNameConstants.FileWrite] = 100000,      // TS: 100_000
        [FileToolNameConstants.FileEdit] = 100000,       // TS: 100_000 (FileEditTool)
        [WebToolNameConstants.WebFetch] = 100000,        // TS: 100_000
        [WebToolNameConstants.WebSearch] = 100000,       // TS: 100_000
    }.ToFrozenDictionary();

    /// <summary>
    /// 获取工具的持久化阈值 — 对齐 TS getPersistenceThreshold
    /// 逻辑: Infinity(-1) → 返回 -1(永不持久化); 否则 Math.min(声明值, DefaultMaxResultSizeChars)
    /// </summary>
    public static int GetPersistenceThreshold(string toolName)
    {
        if (!ToolMaxResultSizeChars.TryGetValue(toolName, out var declared))
            return DefaultMaxResultSizeChars; // 未声明的工具使用默认值

        if (declared < 0)
            return declared; // Infinity → 永不持久化

        return Math.Min(declared, DefaultMaxResultSizeChars);
    }

    /// <summary>
    /// 判断工具是否永不持久化 — 对齐 TS Number.isFinite(maxResultSizeChars) 检查
    /// </summary>
    public static bool IsNeverPersistTool(string toolName)
    {
        return ToolMaxResultSizeChars.TryGetValue(toolName, out var declared) && declared < 0;
    }

    /// <summary>
    /// 获取所有永不持久化的工具名 — 对齐 TS query.ts 过滤 Infinity 工具
    /// </summary>
    public static IEnumerable<string> GetNeverPersistToolNames()
    {
        foreach (var kvp in ToolMaxResultSizeChars)
        {
            if (kvp.Value < 0)
                yield return kvp.Key;
        }
    }

    /// <summary>
    /// 统一构建 persisted-output 消息 — 对齐 TS buildLargeToolResultMessage
    /// 格式: &lt;persisted-output&gt;\nOutput too large (XX.XKB). Full output saved to: path\n\nPreview (first XX.XKB):\n...content...\n&lt;/persisted-output&gt;
    /// </summary>
    public static string BuildPersistedOutputMessage(PersistedToolResult result)
    {
        var sb = new System.Text.StringBuilder(256 + result.Preview.Length);
        sb.Append(PersistedOutputOpen);
        sb.Append('\n');
        sb.Append("Output too large (");
        sb.Append(FormatCharCount(result.OriginalSize));
        sb.Append("). Full output saved to: ");
        sb.Append(result.Filepath);
        sb.Append("\n\nPreview (first ");
        sb.Append(FormatCharCount(PreviewSizeChars));
        sb.Append("):\n");
        sb.Append(result.Preview);
        sb.Append(result.HasMore ? "\n...\n" : "\n");
        sb.Append(PersistedOutputClose);
        return sb.ToString();
    }

    /// <summary>
    /// 格式化字符数/字节数 — 对齐 TS formatFileSize
    /// 统一实现，消除各子系统重复代码
    /// 使用手动整数格式化，避免浮点格式化的文化敏感问题（InvariantGlobalization兼容）
    /// </summary>
    public static string FormatCharCount(long charCount)
    {
        if (charCount < 1024)
            return $"{charCount} bytes";
        if (charCount < 1024 * 1024)
        {
            var kbWhole = charCount / 1024;
            var kbFrac = (charCount % 1024) * 10 / 1024; // 一位小数
            return kbFrac == 0 ? $"{kbWhole}KB" : $"{kbWhole}.{kbFrac}KB";
        }
        if (charCount < 1024L * 1024 * 1024)
        {
            var mbWhole = charCount / (1024 * 1024);
            var mbFrac = (charCount % (1024 * 1024)) * 10 / (1024 * 1024);
            return mbFrac == 0 ? $"{mbWhole}MB" : $"{mbWhole}.{mbFrac}MB";
        }
        var gbWhole = charCount / (1024L * 1024 * 1024);
        var gbFrac = (charCount % (1024L * 1024 * 1024)) * 10 / (1024L * 1024 * 1024);
        return gbFrac == 0 ? $"{gbWhole}GB" : $"{gbWhole}.{gbFrac}GB";
    }

    /// <summary>
    /// 保留旧方法名兼容 — 内部委托 FormatCharCount
    /// </summary>
    public static string FormatFileSize(long sizeInBytes) => FormatCharCount(sizeInBytes);
}
