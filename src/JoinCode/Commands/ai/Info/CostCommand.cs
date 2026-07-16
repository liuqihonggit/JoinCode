
namespace JoinCode.ChatCommands;

/// <summary>
/// /cost 命令 - 显示成本统计
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Cost, Description = "显示使用成本统计", Usage = "/cost [today|session|total]", Category = ChatCommandCategory.Model, ArgumentHint = "[today|session|total]")]
public sealed class CostCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services.CostTracker is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}成本追踪器不可用。{AnsiStyleConstants.Reset}");
            return Task.FromResult(ChatCommandResult.Continue());
        }

        var args = ChatCommandBase.GetSplitArgs(context);
        var scope = args.Length > 0 ? args[0].ToLowerInvariant() : "session";

        CostStatistics stats;

        switch (scope)
        {
            case CostScopeConstants.Today:
                stats = context.Services.CostTracker.GetTodayStatistics();
                break;
            case CostScopeConstants.Total:
                stats = context.Services.CostTracker.GetTotalStatistics();
                break;
            case CostScopeConstants.Session:
            default:
                stats = context.Services.CostTracker.GetSessionStatistics(context.SessionId);
                break;
        }

        var output = FormatTotalCost(stats);
        TerminalHelper.WriteLine(output);

        return Task.FromResult(ChatCommandResult.Continue());
    }

    internal static string FormatTotalCost(CostStatistics stats)
    {
        var costDisplay = FormatCost(stats.TotalCostUsd);
        if (stats.HasUnknownModelCost)
        {
            costDisplay += " (costs may be inaccurate due to usage of unknown models)";
        }

        var sb = new StringBuilder();
        sb.Append($"{TerminalColors.Inactive.ToAnsiFg()}");
        sb.AppendLine($"Total cost:            {costDisplay}");
        sb.AppendLine($"Total duration (API):  {DurationFormatter.Format(stats.ApiDuration)}");
        sb.AppendLine($"Total duration (wall): {DurationFormatter.Format(stats.WallDuration)}");
        sb.AppendLine($"Total code changes:    {stats.LinesAdded} {(stats.LinesAdded == 1 ? "line" : "lines")} added, {stats.LinesRemoved} {(stats.LinesRemoved == 1 ? "line" : "lines")} removed");

        if (stats.ModelBreakdown.Count > 0)
        {
            sb.AppendLine(FormatModelUsage(stats.ModelBreakdown));
        }
        else
        {
            sb.AppendLine("Usage:                 0 input, 0 output, 0 cache read, 0 cache write");
        }

        sb.Append(AnsiStyleConstants.Reset);
        return sb.ToString();
    }

    internal static string FormatCost(decimal cost)
    {
        return cost >= 0.5m ? $"${Math.Round((double)cost, 2):F2}" : $"${cost:F4}";
    }

    internal static string FormatModelUsage(List<ModelCostStatistics> modelBreakdown)
    {
        var usageByShortName = new Dictionary<string, ModelCostStatistics>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in modelBreakdown)
        {
            var shortName = ModelNameHelper.GetCanonicalName(model.Model);
            if (!usageByShortName.TryGetValue(shortName, out var existing))
            {
                usageByShortName[shortName] = new ModelCostStatistics
                {
                    Model = shortName,
                    RequestCount = model.RequestCount,
                    PromptTokens = model.PromptTokens,
                    CompletionTokens = model.CompletionTokens,
                    CacheCreationTokens = model.CacheCreationTokens,
                    CacheReadTokens = model.CacheReadTokens,
                    TotalCost = model.TotalCost
                };
            }
            else
            {
                usageByShortName[shortName] = new ModelCostStatistics
                {
                    Model = shortName,
                    RequestCount = existing.RequestCount + model.RequestCount,
                    PromptTokens = existing.PromptTokens + model.PromptTokens,
                    CompletionTokens = existing.CompletionTokens + model.CompletionTokens,
                    CacheCreationTokens = existing.CacheCreationTokens + model.CacheCreationTokens,
                    CacheReadTokens = existing.CacheReadTokens + model.CacheReadTokens,
                    TotalCost = existing.TotalCost + model.TotalCost
                };
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("Usage by model:");
        foreach (var kvp in usageByShortName)
        {
            var usage = kvp.Value;
            var usageString =
                $"{usage.PromptTokens:N0} input, " +
                $"{usage.CompletionTokens:N0} output, " +
                $"{usage.CacheReadTokens:N0} cache read, " +
                $"{usage.CacheCreationTokens:N0} cache write" +
                $" ({FormatCost(usage.TotalCost)})";
            sb.AppendLine($"{kvp.Key,-22}{usageString}");
        }
        return sb.ToString();
    }

}
