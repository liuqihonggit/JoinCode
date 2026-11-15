namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 节点参数归一化 —— MSYS2/Git Bash 路径展开还原 + 节点名归一化的<b>唯一数据源</b>。
/// <para>为什么需要它：Git Bash / MSYS2 会把以 <c>/</c> 开头的实参当成 Unix 绝对路径并展开成 Windows 路径，
/// 于是斜杠命令名 <c>/agents</c> 到了进程里变成 <c>C:/Program Files/Git/agents</c>，
/// 与目录里的命令名全部失配。这是 shell 层行为，程序无法阻止，只能在入口处还原。</para>
/// <para>历史教训：曾有 3 份各写一遍的实现（jcc 主程序认 <c>/Git/</c> 段、gh 工具认已知 API 首段、
/// slash exe 取末段），其中"只认 <c>/Git/</c>"的写法在 PortableGit 上完全失效
/// （安装路径形如 <c>D:/PortableGit/...</c>）。因此统一到这里，任何一处修复对所有消费方生效。</para>
/// <para>边界：只还原<b>节点名/命令名</b>位置，绝不对普通路径参数做还原——
/// 普通路径被 MSYS 转成 Windows 路径是<b>正确且必要</b>的（如 <c>/tmp/a.txt</c> → 真实临时目录），
/// 擅自"还原"反而会破坏合法路径。</para>
/// </summary>
public static class CliArgNormalizer {
    /// <summary>最短可还原长度（盘符 2 字符 + <c>/Git/</c> 5 字符 + 至少 1 字符名称）。</summary>
    private const int MinRestorableLength = 10;

    /// <summary>最长可还原长度，防御异常超长串进入逐字符扫描。</summary>
    private const int MaxRestorableLength = 4096;

    /// <summary>
    /// 对整条命令行做 MSYS 展开还原（逐参数尝试，不匹配的原样保留）。
    /// </summary>
    /// <param name="args">原始命令行参数。</param>
    /// <returns>还原后的参数数组；无匹配时返回等价内容。</returns>
    public static string[] RestoreMsysPathExpansion(string[] args) {
        if (args.Length == 0) return args;
        var restored = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
            restored[i] = TryRestoreMsysPath(args[i]) ?? args[i];
        return restored;
    }

    /// <summary>
    /// 尝试把一个被 MSYS 展开的参数还原成 <c>/名称</c> 形式。
    /// </summary>
    /// <param name="arg">单个参数。</param>
    /// <returns>还原后的 <c>/名称</c>；不匹配该形状时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 只接受形如 <c>&lt;盘符&gt;:/任意/Git/&lt;单段名称&gt;</c> 的形状：
    /// 必须含 <c>Git</c> 目录段（先锚定 Git 安装根，避免误伤普通盘符路径），
    /// 其后只允许<b>一段</b>且不含 <c>.</c>（含点说明是文件名，不是命令名），
    /// 这样 <c>C:/Users/me/a.txt</c> 之类正常路径不会被改写。
    /// </remarks>
    public static string? TryRestoreMsysPath(string arg) {
        if (arg.Length < MinRestorableLength || arg.Length > MaxRestorableLength) return null;
        if (arg[1] != ':') return null;
        if (!char.IsLetter(arg[0])) return null;

        var gitIndex = arg.IndexOf("/Git/", StringComparison.OrdinalIgnoreCase);
        if (gitIndex < 0) gitIndex = arg.IndexOf("\\Git\\", StringComparison.OrdinalIgnoreCase);
        if (gitIndex < 0) return null;

        var rest = arg.AsSpan(gitIndex + 5);
        while (!rest.IsEmpty && (rest[0] == '/' || rest[0] == '\\')) rest = rest[1..];
        if (rest.IsEmpty) return null;
        if (rest.Contains('.') || rest.Contains('/') || rest.Contains('\\')) return null;
        return string.Concat("/", rest);
    }

    /// <summary>
    /// 归一化节点名/命令名：去掉前导斜杠、去掉 Windows 路径前缀、去掉 <c>.exe</c> 后缀。
    /// </summary>
    /// <param name="raw">原始名称，可能是 <c>agents</c>、<c>/agents</c>、<c>C:/Program Files/Git/agents</c>。</param>
    /// <returns>归一化后的纯名称（小写不改，保持调用方给出的原始大小写）。</returns>
    /// <remarks>
    /// 名称位置取末段是安全的：命令名/工具名本身不可能包含路径分隔符。
    /// 与 <see cref="TryRestoreMsysPath"/> 的分工是——那个负责"看起来像路径但其实是命令名"，
    /// 这个负责"已经是路径形态、只需要末段"，覆盖 PortableGit 等任意安装路径。
    /// </remarks>
    public static string NormalizeNodeName(string? raw) {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0) return string.Empty;

        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0 && lastSeparator + 1 < name.Length) name = name[(lastSeparator + 1)..];
        else name = name.TrimStart('/', '\\');

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }
}
