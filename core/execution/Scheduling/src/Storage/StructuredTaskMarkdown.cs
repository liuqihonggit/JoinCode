
namespace Core.Scheduling;

public sealed class StructuredTaskMarkdownWriter
{
    public static async Task<string> ToMarkdownAsync(IAgentTaskContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine(L.T(StringKey.TaskHeader, context.TaskName));
        sb.AppendLine();
        sb.AppendLine($"- **ID**: {context.TaskId}");
        sb.AppendLine(L.T(StringKey.LabelPriority, context.Priority));
        sb.AppendLine(L.T(StringKey.LabelCreatedAt, context.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
        sb.AppendLine(L.T(StringKey.LabelWorkScope, context.WorkScope));

        if (!string.IsNullOrEmpty(context.ParentTaskId))
        {
            sb.AppendLine(L.T(StringKey.LabelParentTask, context.ParentTaskId));
        }

        sb.AppendLine();
        sb.AppendLine(L.T(StringKey.SectionDescription));
        sb.AppendLine();
        sb.AppendLine(context.Description);
        sb.AppendLine();

        var tasks = await context.GetStructuredTasksAsync().ConfigureAwait(false);
        if (tasks.Count > 0)
        {
            sb.AppendLine(L.T(StringKey.StructuredTaskCount, tasks.Count));
            sb.AppendLine();

            foreach (var task in tasks.OrderBy(t => t.Order))
            {
                var statusIcon = task.Status switch
                {
                    TaskStatusConstants.Completed => "[x]",
                    TaskStatusConstants.InProgress => "[>]",
                    TaskStatusConstants.Failed => "[!]",
                    _ => "[ ]"
                };

                sb.AppendLine(L.T(StringKey.TaskOrderDescription, statusIcon, task.Order, task.Description));
                sb.AppendLine();
                sb.AppendLine(L.T(StringKey.LabelTaskStatus, task.Status));

                if (!string.IsNullOrEmpty(task.Result))
                {
                    sb.AppendLine(L.T(StringKey.LabelTaskResult, task.Result));
                }

                if (task.Possibilities.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(L.T(StringKey.LabelPossibilities));
                    sb.AppendLine();

                    for (var i = 0; i < task.Possibilities.Count; i++)
                    {
                        var p = task.Possibilities[i];
                        var excludePrefix = p.Excluded ? "~~" : "";
                        var excludeSuffix = p.Excluded ? "~~" : "";
                        var reasonSuffix = p.Excluded && !string.IsNullOrEmpty(p.ExclusionReason)
                            ? L.T(StringKey.ExclusionReasonSuffix, p.ExclusionReason)
                            : "";

                        sb.AppendLine($"  {i + 1}. {excludePrefix}{p.Description}{excludeSuffix}{reasonSuffix}");
                    }
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

public sealed class StructuredTaskMarkdownReader
{
    private static readonly string[] StatusPrefixes = ["- **Status**: ", "- **状态**: "];
    private static readonly string[] ResultPrefixes = ["- **Result**: ", "- **结果**: "];
    private static readonly string[] ExclusionReasonPrefixes = ["← Exclusion reason: ", "← 排除原因: "];

    public static List<StructuredTaskEntry> ParseTasks(string markdown)
    {
        var tasks = new List<StructuredTaskEntry>();
        if (string.IsNullOrWhiteSpace(markdown)) return tasks;

        var lines = markdown.Split('\n');
        StructuredTaskEntry? currentTask = null;
        var currentPossibilities = new List<TaskPossibility>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("### "))
            {
                if (currentTask != null)
                {
                    tasks.Add(currentTask with
                    {
                        Possibilities = currentPossibilities.ToList()
                    });
                }

                var headerSpan = line.AsSpan(4);
                var order = ExtractOrder(headerSpan);
                var description = ExtractDescription(headerSpan);

                currentTask = new StructuredTaskEntry
                {
                    Order = order,
                    Description = description
                };
                currentPossibilities = [];
            }
            else if (currentTask != null && line.StartsWith("- **"))
            {
                var statusVal = TryExtractPrefixed(line, StatusPrefixes);
                if (statusVal != null)
                {
                    currentTask = currentTask with { Status = statusVal };
                }
                else
                {
                    var resultVal = TryExtractPrefixed(line, ResultPrefixes);
                    if (resultVal != null)
                    {
                        currentTask = currentTask with { Result = resultVal };
                    }
                }
            }
            else if (currentTask != null && line.TrimStart().Length > 2
                     && char.IsDigit(line.TrimStart()[0])
                     && line.TrimStart().Contains('.'))
            {
                var trimmed = line.AsSpan().TrimStart();
                var dotIdx = trimmed.IndexOf('.');
                if (dotIdx < 0) continue;

                var contentSpan = trimmed[(dotIdx + 1)..].TrimStart();

                var excluded = contentSpan.StartsWith("~~");
                string? exclusionReason = null;

                if (excluded)
                {
                    var endIdx = contentSpan[2..].IndexOf("~~".AsSpan(), StringComparison.Ordinal);
                    if (endIdx >= 0)
                    {
                        endIdx += 2; // 调整切片偏移
                        var afterStrike = contentSpan[(endIdx + 2)..].TrimStart();
                        exclusionReason = TryExtractPrefixedSpan(afterStrike, ExclusionReasonPrefixes);
                    }
                }

                string desc;
                if (excluded)
                {
                    var endIdx = contentSpan[2..].IndexOf("~~".AsSpan(), StringComparison.Ordinal);
                    desc = endIdx >= 0
                        ? contentSpan[2..(endIdx + 2)].Trim().ToString()
                        : contentSpan.Trim().ToString();
                }
                else
                {
                    desc = contentSpan.Trim().ToString();
                }

                currentPossibilities.Add(new TaskPossibility
                {
                    Description = desc,
                    Excluded = excluded,
                    ExclusionReason = exclusionReason
                });
            }
        }

        if (currentTask != null)
        {
            tasks.Add(currentTask with
            {
                Possibilities = currentPossibilities.ToList()
            });
        }

        return tasks;
    }

    private static string? TryExtractPrefixed(string line, string[] prefixes)
    {
        var lineSpan = line.AsSpan();
        foreach (var prefix in prefixes)
        {
            if (lineSpan.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
            {
                return lineSpan[prefix.Length..].Trim().ToString();
            }
        }
        return null;
    }

    private static int ExtractOrder(ReadOnlySpan<char> header)
    {
        var colonIdx = header.IndexOf(':');
        if (colonIdx < 0) return 0;

        var beforeColon = header[..colonIdx];
        var spaceIdx = beforeColon.LastIndexOf(' ');
        var orderSpan = spaceIdx >= 0 ? beforeColon[(spaceIdx + 1)..] : beforeColon;

        return int.TryParse(orderSpan, out var order) ? order : 0;
    }

    private static string ExtractDescription(ReadOnlySpan<char> header)
    {
        var colonIdx = header.IndexOf(':');
        return colonIdx >= 0 ? header[(colonIdx + 1)..].Trim().ToString() : header.Trim().ToString();
    }

    private static string? TryExtractPrefixedSpan(ReadOnlySpan<char> line, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (line.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim().ToString();
            }
        }
        return null;
    }
}
