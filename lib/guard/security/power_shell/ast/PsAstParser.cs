namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS AST 解析器 — spawn pwsh 子进程调用 Parser.ParseInput，解析 JSON 输出
/// 对齐 TS: parser.ts — 同样的架构，进程外解析，AOT 兼容
/// </summary>
public static partial class PsAstParser {
    private static readonly string ParseScriptBody = BuildParseScript();
    private static string? _cachedPwshPath;
    private static readonly AsyncLock CacheLock = new("PsAstParser");

    private const int MaxCacheSize = 256;
    private static ImmutableHamT<string, PsParsedCommand> ParseCache = ImmutableHamT<string, PsParsedCommand>.Empty;

    private static readonly FrozenSet<string> TransientErrorIds = FrozenSet.ToFrozenSet(
        ["PwshSpawnError", "PwshError", "PwshTimeout", "EmptyOutput", "InvalidJson", "ProcessStartFailed", "ProcessTimeout", "ParseException"],
        StringComparer.Ordinal);

    /// <summary>
    /// 解析 PS 命令为结构化结果
    /// </summary>
    public static PsParsedCommand Parse(string command, IProcessService? processService = null) {
        if (string.IsNullOrWhiteSpace(command)) {
            return new PsParsedCommand {
                Valid = false,
                OriginalCommand = command,
            };
        }

        if (ParseCache.TryGetValue(command, out var cached)) {
            return cached;
        }

        var result = ParseCore(command, processService);

        if (ShouldCache(result)) {
            if (ParseCache.Count >= MaxCacheSize) {
                Interlocked.Exchange(ref ParseCache, ImmutableHamT<string, PsParsedCommand>.Empty);
            }
            while (true) {
                var current = ParseCache;
                var updated = current.SetItem(command, result);
                if (Interlocked.CompareExchange(ref ParseCache, updated, current) == current) break;
            }
        }

        return result;
    }

    private static bool ShouldCache(PsParsedCommand result) {
        if (result.Valid) return true;
        if (result.Errors is { Length: > 0 } && TransientErrorIds.Contains(result.Errors[0].ErrorId)) {
            return false;
        }
        return true;
    }

    private static PsParsedCommand ParseCore(string command, IProcessService? processService = null) {
        var pwshPath = FindPwshPath();
        if (pwshPath is null) {
            return new PsParsedCommand {
                Valid = false,
                OriginalCommand = command,
                Errors = [new PsParseError { Message = "PowerShell not found", ErrorId = "NoPwsh" }],
            };
        }

        try {
            var encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
            var scriptEncoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ParseScriptBody));

            if (processService is not null) {
                var options = new ProcessOptions {
                    FileName = pwshPath,
                    ArgumentList = new[] { "-NoProfile", "-NonInteractive", "-NoLogo", "-EncodedCommand", scriptEncoded },
                    TimeoutMs = 5000,
                    EnvironmentVariables = new Dictionary<string, string> {
                        ["EncodedCommand"] = encodedCommand
                    }
                };

                var result = processService.ExecuteAsync(options).GetAwaiter().GetResult();

                if (result.ExitCode == -1) {
                    return new PsParsedCommand {
                        Valid = false,
                        OriginalCommand = command,
                        Errors = [new PsParseError { Message = "pwsh parse process timed out", ErrorId = "ProcessTimeout" }],
                    };
                }

                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) {
                    return new PsParsedCommand {
                        Valid = false,
                        OriginalCommand = command,
                        Errors = [new PsParseError { Message = $"pwsh parse process failed: {result.StandardError}", ErrorId = "ProcessFailed" }],
                    };
                }

                return DeserializeParsedCommand(result.StandardOutput, command);
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = pwshPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(scriptEncoded);

            startInfo.EnvironmentVariables["EncodedCommand"] = encodedCommand;

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) {
                return new PsParsedCommand {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = "Failed to start pwsh process", ErrorId = "ProcessStartFailed" }],
                };
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (!process.HasExited) {
                process.Kill();
                return new PsParsedCommand {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = "pwsh parse process timed out", ErrorId = "ProcessTimeout" }],
                };
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout)) {
                return new PsParsedCommand {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = $"pwsh parse process failed: {stderr}", ErrorId = "ProcessFailed" }],
                };
            }

            return DeserializeParsedCommand(stdout, command);
        } catch (Exception ex) {
            return new PsParsedCommand {
                Valid = false,
                OriginalCommand = command,
                Errors = [new PsParseError { Message = ex.Message, ErrorId = "ParseException" }],
            };
        }
    }

    /// <summary>
    /// 获取所有命令元素（跨所有语句、管道段、嵌套命令）
    /// </summary>
    public static List<PsCommandElement> GetAllCommands(PsParsedCommand parsed) {
        var commands = new List<PsCommandElement>();

        foreach (var stmt in parsed.Statements) {
            commands.AddRange(stmt.Commands);
            commands.AddRange(stmt.NestedCommands);
        }

        return commands;
    }

    /// <summary>
    /// 获取所有命令名（小写，用于大小写不敏感匹配）
    /// </summary>
    public static List<string> GetAllCommandNames(PsParsedCommand parsed) {
        var names = new List<string>();
        foreach (var cmd in GetAllCommands(parsed)) {
            names.Add(cmd.Name.ToLowerInvariant());
        }
        return names;
    }

    /// <summary>
    /// 检查是否存在指定名称的命令（支持别名解析）
    /// </summary>
    public static bool HasCommandNamed(PsParsedCommand parsed, string name) {
        var lowerName = name.ToLowerInvariant();
        foreach (var cmdName in GetAllCommandNames(parsed)) {
            if (cmdName == lowerName) return true;

            if (PsAliases.TryResolve(cmdName, out var canonical) &&
                canonical.Equals(lowerName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (PsAliases.TryResolve(lowerName, out var canonical2) &&
                canonical2.Equals(cmdName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 获取所有变量引用
    /// </summary>
    public static List<PsVariable> GetVariables(PsParsedCommand parsed) {
        return [.. parsed.Variables];
    }

    /// <summary>
    /// 按作用域过滤变量（如 "env" 过滤 $env:PATH）
    /// </summary>
    public static List<PsVariable> GetVariablesByScope(PsParsedCommand parsed, string scope) {
        var prefix = scope.ToLowerInvariant() + ":";
        return parsed.Variables.Where(v => v.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 推导安全标志 — 从解析结果中提取
    /// </summary>
    public static PsSecurityFlags DeriveSecurityFlags(PsParsedCommand parsed) {
        bool hasSubExpr = false, hasScriptBlocks = false, hasSplatting = false;
        bool hasExpandableStrings = false, hasMemberInvocations = false, hasAssignments = false;

        foreach (var stmt in parsed.Statements) {
            if (stmt.SecurityPatterns is not null) {
                if (stmt.SecurityPatterns.HasSubExpressions) hasSubExpr = true;
                if (stmt.SecurityPatterns.HasScriptBlocks) hasScriptBlocks = true;
                if (stmt.SecurityPatterns.HasExpandableStrings) hasExpandableStrings = true;
                if (stmt.SecurityPatterns.HasMemberInvocations) hasMemberInvocations = true;
            }

            foreach (var cmd in stmt.Commands) {
                foreach (var et in cmd.ElementTypes) {
                    switch (et) {
                        case PsElementType.SubExpression: hasSubExpr = true; break;
                        case PsElementType.ScriptBlock: hasScriptBlocks = true; break;
                        case PsElementType.ExpandableString: hasExpandableStrings = true; break;
                        case PsElementType.MemberInvocation: hasMemberInvocations = true; break;
                    }
                }
            }

            if (stmt.StatementType is "AssignmentStatementAst") hasAssignments = true;

            foreach (var v in parsed.Variables) {
                if (v.IsSplatted) hasSplatting = true;
            }
        }

        return new PsSecurityFlags {
            HasSubExpressions = hasSubExpr,
            HasScriptBlocks = hasScriptBlocks,
            HasSplatting = hasSplatting,
            HasExpandableStrings = hasExpandableStrings,
            HasMemberInvocations = hasMemberInvocations,
            HasAssignments = hasAssignments,
            HasStopParsing = parsed.HasStopParsing,
        };
    }

    /// <summary>
    /// 检查参数是否匹配指定参数名（支持缩写）
    /// </summary>
    public static bool CommandHasArgAbbreviation(PsCommandElement cmd, string fullParam, string minPrefix) {
        var lowerFull = fullParam.ToLowerInvariant();
        var lowerMin = minPrefix.ToLowerInvariant();

        foreach (var arg in cmd.Args) {
            var colonIdx = arg.IndexOf(':', 1);
            var paramPart = colonIdx > 0 ? arg[..colonIdx] : arg;

            var lower = paramPart.Replace("`", "").ToLowerInvariant();

            if (lower.StartsWith(lowerMin) &&
                lowerFull.StartsWith(lower) &&
                lower.Length <= lowerFull.Length) {
                return true;
            }
        }
        return false;
    }

    private static readonly FrozenSet<char> AltParamPrefixes = FrozenSet.ToFrozenSet(
        ['/', '\u2013', '\u2014', '\u2015']);

    /// <summary>
    /// 检查参数是否匹配指定参数名（支持缩写与替代前缀 /、–、—、―）
    /// </summary>
    public static bool PsHasParamAbbreviation(PsCommandElement cmd, string fullParam, string minPrefix) {
        if (CommandHasArgAbbreviation(cmd, fullParam, minPrefix)) {
            return true;
        }

        var normalizedArgs = cmd.Args.Select(a =>
            a.Length > 0 && AltParamPrefixes.Contains(a[0]) ? "-" + a[1..] : a).ToArray();

        var normalizedCmd = new PsCommandElement {
            Name = cmd.Name,
            NameType = cmd.NameType,
            Args = [.. normalizedArgs],
            ElementTypes = cmd.ElementTypes,
            Text = cmd.Text,
            Redirections = cmd.Redirections,
        };
        return CommandHasArgAbbreviation(normalizedCmd, fullParam, minPrefix);
    }

    /// <summary>
    /// 判断是否为 PS 可执行文件名
    /// </summary>
    public static bool IsPowerShellExecutable(string name) {
        var lower = name.ToLowerInvariant();
        if (PsExecutableNames.Contains(lower)) return true;

        var lastSep = Math.Max(lower.LastIndexOf('/'), lower.LastIndexOf('\\'));
        if (lastSep >= 0) {
            return PsExecutableNames.Contains(lower[(lastSep + 1)..]);
        }
        return false;
    }

    private static readonly FrozenSet<string> PsExecutableNames = FrozenSet.ToFrozenSet(
        ["pwsh", "pwsh.exe", "powershell", "powershell.exe"],
        StringComparer.OrdinalIgnoreCase);

    private static string? FindPwshPath() {
        using (CacheLock.LockOrCrash()) {
            if (_cachedPwshPath is not null) return _cachedPwshPath;
        }

        string? found = null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separators = new[] { ';' };
        foreach (var dir in pathEnv.Split(separators, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                foreach (var name in new[] { "pwsh.exe", "pwsh" }) {
                    var fullPath = Path.Combine(dir.Trim(), name);
                    if (Path.Exists(fullPath)) {
                        found = fullPath;
                        break;
                    }
                }
            } catch (IOException) { continue; }
            if (found is not null) break;
        }

        if (found is not null) {
            using (CacheLock.LockOrCrash()) {
                _cachedPwshPath = found;
            }
        }

        return found;
    }

    private static PsParsedCommand DeserializeParsedCommand(string json, string originalCommand) {
        try {
            var dto = JsonSerializer.Deserialize(json, PsAstJsonContext.Default.PsAstResultDto);
            if (dto is null) {
                return new PsParsedCommand {
                    Valid = false,
                    OriginalCommand = originalCommand,
                    Errors = [new PsParseError { Message = "Failed to deserialize parse output", ErrorId = "DeserializationFailed" }],
                };
            }

            var errors = dto.Errors.Select(e => new PsParseError {
                Message = e.Message,
                ErrorId = e.ErrorId,
            }).ToArray();

            var variables = dto.Variables.Select(v => new PsVariable(v.Path, v.IsSplatted)).ToArray();

            var statements = dto.Statements.Select(DeserializeStatement).ToArray();

            return new PsParsedCommand {
                Valid = dto.Valid,
                OriginalCommand = originalCommand,
                Errors = errors,
                HasStopParsing = dto.HasStopParsing,
                TypeLiterals = [.. dto.TypeLiterals],
                HasUsingStatements = dto.HasUsingStatements,
                HasScriptRequirements = dto.HasScriptRequirements,
                Statements = statements,
                Variables = variables,
            };
        } catch {
            return new PsParsedCommand {
                Valid = false,
                OriginalCommand = originalCommand,
                Errors = [new PsParseError { Message = "Failed to deserialize parse output", ErrorId = "DeserializationFailed" }],
            };
        }
    }

    private static PsStatement DeserializeStatement(PsAstStatementDto s) {
        var commands = s.Elements
            .Select(DeserializeCommandFromElement)
            .Where(c => c is not null)
            .Cast<PsCommandElement>()
            .ToArray();

        var nestedCommands = s.NestedCommands
            .Select(DeserializeCommandFromElement)
            .Where(c => c is not null)
            .Cast<PsCommandElement>()
            .ToArray();

        var redirections = s.Redirections.Select(DeserializeRedirection).ToArray();

        PsSecurityPatterns? securityPatterns = null;
        if (s.SecurityPatterns is { } sp) {
            securityPatterns = new PsSecurityPatterns {
                HasMemberInvocations = sp.HasMemberInvocations,
                HasSubExpressions = sp.HasSubExpressions,
                HasExpandableStrings = sp.HasExpandableStrings,
                HasScriptBlocks = sp.HasScriptBlocks,
            };
        }

        return new PsStatement {
            StatementType = s.Type,
            Commands = commands,
            NestedCommands = nestedCommands,
            Redirections = redirections,
            Text = s.Text,
            SecurityPatterns = securityPatterns,
        };
    }

    private static PsCommandElement? DeserializeCommandFromElement(PsAstElementDto elem) {
        if (elem.Type != "CommandAst") return null;

        if (elem.CommandElements.Count == 0) return null;

        var commandElements = elem.CommandElements
            .Select(c => (c.Text, c.Type, c.Value))
            .ToList();

        var rawName = commandElements[0].Value ?? commandElements[0].Text;
        rawName = StripQuotes(rawName);
        rawName = StripModulePrefix(rawName);

        var nameType = ClassifyCommandName(rawName);
        var name = rawName;

        var args = new List<string>();
        var elementTypes = new List<PsElementType>();

        elementTypes.Add(MapElementTypeFromRaw(commandElements[0].Type));

        for (var i = 1; i < commandElements.Count; i++) {
            args.Add(commandElements[i].Value ?? commandElements[i].Text);
            elementTypes.Add(MapElementTypeFromRaw(commandElements[i].Type));
        }

        var redirections = elem.Redirections.Select(DeserializeRedirection).ToArray();

        return new PsCommandElement {
            Name = name,
            NameType = nameType,
            Args = [.. args],
            ElementTypes = [.. elementTypes],
            Text = elem.Text,
            Redirections = redirections,
        };
    }

    private static PsElementType MapElementTypeFromRaw(string rawType) {
        return rawType switch {
            "ScriptBlockExpressionAst" => PsElementType.ScriptBlock,
            "SubExpressionAst" or "ArrayExpressionAst" or "ParenExpressionAst" => PsElementType.SubExpression,
            "ExpandableStringExpressionAst" => PsElementType.ExpandableString,
            "InvokeMemberExpressionAst" or "MemberExpressionAst" => PsElementType.MemberInvocation,
            "VariableExpressionAst" => PsElementType.Variable,
            "StringConstantExpressionAst" or "ConstantExpressionAst" => PsElementType.StringConstant,
            "CommandParameterAst" => PsElementType.Parameter,
            _ => PsElementType.Other,
        };
    }

    private static PsRedirection DeserializeRedirection(PsAstRedirectionDto r) {
        if (r.Type == "MergingRedirectionAst") {
            return new PsRedirection("2>&1", "", true);
        }

        if (r.Type == "FileRedirectionAst") {
            var op = (r.Append, r.FromStream) switch {
                (true, "Error") => "2>>",
                (true, "All") => "*>>",
                (true, _) => ">>",
                (false, "Error") => "2>",
                (false, "All") => "*>",
                (false, _) => ">",
            };

            return new PsRedirection(op, r.LocationText, false);
        }

        return new PsRedirection(">", "", false);
    }

    private static string StripQuotes(string name) {
        if (name.Length >= 2) {
            var first = name[0];
            var last = name[^1];
            if ((first == '\'' || first == '"') && first == last) {
                return name[1..^1];
            }
        }
        return name;
    }

    private static string StripModulePrefix(string name) {
        var idx = name.LastIndexOf('\\');
        if (idx < 0) return name;

        if (name.Length >= 2 && name[1] == ':') return name;
        if (name.StartsWith("\\\\")) return name;
        if (name.StartsWith(".\\")) return name;
        if (name.StartsWith("..\\")) return name;

        return name[(idx + 1)..];
    }

    private static PsCommandNameType ClassifyCommandName(string name) {
        if (name.Any(c => c > 0x7F)) {
            return PsCommandNameType.Application;
        }

        if (Regex.IsMatch(name, @"^[A-Za-z]+-[A-Za-z][A-Za-z0-9_]*$")) {
            return PsCommandNameType.Cmdlet;
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains('.')) {
            return PsCommandNameType.Application;
        }

        return PsCommandNameType.Unknown;
    }

    private static string BuildParseScript() {
        return """
if (-not $env:EncodedCommand) {
    Write-Output '{"valid":false,"errors":[{"message":"No command provided","errorId":"NoInput"}],"statements":[],"variables":[],"hasStopParsing":false,"originalCommand":""}'
    exit 0
}

$Command = [System.Text.Encoding]::Unicode.GetString([System.Convert]::FromBase64String($env:EncodedCommand))

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $Command,
    [ref]$tokens,
    [ref]$parseErrors
)

$allVariables = [System.Collections.ArrayList]::new()

function Get-RawCommandElements {
    param([System.Management.Automation.Language.CommandAst]$CmdAst)
    $elems = [System.Collections.ArrayList]::new()
    foreach ($ce in $CmdAst.CommandElements) {
        $ceData = @{ type = $ce.GetType().Name; text = $ce.Extent.Text }
        if ($ce.PSObject.Properties['Value'] -and $null -ne $ce.Value -and $ce.Value -is [string]) {
            $ceData.value = $ce.Value
        }
        if ($ce -is [System.Management.Automation.Language.CommandExpressionAst]) {
            $ceData.expressionType = $ce.Expression.GetType().Name
        }
        $a=$ce.Argument;if($a){$ceData.children=@(@{type=$a.GetType().Name;text=$a.Extent.Text})}
        [void]$elems.Add($ceData)
    }
    return $elems
}

function Get-RawRedirections {
    param($Redirections)
    $result = [System.Collections.ArrayList]::new()
    foreach ($redir in $Redirections) {
        $redirData = @{ type = $redir.GetType().Name }
        if ($redir -is [System.Management.Automation.Language.FileRedirectionAst]) {
            $redirData.append = [bool]$redir.Append
            $redirData.fromStream = $redir.FromStream.ToString()
            $redirData.locationText = $redir.Location.Extent.Text
        }
        [void]$result.Add($redirData)
    }
    return $result
}

function Get-SecurityPatterns($A) {
    $p = @{}
    foreach ($n in $A.FindAll({ param($x)
        $x -is [System.Management.Automation.Language.MemberExpressionAst] -or
        $x -is [System.Management.Automation.Language.SubExpressionAst] -or
        $x -is [System.Management.Automation.Language.ArrayExpressionAst] -or
        $x -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -or
        $x -is [System.Management.Automation.Language.ScriptBlockExpressionAst] -or
        $x -is [System.Management.Automation.Language.ParenExpressionAst]
    }, $true)) { switch ($n.GetType().Name) {
        'InvokeMemberExpressionAst' { $p.hasMemberInvocations = $true }
        'MemberExpressionAst' { $p.hasMemberInvocations = $true }
        'SubExpressionAst' { $p.hasSubExpressions = $true }
        'ArrayExpressionAst' { $p.hasSubExpressions = $true }
        'ParenExpressionAst' { $p.hasSubExpressions = $true }
        'ExpandableStringExpressionAst' { $p.hasExpandableStrings = $true }
        'ScriptBlockExpressionAst' { $p.hasScriptBlocks = $true }
    }}
    if ($p.Count -gt 0) { return $p }
    return $null
}

$varExprs = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.VariableExpressionAst] }, $true)
foreach ($v in $varExprs) {
    [void]$allVariables.Add(@{
        path = $v.VariablePath.ToString()
        isSplatted = [bool]$v.Splatted
    })
}

$typeLiterals = [System.Collections.ArrayList]::new()
foreach ($t in $ast.FindAll({ param($n)
    $n -is [System.Management.Automation.Language.TypeExpressionAst] -or
    $n -is [System.Management.Automation.Language.TypeConstraintAst]
}, $true)) { [void]$typeLiterals.Add($t.TypeName.FullName) }

$hasStopParsing = $false
$tk = [System.Management.Automation.Language.TokenKind]
foreach ($tok in $tokens) {
    if ($tok.Kind -eq $tk::MinusMinus) { $hasStopParsing = $true; break }
    if ($tok.Kind -eq $tk::Generic -and ($tok.Text -replace '[\u2013\u2014\u2015]','-') -eq '--%') {
        $hasStopParsing = $true; break
    }
}

$statements = [System.Collections.ArrayList]::new()

function Process-BlockStatements {
    param($Block)
    if (-not $Block) { return }

    foreach ($stmt in $Block.Statements) {
        $statement = @{
            type = $stmt.GetType().Name
            text = $stmt.Extent.Text
        }

        if ($stmt -is [System.Management.Automation.Language.PipelineAst]) {
            $elements = [System.Collections.ArrayList]::new()
            foreach ($element in $stmt.PipelineElements) {
                $elemData = @{
                    type = $element.GetType().Name
                    text = $element.Extent.Text
                }

                if ($element -is [System.Management.Automation.Language.CommandAst]) {
                    $elemData.commandElements = @(Get-RawCommandElements -CmdAst $element)
                    $elemData.redirections = @(Get-RawRedirections -Redirections $element.Redirections)
                } elseif ($element -is [System.Management.Automation.Language.CommandExpressionAst]) {
                    $elemData.expressionType = $element.Expression.GetType().Name
                    $elemData.redirections = @(Get-RawRedirections -Redirections $element.Redirections)
                }

                [void]$elements.Add($elemData)
            }
            $statement.elements = @($elements)

            $allNestedCmds = $stmt.FindAll(
                { param($node) $node -is [System.Management.Automation.Language.CommandAst] },
                $true
            )
            $nestedCmds = [System.Collections.ArrayList]::new()
            foreach ($cmd in $allNestedCmds) {
                if ($cmd.Parent -eq $stmt) { continue }
                $nested = @{
                    type = $cmd.GetType().Name
                    text = $cmd.Extent.Text
                    commandElements = @(Get-RawCommandElements -CmdAst $cmd)
                    redirections = @(Get-RawRedirections -Redirections $cmd.Redirections)
                }
                [void]$nestedCmds.Add($nested)
            }
            if ($nestedCmds.Count -gt 0) {
                $statement.nestedCommands = @($nestedCmds)
            }
            $r = $stmt.FindAll({param($n) $n -is [System.Management.Automation.Language.FileRedirectionAst]}, $true)
            if ($r.Count -gt 0) {
                $rr = @(Get-RawRedirections -Redirections $r)
                $statement.redirections = if ($statement.redirections) { @($statement.redirections) + $rr } else { $rr }
            }
        } else {
            $nestedCmdAsts = $stmt.FindAll(
                { param($node) $node -is [System.Management.Automation.Language.CommandAst] },
                $true
            )
            $nested = [System.Collections.ArrayList]::new()
            foreach ($cmd in $nestedCmdAsts) {
                [void]$nested.Add(@{
                    type = 'CommandAst'
                    text = $cmd.Extent.Text
                    commandElements = @(Get-RawCommandElements -CmdAst $cmd)
                    redirections = @(Get-RawRedirections -Redirections $cmd.Redirections)
                })
            }
            if ($nested.Count -gt 0) {
                $statement.nestedCommands = @($nested)
            }
            $r = $stmt.FindAll({param($n) $n -is [System.Management.Automation.Language.FileRedirectionAst]}, $true)
            if ($r.Count -gt 0) { $statement.redirections = @(Get-RawRedirections -Redirections $r) }
        }

        $sp = Get-SecurityPatterns $stmt
        if ($sp) { $statement.securityPatterns = $sp }

        [void]$statements.Add($statement)
    }

    if ($Block.Traps) {
        foreach ($trap in $Block.Traps) {
            $statement = @{
                type = 'TrapStatementAst'
                text = $trap.Extent.Text
            }
            $nestedCmdAsts = $trap.FindAll(
                { param($node) $node -is [System.Management.Automation.Language.CommandAst] },
                $true
            )
            $nestedCmds = [System.Collections.ArrayList]::new()
            foreach ($cmd in $nestedCmdAsts) {
                $nested = @{
                    type = $cmd.GetType().Name
                    text = $cmd.Extent.Text
                    commandElements = @(Get-RawCommandElements -CmdAst $cmd)
                    redirections = @(Get-RawRedirections -Redirections $cmd.Redirections)
                }
                [void]$nestedCmds.Add($nested)
            }
            if ($nestedCmds.Count -gt 0) {
                $statement.nestedCommands = @($nestedCmds)
            }
            $r = $trap.FindAll({param($n) $n -is [System.Management.Automation.Language.FileRedirectionAst]}, $true)
            if ($r.Count -gt 0) { $statement.redirections = @(Get-RawRedirections -Redirections $r) }
            $sp = Get-SecurityPatterns $trap
            if ($sp) { $statement.securityPatterns = $sp }
            [void]$statements.Add($statement)
        }
    }
}

Process-BlockStatements -Block $ast.BeginBlock
Process-BlockStatements -Block $ast.ProcessBlock
Process-BlockStatements -Block $ast.EndBlock
Process-BlockStatements -Block $ast.CleanBlock
Process-BlockStatements -Block $ast.DynamicParamBlock

if ($ast.ParamBlock) {
  $pb = $ast.ParamBlock
  $pn = [System.Collections.ArrayList]::new()
  foreach ($c in $pb.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]}, $true)) {
    [void]$pn.Add(@{type='CommandAst';text=$c.Extent.Text;commandElements=@(Get-RawCommandElements -CmdAst $c);redirections=@(Get-RawRedirections -Redirections $c.Redirections)})
  }
  $pr = $pb.FindAll({param($n) $n -is [System.Management.Automation.Language.FileRedirectionAst]}, $true)
  $ps = Get-SecurityPatterns $pb
  if ($pn.Count -gt 0 -or $pr.Count -gt 0 -or $ps) {
    $st = @{type='ParamBlockAst';text=$pb.Extent.Text}
    if ($pn.Count -gt 0) { $st.nestedCommands = @($pn) }
    if ($pr.Count -gt 0) { $st.redirections = @(Get-RawRedirections -Redirections $pr) }
    if ($ps) { $st.securityPatterns = $ps }
    [void]$statements.Add($st)
  }
}

$hasUsingStatements = $ast.UsingStatements -and $ast.UsingStatements.Count -gt 0
$hasScriptRequirements = $ast.ScriptRequirements -ne $null

$output = @{
    valid = ($parseErrors.Count -eq 0)
    errors = @($parseErrors | ForEach-Object {
        @{
            message = $_.Message
            errorId = $_.ErrorId
        }
    })
    statements = @($statements)
    variables = @($allVariables)
    hasStopParsing = $hasStopParsing
    originalCommand = $Command
    typeLiterals = @($typeLiterals)
    hasUsingStatements = [bool]$hasUsingStatements
    hasScriptRequirements = [bool]$hasScriptRequirements
}

$output | ConvertTo-Json -Depth 10 -Compress
""";
    }
}

/// <summary>
/// PS AST 解析顶层结果 DTO — 对应 pwsh 脚本输出的 JSON 根对象
/// </summary>
public sealed class PsAstResultDto {
    /// <summary>是否解析成功（无语法错误）</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    /// <summary>解析错误列表</summary>
    [JsonPropertyName("errors")]
    public List<PsAstErrorDto> Errors { get; set; } = [];

    /// <summary>语句列表</summary>
    [JsonPropertyName("statements")]
    public List<PsAstStatementDto> Statements { get; set; } = [];

    /// <summary>变量引用列表</summary>
    [JsonPropertyName("variables")]
    public List<PsAstVariableDto> Variables { get; set; } = [];

    /// <summary>是否包含 stop-parsing token (--%)</summary>
    [JsonPropertyName("hasStopParsing")]
    public bool HasStopParsing { get; set; }

    /// <summary>原始命令文本</summary>
    [JsonPropertyName("originalCommand")]
    public string OriginalCommand { get; set; } = string.Empty;

    /// <summary>.NET 类型字面量列表</summary>
    [JsonPropertyName("typeLiterals")]
    public List<string> TypeLiterals { get; set; } = [];

    /// <summary>是否包含 using 语句</summary>
    [JsonPropertyName("hasUsingStatements")]
    public bool HasUsingStatements { get; set; }

    /// <summary>是否包含 #Requires 指令</summary>
    [JsonPropertyName("hasScriptRequirements")]
    public bool HasScriptRequirements { get; set; }
}

/// <summary>
/// PS 解析错误 DTO — 对应 errors 数组项
/// </summary>
public sealed class PsAstErrorDto {
    /// <summary>错误消息</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>错误标识符</summary>
    [JsonPropertyName("errorId")]
    public string ErrorId { get; set; } = string.Empty;
}

/// <summary>
/// PS 变量引用 DTO — 对应 variables 数组项
/// </summary>
public sealed class PsAstVariableDto {
    /// <summary>变量路径</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>是否 splatting 变量</summary>
    [JsonPropertyName("isSplatted")]
    public bool IsSplatted { get; set; }
}

/// <summary>
/// PS 语句 DTO — discriminated union，Type 字段区分不同语句类型（PipelineAst/IfStatementAst 等）
/// <para>公共字段统一 DTO 化，不同 type 的特有字段未出现（脚本仅输出公共字段）</para>
/// </summary>
public sealed class PsAstStatementDto {
    /// <summary>语句类型（PipelineAst, IfStatementAst, AssignmentStatementAst 等）</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>完整文本</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>主命令元素列表（管道段）</summary>
    [JsonPropertyName("elements")]
    public List<PsAstElementDto> Elements { get; set; } = [];

    /// <summary>嵌套命令列表</summary>
    [JsonPropertyName("nestedCommands")]
    public List<PsAstElementDto> NestedCommands { get; set; } = [];

    /// <summary>语句级重定向</summary>
    [JsonPropertyName("redirections")]
    public List<PsAstRedirectionDto> Redirections { get; set; } = [];

    /// <summary>安全模式（可选，存在时非 null）</summary>
    [JsonPropertyName("securityPatterns")]
    public PsAstSecurityPatternsDto? SecurityPatterns { get; set; }
}

/// <summary>
/// PS 命令元素 DTO — 对应 elements/nestedCommands 数组项
/// <para>discriminated union：Type="CommandAst" 才是命令，其他类型（CommandExpressionAst 等）跳过</para>
/// </summary>
public sealed class PsAstElementDto {
    /// <summary>元素类型（CommandAst/CommandExpressionAst 等）</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>完整文本</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>命令元素列表（命令名 + 参数）</summary>
    [JsonPropertyName("commandElements")]
    public List<PsAstCommandElementItemDto> CommandElements { get; set; } = [];

    /// <summary>重定向列表</summary>
    [JsonPropertyName("redirections")]
    public List<PsAstRedirectionDto> Redirections { get; set; } = [];
}

/// <summary>
/// PS commandElements 数组项 DTO — 命令名/参数的文本、类型与值
/// </summary>
public sealed class PsAstCommandElementItemDto {
    /// <summary>元素文本</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>元素 AST 类型</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>元素值（StringConstantExpressionAst 的 Value，可选）</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// PS 重定向 DTO — discriminated union，Type 区分 MergingRedirectionAst/FileRedirectionAst
/// <para>Merging 类型仅用 Type 字段；File 类型额外使用 Append/FromStream/LocationText</para>
/// </summary>
public sealed class PsAstRedirectionDto {
    /// <summary>重定向类型（MergingRedirectionAst/FileRedirectionAst）</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>是否追加模式（仅 FileRedirectionAst）</summary>
    [JsonPropertyName("append")]
    public bool Append { get; set; }

    /// <summary>来源流（仅 FileRedirectionAst：Error/All/Output）</summary>
    [JsonPropertyName("fromStream")]
    public string FromStream { get; set; } = string.Empty;

    /// <summary>目标位置文本（仅 FileRedirectionAst）</summary>
    [JsonPropertyName("locationText")]
    public string LocationText { get; set; } = string.Empty;
}

/// <summary>
/// PS 安全模式 DTO — 对应 securityPatterns 嵌套对象
/// </summary>
public sealed class PsAstSecurityPatternsDto {
    /// <summary>是否包含成员调用（.NET 方法调用）</summary>
    [JsonPropertyName("hasMemberInvocations")]
    public bool HasMemberInvocations { get; set; }

    /// <summary>是否包含子表达式 $(...) / @(...) / (...)</summary>
    [JsonPropertyName("hasSubExpressions")]
    public bool HasSubExpressions { get; set; }

    /// <summary>是否包含可展开字符串 "..."</summary>
    [JsonPropertyName("hasExpandableStrings")]
    public bool HasExpandableStrings { get; set; }

    /// <summary>是否包含脚本块 { ... }</summary>
    [JsonPropertyName("hasScriptBlocks")]
    public bool HasScriptBlocks { get; set; }
}

/// <summary>
/// PS AST JSON 序列化上下文 — AOT 安全
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PsAstResultDto))]
internal sealed partial class PsAstJsonContext : JsonSerializerContext;