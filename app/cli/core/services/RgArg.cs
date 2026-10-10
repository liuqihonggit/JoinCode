namespace JoinCode;

/// <summary>
/// rg 子命令参数枚举 — 结构化定义所有 ripgrep 兼容搜索参数
/// <para>由 CliOptionGenerator 自动生成 RgArgParser + RgArgParseResult。</para>
/// <para>注意: rg 的短选项集群(-iSnFo)和位置参数(pattern+paths)由 RgSubCommand.ParseArgs 手写处理,</para>
/// <para>此枚举作为参数名/描述的唯一数据源,用于帮助文本生成和参数校验。</para>
/// </summary>
public enum RgArg {
    /// <summary>搜索模式(正则表达式)</summary>
    [CliOption("pattern", "", "正则表达式(PowerShell 双反斜杠自动修复)", AcceptsValue = true, Category = "位置")]
    Pattern,

    /// <summary>搜索路径</summary>
    [CliOption("path", "", "搜索路径(必填,禁止无路径搜索)", AcceptsValue = true, Category = "位置")]
    Path,

    /// <summary>文件类型过滤</summary>
    [CliOption("type", "-t", "文件类型(cs/js/ts/py/go/rust/java...)", AcceptsValue = true, Category = "过滤")]
    Type,

    /// <summary>glob 过滤</summary>
    [CliOption("glob", "-g", "glob 过滤(! 前缀排除,如 !**/tests/**)", AcceptsValue = true, Category = "过滤")]
    Glob,

    /// <summary>搜索隐藏文件</summary>
    [CliOption("hidden", "", "搜索隐藏文件", Category = "过滤")]
    Hidden,

    /// <summary>禁用 .gitignore</summary>
    [CliOption("no-ignore", "", "禁用 .gitignore", Category = "过滤")]
    NoIgnore,

    /// <summary>忽略大小写</summary>
    [CliOption("ignore-case", "-i", "忽略大小写", Category = "输出")]
    IgnoreCase,

    /// <summary>智能大小写</summary>
    [CliOption("smart-case", "-S", "智能大小写(模式含大写则区分)", Category = "输出")]
    SmartCase,

    /// <summary>词边界匹配</summary>
    [CliOption("word-regexp", "-w", "词边界匹配", Category = "输出")]
    WordRegexp,

    /// <summary>只输出匹配部分</summary>
    [CliOption("only-matching", "-o", "只输出匹配部分", Category = "输出")]
    OnlyMatching,

    /// <summary>反向匹配（输出不匹配的行）</summary>
    [CliOption("invert-match", "-v", "反向匹配(输出不匹配的行)", Category = "输出")]
    InvertMatch,

    /// <summary>整行匹配（pattern 必须匹配整行）</summary>
    [CliOption("line-regexp", "-x", "整行匹配", Category = "输出")]
    LineRegexp,

    /// <summary>多模式交替（可重复，OR 合并）</summary>
    [CliOption("regexp", "-e", "多模式交替(可重复,OR 合并)", AcceptsValue = true, Category = "输出")]
    Regexp,

    /// <summary>每文件最大匹配数（达到后跳过剩余行）</summary>
    [CliOption("max-count", "-m", "每文件最大匹配数", AcceptsValue = true, Category = "输出")]
    MaxCount,

    /// <summary>线程数（默认 CPU 核数）</summary>
    [CliOption("threads", "-j", "线程数(默认 CPU 核数)", AcceptsValue = true, Category = "控制")]
    Threads,

    /// <summary>最大文件大小（超过则跳过，支持 K/M/G 后缀）</summary>
    [CliOption("max-filesize", "", "最大文件大小(超过则跳过,支持 K/M/G 后缀)", AcceptsValue = true, Category = "控制")]
    MaxFilesize,

    /// <summary>替换匹配文本</summary>
    [CliOption("replace", "-r", "替换匹配文本", AcceptsValue = true, Category = "输出")]
    Replace,

    /// <summary>显示行号</summary>
    [CliOption("line-number", "-n", "显示行号(content 模式默认开启)", Category = "输出")]
    LineNumber,

    /// <summary>匹配行后 n 行</summary>
    [CliOption("after-context", "-A", "匹配行后 n 行", AcceptsValue = true, Category = "输出")]
    AfterContext,

    /// <summary>匹配行前 n 行</summary>
    [CliOption("before-context", "-B", "匹配行前 n 行", AcceptsValue = true, Category = "输出")]
    BeforeContext,

    /// <summary>匹配行前后 n 行</summary>
    [CliOption("context", "-C", "匹配行前后 n 行", AcceptsValue = true, Category = "输出")]
    Context,

    /// <summary>多行模式</summary>
    [CliOption("multiline", "-U", "多行模式(. 匹配换行)", Category = "输出")]
    Multiline,

    /// <summary>字面量搜索</summary>
    [CliOption("fixed-strings", "-F", "字面量搜索(非正则)", Category = "输出")]
    FixedStrings,

    /// <summary>输出匹配行</summary>
    [CliOption("content", "", "输出匹配行(默认)", Category = "输出")]
    Content,

    /// <summary>输出匹配计数</summary>
    [CliOption("count", "-c", "输出匹配计数", Category = "输出")]
    Count,

    /// <summary>只输出文件名</summary>
    [CliOption("files-with-matches", "-l", "只输出文件名", Category = "输出")]
    FilesWithMatches,

    /// <summary>限制结果数</summary>
    [CliOption("head-limit", "", "限制结果数(默认 250,0=无限)", AcceptsValue = true, Category = "输出")]
    HeadLimit,

    /// <summary>跳过前 n 条结果</summary>
    [CliOption("offset", "", "跳过前 n 条结果", AcceptsValue = true, Category = "输出")]
    Offset,

    /// <summary>排序</summary>
    [CliOption("sort", "", "排序(path/modified/accessed/created/none)", AcceptsValue = true, Category = "输出")]
    Sort,

    /// <summary>JSON 输出</summary>
    [CliOption("json", "", "JSON 输出", Category = "输出")]
    Json,

    /// <summary>超时秒数</summary>
    [CliOption("timeout", "", "超时秒数(默认 30,最大 300)", AcceptsValue = true, Category = "控制")]
    Timeout,

    /// <summary>从文件读取正则</summary>
    [CliOption("regex-file", "", "从文件读取正则表达式", AcceptsValue = true, Category = "控制")]
    RegexFile,

    /// <summary>显示帮助</summary>
    [CliOption("help", "-h", "显示帮助", Category = "控制")]
    Help,
}
