namespace JoinCode.Abstractions.Security.Scanning;

/// <summary>
/// Windows 保留设备名唯一数据源 — ADR 0012 阶段2/3/5
/// <para>
/// Windows 保留设备名: nul, con, prn, aux, com1-9, lpt1-9
/// git bash 不识别这些为设备,会创建同名普通文件(如 nul 文件)
/// 所有守卫和路径检查委托给本类,禁止在消费方重复硬编码设备名列表
/// </para>
/// <para>
/// 用法:
/// <list type="bullet">
/// <item><see cref="IsMatch"/> — 检查字符串是否为保留设备名(大小写不敏感)</item>
/// <item><see cref="FindInPath"/> — 检查路径中是否包含保留设备名组件</item>
/// <item><see cref="FindAfterRedirect"/> — 从重定向操作符后提取并检查设备名</item>
/// </list>
/// </para>
/// </summary>
public static partial class RetainedDeviceNames {
    /// <summary>
    /// 正则交替模式 — 供 GeneratedRegex 字面量对齐参考(源生成器不支持 const 嵌入)
    /// <para>消费方在 GeneratedRegex 中使用此模式时,必须与此常量保持一致</para>
    /// </summary>
    public const string Pattern = "nul|con|prn|aux|com[1-9]|lpt[1-9]";

    /// <summary>
    /// 保留设备名集合 — 唯一数据源,大小写不敏感
    /// <para>包含 nul, con, prn, aux, com1-com9, lpt1-lpt9 共 22 个</para>
    /// </summary>
    public static readonly FrozenSet<string> Names = BuildNames();

    private static FrozenSet<string> BuildNames() {
        var names = new List<string>(22) { "nul", "con", "prn", "aux" };
        for (var i = 1; i <= 9; i++) {
            names.Add($"com{i}");
            names.Add($"lpt{i}");
        }
        return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查字符串是否为保留设备名(大小写不敏感)
    /// </summary>
    /// <param name="name">待检查的名称</param>
    /// <returns>true 表示是保留设备名</returns>
    public static bool IsMatch(string? name) {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return Names.Contains(name);
    }

    /// <summary>
    /// 检查路径中是否包含保留设备名组件
    /// <para>按 / 和 \ 分隔后逐段检查,每段取首个 . 前的部分作为文件名</para>
    /// </summary>
    /// <param name="path">待检查的路径</param>
    /// <returns>true 表示路径含保留设备名组件</returns>
    public static bool FindInPath(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        foreach (var segment in path.Split('/', '\\')) {
            var name = segment.AsSpan();
            var dotIndex = name.IndexOf('.');
            if (dotIndex >= 0)
                name = name[..dotIndex];
            if (IsMatch(name.ToString()))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查路径中是否包含保留设备名(含扩展名位置)
    /// <para>按 / \ . 分隔后逐段检查,覆盖 path/nul, foo.CON, nul.txt 等场景</para>
    /// <para>等价于正则 (^|[/\\.])(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([/\\.]|$)</para>
    /// </summary>
    /// <param name="path">待检查的路径</param>
    /// <returns>true 表示路径含保留设备名</returns>
    public static bool FindInPathOrExtension(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        foreach (var part in path.Split('/', '\\', '.')) {
            if (IsMatch(part))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 从重定向操作符后提取目标并检查是否为保留设备名
    /// <para>支持 &gt;nul, 2&gt;&gt;nul, &amp;&gt;nul, &lt;nul, &gt;&gt;nul 等变体</para>
    /// </summary>
    /// <param name="command">完整命令字符串</param>
    /// <returns>匹配到的设备名(如 "nul"),未匹配返回 null</returns>
    public static string? FindAfterRedirect(string? command) {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var device = RedirectDeviceRegex().Match(command).Groups["device"].Value;
        return device.Length == 0 ? null : device;
    }

    /// <summary>
    /// 重定向到保留设备名的正则 — 匹配 [fd]op + 保留设备名
    /// <para>fd 可选(0/1/2/&amp;), op 为 &gt;, &gt;&gt;, &gt;|, &amp;&gt;, &lt; 等</para>
    /// <para>设备名模式与 <see cref="Pattern"/> 保持一致</para>
    /// </summary>
    [GeneratedRegex(@"(?<fd>\d*[<>]|\&)?(?<op>>\>?\|?|<)\s*(?<device>nul|con|prn|aux|com[1-9]|lpt[1-9])\b", RegexOptions.IgnoreCase)]
    private static partial Regex RedirectDeviceRegex();
}