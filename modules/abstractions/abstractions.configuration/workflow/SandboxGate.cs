namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 沙箱跳过原因 — 对齐基准 <c>shouldUseSandbox.ts:130-153</c> 的判定分支，供日志/提示/权限判定消费。
/// </summary>
public enum SandboxSkipReason {
    /// <summary>进入沙箱（未被跳过）。</summary>
    Used,
    /// <summary>配置总开关关闭（<c>sandbox.enabled=false</c>，基准缺省）。</summary>
    Disabled,
    /// <summary>当前平台不在 <c>enabledPlatforms</c> 白名单内。</summary>
    PlatformExcluded,
    /// <summary>已启用但没有任何可用沙箱提供器（依赖缺失）。</summary>
    Unavailable,
    /// <summary>命令为空或纯空白。</summary>
    EmptyCommand,
    /// <summary>命令显式请求 <c>dangerouslyDisableSandbox</c> 且策略允许跳出。</summary>
    UnsandboxedRequested,
    /// <summary>命中 <c>excludedCommands</c> 排除清单。</summary>
    ExcludedCommand,
}

/// <summary>
/// 沙箱运行期可用性 — 由沙箱管理器（ISandboxManager）侧提供，
/// 使门控保持纯函数、可做真值表测试（不依赖 DI / 真实进程）。
/// </summary>
/// <param name="PlatformSupported">平台是否在 <c>enabledPlatforms</c> 内（空清单 = 不限制）。</param>
/// <param name="ProviderAvailable">是否至少有一个可用沙箱提供器。</param>
public readonly record struct SandboxRuntimeAvailability(bool PlatformSupported, bool ProviderAvailable);

/// <summary>
/// 沙箱门控判定结果 — <see cref="UseSandbox"/> 为最终结论，<see cref="Reason"/> 说明为什么。
/// </summary>
/// <param name="UseSandbox">是否应在沙箱内执行。</param>
/// <param name="Reason">判定原因。</param>
public readonly record struct SandboxGateDecision(bool UseSandbox, SandboxSkipReason Reason) {
    /// <summary>生成可读判定说明 — 供 Diag/日志/错误提示使用。</summary>
    public string Describe() => Reason switch {
        SandboxSkipReason.Used => "sandbox: used",
        SandboxSkipReason.Disabled => "sandbox: skipped (sandbox.enabled=false)",
        SandboxSkipReason.PlatformExcluded => "sandbox: skipped (platform not in sandbox.enabledPlatforms)",
        SandboxSkipReason.Unavailable => "sandbox: skipped (no available sandbox provider)",
        SandboxSkipReason.EmptyCommand => "sandbox: skipped (empty command)",
        SandboxSkipReason.UnsandboxedRequested => "sandbox: skipped (dangerouslyDisableSandbox allowed by policy)",
        SandboxSkipReason.ExcludedCommand => "sandbox: skipped (command in sandbox.excludedCommands)",
        _ => "sandbox: unknown",
    };
}

/// <summary>
/// 沙箱门控纯函数 — 对齐基准 <c>tools/BashTool/shouldUseSandbox.ts:130-153</c>：
/// <c>enabled</c>（含平台/依赖）→ <c>dangerouslyDisableSandbox</c> 且策略允许 → 空命令 → <c>excludedCommands</c> → 沙箱。
/// <para><b>注意</b>：基准明确声明 <c>excludedCommands</c> 是便利功能而非安全边界
/// （<c>shouldUseSandbox.ts:18-20</c>）；真正的安全控制是权限确认。这里同样声明。</para>
/// </summary>
public static class SandboxGate {
    /// <summary>平台标识 — Windows。</summary>
    public const string PlatformWindows = "windows";
    /// <summary>平台标识 — Linux。</summary>
    public const string PlatformLinux = "linux";
    /// <summary>平台标识 — macOS。</summary>
    public const string PlatformMacOs = "macos";

    /// <summary>当前进程平台标识（与 <c>enabledPlatforms</c> 词表一致）。</summary>
    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? PlatformWindows
        : OperatingSystem.IsMacOS() ? PlatformMacOs
        : PlatformLinux;

    /// <summary>判定平台是否被 <c>enabledPlatforms</c> 允许 — 空清单 = 不限制；比较大小写不敏感。</summary>
    /// <param name="platform">当前平台标识（<see cref="CurrentPlatform"/>）。</param>
    /// <param name="enabledPlatforms">配置的平台清单。</param>
    /// <returns>允许返回 true。</returns>
    public static bool IsPlatformSupported(string platform, IReadOnlyList<string>? enabledPlatforms) {
        ArgumentException.ThrowIfNullOrEmpty(platform);
        if (enabledPlatforms is null || enabledPlatforms.Count == 0) {
            return true;
        }

        for (var i = 0; i < enabledPlatforms.Count; i++) {
            if (string.Equals(enabledPlatforms[i], platform, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 决定某条命令是否应进入沙箱。
    /// </summary>
    /// <param name="command">命令串（Bash/PowerShell 的 command 参数）。</param>
    /// <param name="config">沙箱配置（<see cref="SandboxConfig"/>）。</param>
    /// <param name="availability">运行期可用性（平台 + 提供器）。</param>
    /// <param name="dangerouslyDisableSandbox">本次调用是否显式请求跳出沙箱。</param>
    /// <returns>判定结果。</returns>
    public static SandboxGateDecision Evaluate(
        string? command,
        SandboxConfig config,
        SandboxRuntimeAvailability availability,
        bool dangerouslyDisableSandbox) {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled) {
            return new SandboxGateDecision(false, SandboxSkipReason.Disabled);
        }

        if (!availability.PlatformSupported) {
            return new SandboxGateDecision(false, SandboxSkipReason.PlatformExcluded);
        }

        if (!availability.ProviderAvailable) {
            return new SandboxGateDecision(false, SandboxSkipReason.Unavailable);
        }

        if (string.IsNullOrWhiteSpace(command)) {
            return new SandboxGateDecision(false, SandboxSkipReason.EmptyCommand);
        }

        if (dangerouslyDisableSandbox && config.AllowUnsandboxedCommands) {
            return new SandboxGateDecision(false, SandboxSkipReason.UnsandboxedRequested);
        }

        if (IsExcludedCommand(command, config.ExcludedCommands)) {
            return new SandboxGateDecision(false, SandboxSkipReason.ExcludedCommand);
        }

        return new SandboxGateDecision(true, SandboxSkipReason.Used);
    }

    /// <summary>
    /// 命令是否命中排除清单 — 逐段检查复合命令，每段再做前导环境变量剥离后匹配
    /// （对齐基准 <c>shouldUseSandbox.ts:60-125</c>）。<c>excludedCommands</c> 不是安全边界。
    /// </summary>
    /// <param name="command">命令串。</param>
    /// <param name="patterns">排除模式：<c>cmd:*</c> 前缀 / 含 <c>*</c>、<c>?</c> 通配 / 其余精确。</param>
    /// <returns>命中返回 true。</returns>
    public static bool IsExcludedCommand(string? command, IReadOnlyList<string>? patterns) {
        if (string.IsNullOrWhiteSpace(command) || patterns is null || patterns.Count == 0) {
            return false;
        }

        foreach (var segment in SplitCompoundCommand(command)) {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            var candidate = StripLeadingEnvVars(trimmed);
            if (candidate.Length == 0) {
                continue;
            }

            for (var i = 0; i < patterns.Count; i++) {
                if (MatchesExcludedPattern(candidate, patterns[i])) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>单个模式匹配 — <c>:*</c> 前缀 / 通配符 / 精确（大小写敏感，对齐 Bash 语义）。</summary>
    internal static bool MatchesExcludedPattern(string command, string? pattern) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            return false;
        }

        if (pattern.EndsWith(":*", StringComparison.Ordinal)) {
            var prefix = pattern[..^2];
            if (prefix.Length == 0) {
                return false;
            }

            return command.Equals(prefix, StringComparison.Ordinal)
                || (command.Length > prefix.Length
                    && command.StartsWith(prefix, StringComparison.Ordinal)
                    && command[prefix.Length] == ' ');
        }

        if (pattern.IndexOfAny(['*', '?']) >= 0) {
            return MatchOrdinalWildcard(command, pattern);
        }

        return command.Equals(pattern, StringComparison.Ordinal);
    }

    /// <summary>
    /// 拆分复合命令 — 按 <c>&amp;&amp;</c> / <c>||</c> / <c>;</c> / <c>|</c> / 换行切段，
    /// 引号内与反斜杠转义不切分（单字符 <c>&amp;</c>、重定向 <c>&gt;&amp;</c> 不切分）。
    /// </summary>
    internal static IReadOnlyList<string> SplitCompoundCommand(string command) {
        ArgumentNullException.ThrowIfNull(command);

        var segments = new List<string>();
        var start = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < command.Length; i++) {
            var c = command[i];

            if (c == '\\' && !inSingle) {
                i++; // 跳过被转义字符
                continue;
            }

            if (c == '\'' && !inDouble) {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle) {
                inDouble = !inDouble;
                continue;
            }

            if (inSingle || inDouble) {
                continue;
            }

            var isSeparator = c switch {
                '\n' => true,
                ';' => true,
                '&' => i + 1 < command.Length && command[i + 1] == '&',
                '|' => true,
                _ => false,
            };

            if (!isSeparator) {
                continue;
            }

            segments.Add(command[start..i]);

            if (c is '&' or '|' && i + 1 < command.Length && command[i + 1] == c) {
                i++; // 跳过第二个字符（&& / ||）
            }

            start = i + 1;
        }

        if (start < command.Length) {
            segments.Add(command[start..]);
        }

        return segments;
    }

    /// <summary>剥离命令前导环境变量赋值（<c>FOO=bar BAZ=qux cmd</c> → <c>cmd</c>）。</summary>
    internal static string StripLeadingEnvVars(string command) {
        ArgumentNullException.ThrowIfNull(command);

        var index = 0;
        while (index < command.Length) {
            var assignmentEnd = TryReadEnvAssignment(command, index);
            if (assignmentEnd <= index) {
                break;
            }

            index = assignmentEnd;
            while (index < command.Length && char.IsWhiteSpace(command[index])) {
                index++;
            }
        }

        return command[index..];
    }

    /// <summary>读取一个前导环境变量赋值 — 非赋值返回 -1。</summary>
    private static int TryReadEnvAssignment(string command, int start) {
        var i = start;
        if (i >= command.Length || !(char.IsLetter(command[i]) || command[i] == '_')) {
            return -1;
        }

        while (i < command.Length && (char.IsLetterOrDigit(command[i]) || command[i] == '_')) {
            i++;
        }

        // 数组下标 FOO[0]=x
        if (i < command.Length && command[i] == '[') {
            var close = command.IndexOf(']', i + 1);
            if (close < 0) {
                return -1;
            }
            i = close + 1;
        }

        // 追加赋值 FOO+=x
        if (i < command.Length && command[i] == '+') {
            i++;
        }

        if (i >= command.Length || command[i] != '=') {
            return -1;
        }

        i++;

        if (i < command.Length && command[i] == '\'') {
            var close = command.IndexOf('\'', i + 1);
            return close < 0 ? -1 : close + 1;
        }

        if (i < command.Length && command[i] == '"') {
            i++;
            while (i < command.Length && command[i] != '"') {
                if (command[i] == '\\' && i + 1 < command.Length) {
                    i++;
                }
                i++;
            }
            return i < command.Length ? i + 1 : -1;
        }

        var valueStart = i;
        while (i < command.Length && !char.IsWhiteSpace(command[i])) {
            i++;
        }

        return i > valueStart ? i : -1;
    }

    /// <summary>序号通配符匹配 — <c>*</c> 任意长度、<c>?</c> 单字符，大小写敏感。</summary>
    internal static bool MatchOrdinalWildcard(string input, string pattern) {
        var inputIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var starMatchIndex = 0;

        while (inputIndex < input.Length) {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || pattern[patternIndex] == input[inputIndex])) {
                inputIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
                starIndex = patternIndex;
                starMatchIndex = inputIndex;
                patternIndex++;
                continue;
            }

            if (starIndex >= 0) {
                patternIndex = starIndex + 1;
                starMatchIndex++;
                inputIndex = starMatchIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
