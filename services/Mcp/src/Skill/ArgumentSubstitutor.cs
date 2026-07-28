namespace McpClient;

/// <summary>
/// 参数替换器 — 对齐 TS argumentSubstitution.ts substituteArguments
/// 支持 $ARGUMENTS/$ARGUMENTS[N]/$N/命名参数/${CLAUDE_SKILL_DIR}/${CLAUDE_SESSION_ID}
/// </summary>
public sealed class ArgumentSubstitutor
{
    /// <summary>
    /// 替换内容中的参数占位符 — 对齐 TS substituteArguments
    /// 优先级: 命名参数 → $ARGUMENTS[N] → $N → $ARGUMENTS → ${CLAUDE_SKILL_DIR} → ${CLAUDE_SESSION_ID}
    /// </summary>
    public string Substitute(
        string content,
        string? args,
        IReadOnlyList<string>? argumentNames = null,
        string? skillDirectory = null,
        string? sessionId = null,
        bool appendIfNoPlaceholder = true)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var result = content;
        var parsedArgs = ParseShellArgs(args);
        var hasPlaceholder = false;

        if (argumentNames is { Count: > 0 } && parsedArgs.Count > 0)
        {
            for (var i = 0; i < argumentNames.Count && i < parsedArgs.Count; i++)
            {
                var name = argumentNames[i];
                var value = parsedArgs[i];
                var pattern = $@"\${Regex.Escape(name)}(?![\[\w])";
                var replaced = Regex.Replace(result, pattern, value);
                if (replaced != result)
                {
                    hasPlaceholder = true;
                    result = replaced;
                }
            }
        }

        var indexedPattern = @"\$ARGUMENTS\[(\d+)\]";
        result = Regex.Replace(result, indexedPattern, match =>
        {
            hasPlaceholder = true;
            if (int.TryParse(match.Groups[1].Value, out var idx) && idx < parsedArgs.Count)
                return parsedArgs[idx];
            return string.Empty;
        });

        var shorthandPattern = @"\$(\d+)(?!\w)";
        result = Regex.Replace(result, shorthandPattern, match =>
        {
            hasPlaceholder = true;
            if (int.TryParse(match.Groups[1].Value, out var idx) && idx < parsedArgs.Count)
                return parsedArgs[idx];
            return string.Empty;
        });

        if (result.Contains("$ARGUMENTS"))
        {
            hasPlaceholder = true;
            result = result.Replace("$ARGUMENTS", args ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(skillDirectory) && result.Contains("${CLAUDE_SKILL_DIR}"))
        {
            hasPlaceholder = true;
            var normalizedPath = skillDirectory.Replace('\\', '/');
            result = result.Replace("${CLAUDE_SKILL_DIR}", normalizedPath);
        }

        if (!string.IsNullOrEmpty(sessionId) && result.Contains("${CLAUDE_SESSION_ID}"))
        {
            hasPlaceholder = true;
            result = result.Replace("${CLAUDE_SESSION_ID}", sessionId);
        }

        if (!hasPlaceholder && appendIfNoPlaceholder && !string.IsNullOrWhiteSpace(args))
        {
            result = $"{result}\n\nARGUMENTS: {args}";
        }

        return result;
    }

    internal static List<string> ParseShellArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return [];

        var result = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];

            if (inSingleQuote)
            {
                if (c == '\'')
                {
                    inSingleQuote = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (inDoubleQuote)
            {
                if (c == '"')
                {
                    inDoubleQuote = false;
                }
                else if (c == '\\' && i + 1 < args.Length)
                {
                    current.Append(args[++i]);
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '\'')
                {
                    inSingleQuote = true;
                }
                else if (c == '"')
                {
                    inDoubleQuote = true;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
