
namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PowerShell 权限模式验证。
/// 检查命令是否应基于当前权限模式自动允许。
/// 在 acceptEdits 模式下，文件系统修改的 PS cmdlet 自动允许。
/// 对齐 TS: src/tools/PowerShellTool/modeValidation.ts
/// </summary>
public static class PsModeValidation
{
    /// <summary>
    /// acceptEdits 模式下自动允许的文件系统修改 cmdlet（小写，规范名）
    /// 仅包含简单写入 cmdlet（第一个位置参数 = -Path），复杂 cmdlet 需要询问
    /// </summary>
    private static readonly FrozenSet<string> AcceptEditsAllowedCmdlets = FrozenSet.ToFrozenSet(
    [
        "set-content", "add-content", "remove-item", "clear-content"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// New-Item -ItemType 值中创建文件系统链接的类型
    /// 符号链接、junction 和硬链接都重定向路径解析
    /// </summary>
    private static readonly FrozenSet<string> LinkItemTypes = FrozenSet.ToFrozenSet(
    [
        "symboliclink", "junction", "hardlink"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 改变当前目录的 cmdlet（小写，规范名）
    /// </summary>
    private static readonly FrozenSet<string> CwdChangingCmdlets = FrozenSet.ToFrozenSet(
    [
        "set-location", "push-location", "pop-location",
        "cd", "sl", "chdir", "pushd", "popd"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// PS cmdlet 常见别名 → 规范名映射
    /// </summary>
    private static readonly FrozenDictionary<string, string> CommonAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rm"] = "remove-item", ["del"] = "remove-item",
            ["rd"] = "remove-item", ["rmdir"] = "remove-item",
            ["ri"] = "remove-item", ["erase"] = "remove-item",
            ["sc"] = "set-content", ["ac"] = "add-content",
            ["clc"] = "clear-content", ["ni"] = "new-item",
            ["cp"] = "copy-item", ["copy"] = "copy-item",
            ["mv"] = "move-item", ["move"] = "move-item",
            ["mkdir"] = "new-item", ["md"] = "new-item",
            ["cd"] = "set-location", ["sl"] = "set-location",
            ["chdir"] = "set-location", ["pushd"] = "push-location",
            ["popd"] = "pop-location",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 安全输出 cmdlet（不影响语义，可跳过检查）
    /// </summary>
    private static readonly FrozenSet<string> SafeOutputCmdlets = FrozenSet.ToFrozenSet(
    [
        "out-null", "out-default", "out-host",
        "format-table", "format-list", "format-wide", "format-custom",
        "measure-object", "select-object", "sort-object", "group-object",
        "where-object", "foreach-object", "tee-object"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 权限模式验证结果
    /// </summary>
    public sealed record ModeValidationResult
    {
        /// <summary>
        /// 行为: Allow=自动允许, Passthrough=交给主权限流程
        /// </summary>
        public required PermissionBehavior Behavior { get; init; }

        /// <summary>
        /// 说明消息
        /// </summary>
        public string? Message { get; init; }
    }

    /// <summary>
    /// 解析 cmdlet 别名为规范名
    /// </summary>
    public static string ResolveToCanonical(string name)
    {
        return CommonAliases.TryGetValue(name, out var canonical) ? canonical : name.ToLowerInvariant();
    }

    /// <summary>
    /// 判断 cmdlet 是否为 acceptEdits 模式下自动允许的写入 cmdlet
    /// </summary>
    public static bool IsAcceptEditsAllowedCmdlet(string name)
    {
        var canonical = ResolveToCanonical(name);
        return AcceptEditsAllowedCmdlets.Contains(canonical);
    }

    /// <summary>
    /// 判断 cmdlet 是否改变当前目录
    /// </summary>
    public static bool IsCwdChangingCmdlet(string name)
    {
        return CwdChangingCmdlets.Contains(name) ||
               CwdChangingCmdlets.Contains(ResolveToCanonical(name));
    }

    /// <summary>
    /// 判断命令是否创建符号链接。
    /// 检测 New-Item -ItemType SymbolicLink/Junction/HardLink。
    /// 链接会毒化后续路径解析: 通过链接的相对路径解析到链接目标，而非验证器所见。
    /// </summary>
    public static bool IsSymlinkCreatingCommand(string commandName, string[] args)
    {
        var canonical = ResolveToCanonical(commandName);
        if (canonical != "new-item") return false;

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrEmpty(raw)) continue;

            // 规范化 unicode 短划线前缀和正斜杠
            var normalized = IsDashChar(raw[0]) || raw[0] == '/'
                ? "-" + raw[1..]
                : raw;

            var lower = normalized.ToLowerInvariant();

            // 分割冒号绑定值: -it:SymbolicLink → param='-it', val='symboliclink'
            var colonIdx = lower.IndexOf(':', 1);
            var paramRaw = colonIdx > 0 ? lower[..colonIdx] : lower;

            // 去除反引号转义: -Item`Type → -ItemType
            var param = paramRaw.Replace("`", "");

            if (!IsItemTypeParamAbbrev(param)) continue;

            var rawVal = colonIdx > 0
                ? lower[(colonIdx + 1)..]
                : (i + 1 < args.Length ? args[i + 1]?.ToLowerInvariant() ?? "" : "");

            // 去除反引号和引号
            var val = rawVal.Replace("`", "").Trim('"', '\'');

            if (LinkItemTypes.Contains(val)) return true;
        }

        return false;
    }

    /// <summary>
    /// 检查命令是否应基于当前权限模式不同处理。
    /// acceptEdits 模式下自动允许文件系统修改的 PS cmdlet。
    /// </summary>
    /// <param name="command">PS 命令文本</param>
    /// <param name="mode">当前权限模式</param>
    /// <returns>验证结果</returns>
    public static ModeValidationResult CheckPermissionMode(string command, string mode)
    {
        // 跳过 bypass 和 dontAsk 模式
        if (mode is "bypassPermissions" or "dontAsk")
        {
            return new ModeValidationResult { Behavior = PermissionBehavior.Passthrough, Message = "Mode is handled in main permission flow" };
        }

        if (mode != PermissionModeConstants.AcceptEdits)
        {
            return new ModeValidationResult { Behavior = PermissionBehavior.Passthrough, Message = "No mode-specific validation required" };
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return new ModeValidationResult { Behavior = PermissionBehavior.Passthrough, Message = "Empty command" };
        }

        // 简化实现: 对复合命令（含 ; 或 |）做基本安全检查
        var segments = command.Split(';', '|', StringSplitOptions.RemoveEmptyEntries);

        // 安全检查: 复合命令中的 cd + 写入 拒绝
        var hasCdCommand = false;
        var hasSymlinkCreate = false;
        var hasWriteCommand = false;

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var cmdName = StripCallOperators(tokens[0]).ToLowerInvariant();

            if (IsCwdChangingCmdlet(cmdName)) hasCdCommand = true;
            if (IsSymlinkCreatingCommand(cmdName, tokens[1..])) hasSymlinkCreate = true;
            if (IsAcceptEditsAllowedCmdlet(cmdName)) hasWriteCommand = true;
        }

        if (hasCdCommand && hasWriteCommand)
        {
            return new ModeValidationResult
            {
                Behavior = PermissionBehavior.Passthrough,
                Message = "Compound command contains a directory-changing command with a write operation — cannot auto-allow because path validation uses stale cwd"
            };
        }

        if (hasSymlinkCreate)
        {
            return new ModeValidationResult
            {
                Behavior = PermissionBehavior.Passthrough,
                Message = "Compound command creates a filesystem link — cannot auto-allow because path validation cannot follow just-created links"
            };
        }

        // 检查每个段中的命令是否都是允许的
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var cmdName = StripCallOperators(tokens[0]).ToLowerInvariant();

            // 安全输出 cmdlet 可跳过
            if (SafeOutputCmdlets.Contains(cmdName) || SafeOutputCmdlets.Contains(ResolveToCanonical(cmdName)))
            {
                continue;
            }

            if (!IsAcceptEditsAllowedCmdlet(cmdName))
            {
                return new ModeValidationResult
                {
                    Behavior = PermissionBehavior.Passthrough,
                    Message = $"No mode-specific handling for '{cmdName}' in acceptEdits mode"
                };
            }
        }

        // 所有命令都是文件系统修改 cmdlet — 自动允许
        return new ModeValidationResult { Behavior = PermissionBehavior.Allow, Message = "Auto-allowed in acceptEdits mode" };
    }

    /// <summary>
    /// 去除 PS 调用运算符: &amp; "cmd", . "cmd"
    /// </summary>
    private static string StripCallOperators(string token)
    {
        if (token is "&" or ".") return "";
        return token;
    }

    /// <summary>
    /// 检查参数是否为 -ItemType/-Type 的缩写
    /// 最小前缀: -it（避免与其他 New-Item 参数冲突）, -ty（避免 -t 与 -Target 冲突）
    /// </summary>
    private static bool IsItemTypeParamAbbrev(string param)
    {
        if (param.Length < 3) return false;
        return "-itemtype".StartsWith(param, StringComparison.OrdinalIgnoreCase) ||
               "-type".StartsWith(param, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDashChar(char c) =>
        c == '\u2013' || c == '\u2014' || c == '\u2015' || c == '-';
}
