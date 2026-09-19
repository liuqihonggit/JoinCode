
namespace Core.Permission;

/// <summary>
/// 代码安全验证器实现 — 综合 Shell 命令分类与代码内容危险模式检测,阻止破坏性命令与危险 API 调用
/// </summary>
[Register(typeof(ICodeSecurityValidator), ServiceLifetime.Singleton)]
public sealed partial class CodeSecurityValidator : ServiceEntity, ICodeSecurityValidator {
    private readonly ICommandClassifier _commandClassifier;
    private readonly IFileSystem _fs;
    private readonly string _workingDirectory;

    private static readonly Regex UsingNamespaceRegex = new(@"using\s+([\w.]+)");

    private static readonly FrozenSet<string> DangerousPatterns = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        nameof(System.Diagnostics.Process.Start),
        "System.Diagnostics.Process",
        "File.Delete",
        "Directory.Delete",
        "Registry.",
        "Environment.Exit",
        "AppDomain.CurrentDomain",
        "Assembly.Load",
        "Reflection.Emit",
        "DllImport",
        "unsafe",
        "fixed",
        "stackalloc",
        "Marshal.",
        "P/Invoke",
        "[DllImport",
        "RuntimeHelpers",
        "GCHandle",
        "Thread.Abort",
        "Thread.Suspend",
        "Thread.Resume",
        "Activator.CreateInstance",
        "MethodInfo.Invoke",
        "Type.InvokeMember",
        "DynamicMethod",
        "NativeLibrary.Load",
        "Socket.");

    /// <summary>危险模式 AC 自动机 — 一次扫描检测所有危险代码模式。</summary>
    private static readonly AhoCorasick<string> DangerousPatternAc = AhoCorasick.Create(
        DangerousPatterns, ignoreCase: true);

    private static readonly FrozenSet<string> AllowedExternalLibs = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        nameof(System),
        "System.Core",
        "System.Linq",
        "System.Collections",
        "System.Text",
        "System.Text.Json",
        "System.Math",
        "System.DateTime",
        "System.TimeSpan",
        "System.Convert",
        nameof(System.Console));

    /// <summary>
    /// 构造代码安全验证器
    /// </summary>
    /// <param name="commandClassifier">命令分类器,用于判定 Shell 命令的类别</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="workingDirectory">工作目录(可选),默认使用文件系统当前目录</param>
    public CodeSecurityValidator(
        ICommandClassifier commandClassifier,
        IFileSystem fs,
        string? workingDirectory = null) {
        _commandClassifier = commandClassifier;
        _fs = fs;
        _workingDirectory = workingDirectory ?? _fs.GetCurrentDirectory();
    }

    /// <inheritdoc/>
    public ValidationResult Validate(string code, bool allowExternalLibs) {
        if (string.IsNullOrWhiteSpace(code)) {
            return ValidationResult.Invalid("代码不能为空");
        }

        if (IsShellCommand(code)) {
            return ValidateShellCommand(code);
        }

        return ValidateCodeContent(code, allowExternalLibs);
    }

    private static bool IsShellCommand(string code) {
        var shellIndicators = new[]
        {
            "rm ", "del ", "erase ", "format ", "dd ", "mv ", "cp ", "copy ",
            "git ", "ls ", "dir ", "cat ", "grep ", "find ", "echo ", "pwd ",
            "powershell", "pwsh", "bash", "sh ", "cmd ", "curl ", "wget ",
            "Invoke-", "Get-", "Set-", "Remove-", "New-"
        };

        var csharpPatterns = new[]
        {
            "unsafe", "fixed", "stackalloc", "checked", "unchecked",
            "nameof", "typeof", "sizeof", "default", "var ", "new "
        };

        var trimmed = code.Trim();

        if (csharpPatterns.Any(pattern =>
            trimmed.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(pattern + " ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(pattern + "(", StringComparison.OrdinalIgnoreCase))) {
            return false;
        }

        if (shellIndicators.Any(indicator =>
            trimmed.StartsWith(indicator, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        if (trimmed.Contains(' ') && (trimmed.Contains('/') || trimmed.Contains('\\'))) {
            return true;
        }

        if (!trimmed.Contains(' ') &&
            !trimmed.Contains(';') &&
            !trimmed.Contains('{') &&
            !trimmed.Contains('}') &&
            !trimmed.Contains('.') &&
            trimmed.Length > 0 &&
            char.IsLetter(trimmed[0])) {
            return true;
        }

        return false;
    }

    private ValidationResult ValidateShellCommand(string command) {
        try {
            var shellCommand = ShellCommand.Parse(command);
            var classification = _commandClassifier.Classify(shellCommand, _workingDirectory);

            return classification.Category switch {
                CommandCategory.ReadOnly =>
                    ValidationResult.Valid(),

                CommandCategory.Destructive =>
                    ValidationResult.Invalid(
                        $"破坏性命令检测: {classification.Details ?? "命令包含破坏性操作"}"),

                CommandCategory.PathViolation =>
                    ValidationResult.Invalid(
                        $"路径违规: {classification.Details ?? "命令操作超出工作区范围"}"),

                CommandCategory.Unknown =>
                    ValidationResult.Invalid(
                        $"未知命令类型，需要确认: {command}"),

                _ => ValidationResult.Invalid($"无法分类的命令: {command}")
            };
        } catch (Exception ex) {
            return ValidationResult.Invalid(
                $"命令解析失败: {ex.Message}");
        }
    }

    private static ValidationResult ValidateCodeContent(string code, bool allowExternalLibs) {
        var dangerMatch = DangerousPatternAc.FindFirst(code.AsSpan());
        if (dangerMatch is not null) {
            return ValidationResult.Invalid($"代码包含危险操作: {dangerMatch!.Value.Value}");
        }

        if (!allowExternalLibs) {
            var usingMatches = UsingNamespaceRegex.Matches(code);
            foreach (Match match in usingMatches) {
                var ns = match.Groups[1].Value;
                if (!AllowedExternalLibs.Any(allowed => ns.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))) {
                    return ValidationResult.Invalid($"不允许使用外部库命名空间: {ns}");
                }
            }
        }

        return ValidationResult.Valid();
    }
}