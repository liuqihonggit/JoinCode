namespace JoinCode.Abstractions.Interfaces.Scheduling;

public sealed record CronFields {
    /// <summary>获取分钟字段取值数组。</summary>
    public required int[] Minute { get; init; }
    /// <summary>获取小时字段取值数组。</summary>
    public required int[] Hour { get; init; }
    /// <summary>获取月份中的天字段取值数组。</summary>
    public required int[] DayOfMonth { get; init; }
    /// <summary>获取月份字段取值数组。</summary>
    public required int[] Month { get; init; }
    /// <summary>获取星期中的天字段取值数组。</summary>
    public required int[] DayOfWeek { get; init; }
}

internal sealed record FieldRange(int Min, int Max);

public static class CronExpressionParser {
    private static readonly FieldRange[] FieldRanges =
    [
        new FieldRange(0, 59),
        new FieldRange(0, 23),
        new FieldRange(1, 31),
        new FieldRange(1, 12),
        new FieldRange(0, 6)
    ];

    private static readonly Regex StepPattern = new(@"^\*(?:/(\d+))?$", RegexOptions.Compiled);
    private static readonly Regex RangePattern = new(@"^(\d+)-(\d+)(?:/(\d+))?$", RegexOptions.Compiled);
    private static readonly Regex SinglePattern = new(@"^(\d+)$", RegexOptions.Compiled);

    /// <summary>解析 cron 表达式为 CronFields,失败返回 null。</summary>
    public static CronFields? Parse(string expression) {
        var parts = expression.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return null;

        var expanded = new int[5][];
        for (var i = 0; i < 5; i++) {
            var result = ExpandField(parts[i], FieldRanges[i]);
            if (result == null) return null;
            expanded[i] = result;
        }

        return new CronFields {
            Minute = expanded[0],
            Hour = expanded[1],
            DayOfMonth = expanded[2],
            Month = expanded[3],
            DayOfWeek = expanded[4]
        };
    }

    /// <summary>判断 cron 表达式是否合法。</summary>
    public static bool IsValid(string expression) {
        return Parse(expression) != null;
    }

    private static int[]? ExpandField(string field, FieldRange range) {
        var (min, max) = (range.Min, range.Max);
        var result = new HashSet<int>();

        foreach (var part in field.Split(',')) {
            var trimmed = part.Trim();

            var stepMatch = StepPattern.Match(trimmed);
            if (stepMatch.Success) {
                var step = stepMatch.Groups[1].Success ? int.Parse(stepMatch.Groups[1].Value) : 1;
                if (step < 1) return null;
                for (var i = min; i <= max; i += step)
                    result.Add(i);
                continue;
            }

            var rangeMatch = RangePattern.Match(trimmed);
            if (rangeMatch.Success) {
                var lo = int.Parse(rangeMatch.Groups[1].Value);
                var hi = int.Parse(rangeMatch.Groups[2].Value);
                var step = rangeMatch.Groups[3].Success ? int.Parse(rangeMatch.Groups[3].Value) : 1;

                var isDow = min == 0 && max == 6;
                var effMax = isDow ? 7 : max;

                if (lo > hi || step < 1 || lo < min || hi > effMax) return null;

                for (var i = lo; i <= hi; i += step) {
                    result.Add(isDow && i == 7 ? 0 : i);
                }
                continue;
            }

            var singleMatch = SinglePattern.Match(trimmed);
            if (singleMatch.Success) {
                var n = int.Parse(trimmed);
                if (min == 0 && max == 6 && n == 7) n = 0;
                if (n < min || n > max) return null;
                result.Add(n);
                continue;
            }

            return null;
        }

        if (result.Count == 0) return null;
        return result.OrderBy(x => x).ToArray();
    }
}
