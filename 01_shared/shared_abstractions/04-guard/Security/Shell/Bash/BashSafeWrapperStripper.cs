namespace JoinCode.Abstractions.Security.Shell;

public static class BashSafeWrapperStripper
{
    public static string[] StripSafeWrappers(string[] argv)
    {
        var a = argv;
        for (; ; )
        {
            if (a.Length == 0) break;

            switch (a[0])
            {
                case "time":
                case "nohup":
                    a = a[1..];
                    break;
                case "timeout":
                    {
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg is "--foreground" or "--preserve-status" or "--verbose") i++;
                            else if (arg.StartsWith("--kill-after=") || arg.StartsWith("--signal=")) i++;
                            else if ((arg is "--kill-after" or "--signal") && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith("--")) return a;
                            else if (arg == "-v") i++;
                            else if ((arg is "-k" or "-s") && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith("-k") || arg.StartsWith("-s")) i++;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i < a.Length && BashSecurityRegex.DurationRegex().IsMatch(a[i])) a = a[(i + 1)..];
                        else if (i < a.Length) return a;
                        else break;
                    }
                    break;
                case "nice":
                    {
                        if (a.Length > 2 && a[1] == "-n" && BashSecurityRegex.NiceNumRegex().IsMatch(a[2])) a = a[3..];
                        else if (a.Length > 1 && BashSecurityRegex.NiceLegacyRegex().IsMatch(a[1])) a = a[2..];
                        else if (a.Length > 1 && a[1].Contains('$')) return a;
                        else if (a.Length > 1) a = a[1..];
                        else break;
                    }
                    break;
                case "env":
                    {
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg.Contains('=') && !arg.StartsWith('-')) i++;
                            else if (arg is "-i" or "-0" or "-v") i++;
                            else if (arg == "-u" && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i < a.Length) a = a[i..];
                        else break;
                    }
                    break;
                case "stdbuf":
                    {
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (BashSecurityRegex.StdbufShortSepRegex().IsMatch(arg) && i + 1 < a.Length) i += 2;
                            else if (BashSecurityRegex.StdbufShortFusedRegex().IsMatch(arg)) i++;
                            else if (BashSecurityRegex.StdbufLongRegex().IsMatch(arg)) i++;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i > 1 && i < a.Length) a = a[i..];
                        else break;
                    }
                    break;
                default:
                    goto Done;
            }
        }
    Done:
        return a;
    }

    public static string StripSafeWrappersString(string command, FrozenSet<string> safeEnvVars)
    {
        var result = command.TrimStart();

        while (result.Length > 0 && IsEnvVarAssignment(result))
        {
            var spaceIdx = result.IndexOf(' ');
            if (spaceIdx < 0) break;

            var varPart = result[..spaceIdx];
            var eqIdx = varPart.IndexOf('=');
            if (eqIdx < 0) break;

            var varName = varPart[..eqIdx];
            if (!safeEnvVars.Contains(varName)) break;

            result = result[(spaceIdx + 1)..].TrimStart();
        }

        var wrappers = new[] { "timeout", "time", "nice", "nohup", "stdbuf", "env" };
        foreach (var wrapper in wrappers)
        {
            if (result.StartsWith(wrapper + " ", StringComparison.OrdinalIgnoreCase))
            {
                result = result[(wrapper.Length + 1)..].TrimStart();
            }
        }

        return result;
    }

    private static bool IsEnvVarAssignment(string s)
    {
        var eqIdx = s.IndexOf('=');
        if (eqIdx <= 0) return false;

        for (var i = 0; i < eqIdx; i++)
        {
            var c = s[i];
            if (i == 0 && !char.IsLetter(c) && c != '_') return false;
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }
}
