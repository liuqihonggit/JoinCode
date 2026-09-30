namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 标志验证器实现 — 无独立数据源，纯逻辑验证
/// <para>对齐 TS validateFlags</para>
/// </summary>
internal sealed class FlagValidator : IFlagValidator {
    /// <inheritdoc />
    public bool ValidateFlags(
        IReadOnlyList<string> args,
        FrozenDictionary<string, FlagArgType> safeFlags,
        bool respectsDoubleDash) {
        var i = 0;
        var pastDelimiter = false;

        while (i < args.Count) {
            var arg = args[i];

            if (pastDelimiter) {
                i++;
                continue;
            }

            if (arg == "--") {
                if (!respectsDoubleDash) {
                    return false;
                }

                pastDelimiter = true;
                i++;
                continue;
            }

            if (!arg.StartsWith('-') || arg.Length == 1) {
                i++;
                continue;
            }

            if (arg.StartsWith("--")) {
                var eqIdx = arg.IndexOf('=');
                var flagName = eqIdx >= 0 ? arg[..eqIdx] : arg;

                if (!safeFlags.TryGetValue(flagName, out var flagType)) {
                    return false;
                }

                if (flagType == FlagArgType.Required && eqIdx < 0) {
                    i += 2;
                } else {
                    i++;
                }

                continue;
            }

            for (var j = 1; j < arg.Length; j++) {
                var shortFlag = $"-{arg[j]}";
                if (!safeFlags.TryGetValue(shortFlag, out var flagType)) {
                    return false;
                }

                if (flagType == FlagArgType.Required) {
                    if (j + 1 < arg.Length) {
                        break;
                    }

                    i++;
                    break;
                }
            }

            i++;
        }

        return true;
    }
}
