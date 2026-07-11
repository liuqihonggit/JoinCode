namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS AST 解析器 — spawn pwsh 子进程调用 Parser.ParseInput，解析 JSON 输出
/// 对齐 TS: parser.ts — 同样的架构，进程外解析，AOT 兼容
/// </summary>
public static partial class PsAstParser
{
    private static readonly string ParseScriptBody = BuildParseScript();
    private static string? _cachedPwshPath;
    private static readonly Lock CacheLock = new();

    private const int MaxCacheSize = 256;
    private static readonly ConcurrentDictionary<string, PsParsedCommand> ParseCache = new(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TransientErrorIds = FrozenSet.ToFrozenSet(
        ["PwshSpawnError", "PwshError", "PwshTimeout", "EmptyOutput", "InvalidJson", "ProcessStartFailed", "ProcessTimeout", "ParseException"],
        StringComparer.Ordinal);

    /// <summary>
    /// 解析 PS 命令为结构化结果
    /// </summary>
    public static PsParsedCommand Parse(string command, IProcessService? processService = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new PsParsedCommand
            {
                Valid = false,
                OriginalCommand = command,
            };
        }

        if (ParseCache.TryGetValue(command, out var cached))
        {
            return cached;
        }

        var result = ParseCore(command, processService);

        if (ShouldCache(result))
        {
            if (ParseCache.Count >= MaxCacheSize)
            {
                ParseCache.Clear();
            }
            ParseCache[command] = result;
        }

        return result;
    }

    private static bool ShouldCache(PsParsedCommand result)
    {
        if (result.Valid) return true;
        if (result.Errors is { Length: > 0 } && TransientErrorIds.Contains(result.Errors[0].ErrorId))
        {
            return false;
        }
        return true;
    }

    private static PsParsedCommand ParseCore(string command, IProcessService? processService = null)
    {
        var pwshPath = FindPwshPath();
        if (pwshPath is null)
        {
            return new PsParsedCommand
            {
                Valid = false,
                OriginalCommand = command,
                Errors = [new PsParseError { Message = "PowerShell not found", ErrorId = "NoPwsh" }],
            };
        }

        try
        {
            var encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
            var scriptEncoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ParseScriptBody));

            if (processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = pwshPath,
                    Arguments = $"-NoProfile -NonInteractive -NoLogo -EncodedCommand {scriptEncoded}",
                    TimeoutMs = 5000,
                    EnvironmentVariables = new Dictionary<string, string>
                    {
                        ["EncodedCommand"] = encodedCommand
                    }
                };

                var result = processService.ExecuteAsync(options).GetAwaiter().GetResult();

                if (result.ExitCode == -1)
                {
                    return new PsParsedCommand
                    {
                        Valid = false,
                        OriginalCommand = command,
                        Errors = [new PsParseError { Message = "pwsh parse process timed out", ErrorId = "ProcessTimeout" }],
                    };
                }

                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    return new PsParsedCommand
                    {
                        Valid = false,
                        OriginalCommand = command,
                        Errors = [new PsParseError { Message = $"pwsh parse process failed: {result.StandardError}", ErrorId = "ProcessFailed" }],
                    };
                }

                return DeserializeParsedCommand(result.StandardOutput, command);
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = pwshPath,
                Arguments = $"-NoProfile -NonInteractive -NoLogo -EncodedCommand {scriptEncoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.EnvironmentVariables["EncodedCommand"] = encodedCommand;

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return new PsParsedCommand
                {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = "Failed to start pwsh process", ErrorId = "ProcessStartFailed" }],
                };
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (!process.HasExited)
            {
                process.Kill();
                return new PsParsedCommand
                {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = "pwsh parse process timed out", ErrorId = "ProcessTimeout" }],
                };
            }

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return new PsParsedCommand
                {
                    Valid = false,
                    OriginalCommand = command,
                    Errors = [new PsParseError { Message = $"pwsh parse process failed: {stderr}", ErrorId = "ProcessFailed" }],
                };
            }

            return DeserializeParsedCommand(stdout, command);
        }
        catch (Exception ex)
        {
            return new PsParsedCommand
            {
                Valid = false,
                OriginalCommand = command,
                Errors = [new PsParseError { Message = ex.Message, ErrorId = "ParseException" }],
            };
        }
    }

    /// <summary>
    /// 获取所有命令元素（跨所有语句、管道段、嵌套命令）
    /// </summary>
    public static List<PsCommandElement> GetAllCommands(PsParsedCommand parsed)
    {
        var commands = new List<PsCommandElement>();

        foreach (var stmt in parsed.Statements)
        {
            commands.AddRange(stmt.Commands);
            commands.AddRange(stmt.NestedCommands);
        }

        return commands;
    }

    /// <summary>
    /// 获取所有命令名（小写，用于大小写不敏感匹配）
    /// </summary>
    public static List<string> GetAllCommandNames(PsParsedCommand parsed)
    {
        var names = new List<string>();
        foreach (var cmd in GetAllCommands(parsed))
        {
            names.Add(cmd.Name.ToLowerInvariant());
        }
        return names;
    }

    /// <summary>
    /// 检查是否存在指定名称的命令（支持别名解析）
    /// </summary>
    public static bool HasCommandNamed(PsParsedCommand parsed, string name)
    {
        var lowerName = name.ToLowerInvariant();
        foreach (var cmdName in GetAllCommandNames(parsed))
        {
            if (cmdName == lowerName) return true;

            if (PsAliases.TryResolve(cmdName, out var canonical) &&
                canonical.Equals(lowerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (PsAliases.TryResolve(lowerName, out var canonical2) &&
                canonical2.Equals(cmdName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 获取所有变量引用
    /// </summary>
    public static List<PsVariable> GetVariables(PsParsedCommand parsed)
    {
        return [.. parsed.Variables];
    }

    /// <summary>
    /// 按作用域过滤变量（如 "env" 过滤 $env:PATH）
    /// </summary>
    public static List<PsVariable> GetVariablesByScope(PsParsedCommand parsed, string scope)
    {
        var prefix = scope.ToLowerInvariant() + ":";
        return parsed.Variables.Where(v => v.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 推导安全标志 — 从解析结果中提取
    /// </summary>
    public static PsSecurityFlags DeriveSecurityFlags(PsParsedCommand parsed)
    {
        bool hasSubExpr = false, hasScriptBlocks = false, hasSplatting = false;
        bool hasExpandableStrings = false, hasMemberInvocations = false, hasAssignments = false;

        foreach (var stmt in parsed.Statements)
        {
            if (stmt.SecurityPatterns is not null)
            {
                if (stmt.SecurityPatterns.HasSubExpressions) hasSubExpr = true;
                if (stmt.SecurityPatterns.HasScriptBlocks) hasScriptBlocks = true;
                if (stmt.SecurityPatterns.HasExpandableStrings) hasExpandableStrings = true;
                if (stmt.SecurityPatterns.HasMemberInvocations) hasMemberInvocations = true;
            }

            foreach (var cmd in stmt.Commands)
            {
                foreach (var et in cmd.ElementTypes)
                {
                    switch (et)
                    {
                        case PsElementType.SubExpression: hasSubExpr = true; break;
                        case PsElementType.ScriptBlock: hasScriptBlocks = true; break;
                        case PsElementType.ExpandableString: hasExpandableStrings = true; break;
                        case PsElementType.MemberInvocation: hasMemberInvocations = true; break;
                    }
                }
            }

            if (stmt.StatementType is "AssignmentStatementAst") hasAssignments = true;

            foreach (var v in parsed.Variables)
            {
                if (v.IsSplatted) hasSplatting = true;
            }
        }

        return new PsSecurityFlags
        {
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
    public static bool CommandHasArgAbbreviation(PsCommandElement cmd, string fullParam, string minPrefix)
    {
        var lowerFull = fullParam.ToLowerInvariant();
        var lowerMin = minPrefix.ToLowerInvariant();

        foreach (var arg in cmd.Args)
        {
            var colonIdx = arg.IndexOf(':', 1);
            var paramPart = colonIdx > 0 ? arg[..colonIdx] : arg;

            var lower = paramPart.Replace("`", "").ToLowerInvariant();

            if (lower.StartsWith(lowerMin) &&
                lowerFull.StartsWith(lower) &&
                lower.Length <= lowerFull.Length)
            {
                return true;
            }
        }
        return false;
    }

    private static readonly FrozenSet<char> AltParamPrefixes = FrozenSet.ToFrozenSet(
        ['/', '\u2013', '\u2014', '\u2015']);

    public static bool PsHasParamAbbreviation(PsCommandElement cmd, string fullParam, string minPrefix)
    {
        if (CommandHasArgAbbreviation(cmd, fullParam, minPrefix))
        {
            return true;
        }

        var normalizedArgs = cmd.Args.Select(a =>
            a.Length > 0 && AltParamPrefixes.Contains(a[0]) ? "-" + a[1..] : a).ToArray();

        var normalizedCmd = new PsCommandElement
        {
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
    public static bool IsPowerShellExecutable(string name)
    {
        var lower = name.ToLowerInvariant();
        if (PsExecutableNames.Contains(lower)) return true;

        var lastSep = Math.Max(lower.LastIndexOf('/'), lower.LastIndexOf('\\'));
        if (lastSep >= 0)
        {
            return PsExecutableNames.Contains(lower[(lastSep + 1)..]);
        }
        return false;
    }

    private static readonly FrozenSet<string> PsExecutableNames = FrozenSet.ToFrozenSet(
        ["pwsh", "pwsh.exe", "powershell", "powershell.exe"],
        StringComparer.OrdinalIgnoreCase);

    private static string? FindPwshPath()
    {
        lock (CacheLock)
        {
            if (_cachedPwshPath is not null) return _cachedPwshPath;
        }

        string? found = null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separators = new[] { ';' };
        foreach (var dir in pathEnv.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                foreach (var name in new[] { "pwsh.exe", "pwsh" })
                {
                    var fullPath = Path.Combine(dir.Trim(), name);
                    if (Path.Exists(fullPath))
                    {
                        found = fullPath;
                        break;
                    }
                }
            }
            catch (IOException) { continue; }
            if (found is not null) break;
        }

        if (found is not null)
        {
            lock (CacheLock)
            {
                _cachedPwshPath = found;
            }
        }

        return found;
    }

    private static PsParsedCommand DeserializeParsedCommand(string json, string originalCommand)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var errors = new List<PsParseError>();
            if (root.TryGetProperty("errors", out var errorsElem))
            {
                foreach (var e in errorsElem.EnumerateArray())
                {
                    errors.Add(new PsParseError
                    {
                        Message = e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                        ErrorId = e.TryGetProperty("errorId", out var id) ? id.GetString() ?? "" : "",
                    });
                }
            }

            var typeLiterals = new List<string>();
            if (root.TryGetProperty("typeLiterals", out var tlElem))
            {
                foreach (var tl in tlElem.EnumerateArray())
                {
                    var val = tl.GetString();
                    if (val is not null) typeLiterals.Add(val);
                }
            }

            var variables = new List<PsVariable>();
            if (root.TryGetProperty("variables", out var varsElem))
            {
                foreach (var v in varsElem.EnumerateArray())
                {
                    variables.Add(new PsVariable(
                        v.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                        v.TryGetProperty("isSplatted", out var sp) && sp.GetBoolean()));
                }
            }

            var statements = new List<PsStatement>();
            if (root.TryGetProperty("statements", out var stmtsElem))
            {
                foreach (var s in stmtsElem.EnumerateArray())
                {
                    statements.Add(DeserializeStatement(s));
                }
            }

            return new PsParsedCommand
            {
                Valid = root.TryGetProperty("valid", out var vElem) && vElem.GetBoolean(),
                OriginalCommand = originalCommand,
                Errors = [.. errors],
                HasStopParsing = root.TryGetProperty("hasStopParsing", out var hsp) && hsp.GetBoolean(),
                TypeLiterals = [.. typeLiterals],
                HasUsingStatements = root.TryGetProperty("hasUsingStatements", out var hu) && hu.GetBoolean(),
                HasScriptRequirements = root.TryGetProperty("hasScriptRequirements", out var hr) && hr.GetBoolean(),
                Statements = [.. statements],
                Variables = [.. variables],
            };
        }
        catch
        {
            return new PsParsedCommand
            {
                Valid = false,
                OriginalCommand = originalCommand,
                Errors = [new PsParseError { Message = "Failed to deserialize parse output", ErrorId = "DeserializationFailed" }],
            };
        }
    }

    private static PsStatement DeserializeStatement(JsonElement s)
    {
        var commands = new List<PsCommandElement>();
        var nestedCommands = new List<PsCommandElement>();
        var redirections = new List<PsRedirection>();

        var stmtType = s.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        if (s.TryGetProperty("elements", out var elems))
        {
            foreach (var elem in elems.EnumerateArray())
            {
                var cmd = DeserializeCommandFromElement(elem);
                if (cmd is not null) commands.Add(cmd);
            }
        }

        if (s.TryGetProperty("nestedCommands", out var nested))
        {
            foreach (var nc in nested.EnumerateArray())
            {
                var cmd = DeserializeCommandFromElement(nc);
                if (cmd is not null) nestedCommands.Add(cmd);
            }
        }

        if (s.TryGetProperty("redirections", out var redirs))
        {
            foreach (var r in redirs.EnumerateArray())
            {
                redirections.Add(DeserializeRedirection(r));
            }
        }

        PsSecurityPatterns? securityPatterns = null;
        if (s.TryGetProperty("securityPatterns", out var sp))
        {
            securityPatterns = new PsSecurityPatterns
            {
                HasMemberInvocations = sp.TryGetProperty("hasMemberInvocations", out var mi) && mi.GetBoolean(),
                HasSubExpressions = sp.TryGetProperty("hasSubExpressions", out var se) && se.GetBoolean(),
                HasExpandableStrings = sp.TryGetProperty("hasExpandableStrings", out var es) && es.GetBoolean(),
                HasScriptBlocks = sp.TryGetProperty("hasScriptBlocks", out var sb) && sb.GetBoolean(),
            };
        }

        return new PsStatement
        {
            StatementType = stmtType,
            Commands = [.. commands],
            NestedCommands = [.. nestedCommands],
            Redirections = [.. redirections],
            Text = s.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
            SecurityPatterns = securityPatterns,
        };
    }

    private static PsCommandElement? DeserializeCommandFromElement(JsonElement elem)
    {
        var elemType = elem.TryGetProperty("type", out var et) ? et.GetString() ?? "" : "";

        if (elemType != "CommandAst") return null;

        var commandElements = new List<(string Text, string Type, string? Value)>();
        if (elem.TryGetProperty("commandElements", out var ce))
        {
            foreach (var c in ce.EnumerateArray())
            {
                commandElements.Add((
                    c.TryGetProperty("text", out var ct) ? ct.GetString() ?? "" : "",
                    c.TryGetProperty("type", out var ctype) ? ctype.GetString() ?? "" : "",
                    c.TryGetProperty("value", out var cv) ? cv.GetString() : null
                ));
            }
        }

        if (commandElements.Count == 0) return null;

        var rawName = commandElements[0].Value ?? commandElements[0].Text;
        rawName = StripQuotes(rawName);
        rawName = StripModulePrefix(rawName);

        var nameType = ClassifyCommandName(rawName);
        var name = rawName;

        var args = new List<string>();
        var elementTypes = new List<PsElementType>();

        elementTypes.Add(MapElementTypeFromRaw(commandElements[0].Type));

        for (var i = 1; i < commandElements.Count; i++)
        {
            args.Add(commandElements[i].Value ?? commandElements[i].Text);
            elementTypes.Add(MapElementTypeFromRaw(commandElements[i].Type));
        }

        var redirections = new List<PsRedirection>();
        if (elem.TryGetProperty("redirections", out var redirs))
        {
            foreach (var r in redirs.EnumerateArray())
            {
                redirections.Add(DeserializeRedirection(r));
            }
        }

        return new PsCommandElement
        {
            Name = name,
            NameType = nameType,
            Args = [.. args],
            ElementTypes = [.. elementTypes],
            Text = elem.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
            Redirections = [.. redirections],
        };
    }

    private static PsElementType MapElementTypeFromRaw(string rawType)
    {
        return rawType switch
        {
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

    private static PsRedirection DeserializeRedirection(JsonElement r)
    {
        var rType = r.TryGetProperty("type", out var rt) ? rt.GetString() ?? "" : "";

        if (rType == "MergingRedirectionAst")
        {
            return new PsRedirection("2>&1", "", true);
        }

        if (rType == "FileRedirectionAst")
        {
            var append = r.TryGetProperty("append", out var a) && a.GetBoolean();
            var fromStream = r.TryGetProperty("fromStream", out var fs) ? fs.GetString() ?? "" : "";
            var target = r.TryGetProperty("locationText", out var lt) ? lt.GetString() ?? "" : "";

            var op = (append, fromStream) switch
            {
                (true, "Error") => "2>>",
                (true, "All") => "*>>",
                (true, _) => ">>",
                (false, "Error") => "2>",
                (false, "All") => "*>",
                (false, _) => ">",
            };

            return new PsRedirection(op, target, false);
        }

        return new PsRedirection(">", "", false);
    }

    private static string StripQuotes(string name)
    {
        if (name.Length >= 2)
        {
            var first = name[0];
            var last = name[^1];
            if ((first == '\'' || first == '"') && first == last)
            {
                return name[1..^1];
            }
        }
        return name;
    }

    private static string StripModulePrefix(string name)
    {
        var idx = name.LastIndexOf('\\');
        if (idx < 0) return name;

        if (name.Length >= 2 && name[1] == ':') return name;
        if (name.StartsWith("\\\\")) return name;
        if (name.StartsWith(".\\")) return name;
        if (name.StartsWith("..\\")) return name;

        return name[(idx + 1)..];
    }

    private static PsCommandNameType ClassifyCommandName(string name)
    {
        if (name.Any(c => c > 0x7F))
        {
            return PsCommandNameType.Application;
        }

        if (Regex.IsMatch(name, @"^[A-Za-z]+-[A-Za-z][A-Za-z0-9_]*$"))
        {
            return PsCommandNameType.Cmdlet;
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains('.'))
        {
            return PsCommandNameType.Application;
        }

        return PsCommandNameType.Unknown;
    }

    private static string BuildParseScript()
    {
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
