namespace MockServer.E2E.Tests.Core;

public sealed class PrefixCacheAnalyzer
{
    private readonly IFileSystem _fs;

    public PrefixCacheAnalyzer(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public PrefixCacheAnalysis Analyze(IReadOnlyList<string> dumpFiles)
    {
        ArgumentNullException.ThrowIfNull(dumpFiles);

        if (dumpFiles.Count < 2)
        {
            return new PrefixCacheAnalysis
            {
                AllPrefixesStable = true,
                AdjacentPairs = [],
                Breaks = []
            };
        }

        var sorted = dumpFiles
            .Select(f => new { Path = f, Name = System.IO.Path.GetFileName(f) })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pairs = new List<DumpFilePair>();
        var breaks = new List<CacheBreakDetail>();

        for (var i = 0; i < sorted.Count - 1; i++)
        {
            var earlier = sorted[i];
            var later = sorted[i + 1];

            var earlierTurn = ParseTurnIndex(earlier.Name);
            var laterTurn = ParseTurnIndex(later.Name);

            var earlierContent = _fs.ReadAllText(earlier.Path);
            var laterContent = _fs.ReadAllText(later.Path);

            var (stable, reason) = ComparePrefix(earlierContent, laterContent);

            var pair = new DumpFilePair
            {
                EarlierFile = earlier.Path,
                LaterFile = later.Path,
                EarlierTurn = earlierTurn,
                LaterTurn = laterTurn,
                PrefixStable = stable,
                BreakReason = reason
            };
            pairs.Add(pair);

            if (!stable)
            {
                breaks.Add(new CacheBreakDetail
                {
                    FromTurn = earlierTurn,
                    ToTurn = laterTurn,
                    Reason = reason ?? "Unknown"
                });
            }
        }

        return new PrefixCacheAnalysis
        {
            AdjacentPairs = pairs,
            AllPrefixesStable = breaks.Count == 0,
            Breaks = breaks
        };
    }

    private static (bool Stable, string? Reason) ComparePrefix(string earlierContent, string laterContent)
    {
        var earlierRounds = ParseRounds(earlierContent);
        var laterRounds = ParseRounds(laterContent);

        if (earlierRounds.Count == 0)
        {
            return (true, null);
        }

        if (laterRounds.Count < earlierRounds.Count)
        {
            return (false, $"Later file has fewer rounds ({laterRounds.Count}) than earlier ({earlierRounds.Count})");
        }

        for (var i = 0; i < earlierRounds.Count; i++)
        {
            var earlierRound = earlierRounds[i];
            var laterRound = laterRounds[i];

            if (!string.Equals(earlierRound.UserInput, laterRound.UserInput, StringComparison.Ordinal))
            {
                var earlierPreview = Truncate(earlierRound.UserInput, 60);
                var laterPreview = Truncate(laterRound.UserInput, 60);
                return (false, $"Round {i + 1} UserInput changed: \"{earlierPreview}\" -> \"{laterPreview}\"");
            }

            if (!string.Equals(earlierRound.AssistantResponse, laterRound.AssistantResponse, StringComparison.Ordinal))
            {
                var earlierPreview = Truncate(earlierRound.AssistantResponse, 60);
                var laterPreview = Truncate(laterRound.AssistantResponse, 60);
                return (false, $"Round {i + 1} AssistantResponse changed: \"{earlierPreview}\" -> \"{laterPreview}\"");
            }

            if (earlierRound.ToolCalls.Count != laterRound.ToolCalls.Count)
            {
                return (false, $"Round {i + 1} ToolCalls count changed: {earlierRound.ToolCalls.Count} -> {laterRound.ToolCalls.Count}");
            }

            for (var j = 0; j < earlierRound.ToolCalls.Count; j++)
            {
                var etc = earlierRound.ToolCalls[j];
                var ltc = laterRound.ToolCalls[j];

                if (!string.Equals(etc.ToolName, ltc.ToolName, StringComparison.Ordinal))
                {
                    return (false, $"Round {i + 1} ToolCall[{j}] name changed: \"{etc.ToolName}\" -> \"{ltc.ToolName}\"");
                }
            }
        }

        return (true, null);
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        return s.Length <= maxLen ? s : s[..maxLen] + "...";
    }

    private static List<ParsedRound> ParseRounds(string content)
    {
        var rounds = new List<ParsedRound>();
        var sections = content.Split(["--- 第"], StringSplitOptions.None);

        for (var i = 1; i < sections.Length; i++)
        {
            var section = sections[i];
            var round = new ParsedRound();

            var userInput = ExtractSection(section, "[User]", "[Assistant]", "[Tool]", "[Errors]");
            round.UserInput = userInput.Trim();

            var assistantStart = section.IndexOf("[Assistant]", StringComparison.OrdinalIgnoreCase);
            if (assistantStart >= 0)
            {
                var afterAssistant = section[(assistantStart + "[Assistant]".Length)..];
                var endMarkers = new[] { "[Errors]", "=== 当前轮原始输出 ===", "--- 第" };
                var endIdx = int.MaxValue;
                foreach (var marker in endMarkers)
                {
                    var idx = afterAssistant.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && idx < endIdx) endIdx = idx;
                }
                round.AssistantResponse = (endIdx < int.MaxValue ? afterAssistant[..endIdx] : afterAssistant).Trim();
            }

            var toolStart = section.IndexOf("[Tool]", StringComparison.OrdinalIgnoreCase);
            if (toolStart >= 0)
            {
                var toolSection = section[toolStart..];
                var toolLines = toolSection.Split('\n');
                foreach (var line in toolLines)
                {
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.StartsWith("[Tool]", StringComparison.OrdinalIgnoreCase))
                    {
                        var toolName = trimmed["[Tool] ".Length..].Split('(')[0].Trim();
                        round.ToolCalls.Add(new ParsedToolCall { ToolName = toolName });
                    }
                }
            }

            rounds.Add(round);
        }

        return rounds;
    }

    private static string ExtractSection(string content, string startMarker, params string[] endMarkers)
    {
        var startIdx = content.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return "";

        var afterStart = content[(startIdx + startMarker.Length)..];
        var endIdx = int.MaxValue;
        foreach (var marker in endMarkers)
        {
            var idx = afterStart.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < endIdx) endIdx = idx;
        }

        return endIdx < int.MaxValue ? afterStart[..endIdx] : afterStart;
    }

    private static int ParseTurnIndex(string fileName)
    {
        var turnIdx = fileName.IndexOf("turn_", StringComparison.OrdinalIgnoreCase);
        if (turnIdx < 0) return 0;

        var rest = fileName.AsSpan(turnIdx + 5);
        var dotIdx = rest.IndexOf('.');
        var numSpan = dotIdx >= 0 ? rest[..dotIdx] : rest;

        return int.TryParse(numSpan, out var turn) ? turn : 0;
    }

    private sealed class ParsedRound
    {
        public string UserInput { get; set; } = "";
        public string AssistantResponse { get; set; } = "";
        public List<ParsedToolCall> ToolCalls { get; } = [];
    }

    private sealed class ParsedToolCall
    {
        public string ToolName { get; set; } = "";
    }
}
