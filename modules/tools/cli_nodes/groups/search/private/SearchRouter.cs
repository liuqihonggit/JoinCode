// search 组合并路由 — 先查已有文本搜索工具，再查自建项目图工具。
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Search;

internal static class SearchRouter {
    public static CliNodeHandler? Find(string toolId) =>
        SearchCommands.Find(toolId) ?? ProjectGraphCommands.Find(toolId);

    public static IEnumerable<string> LocalToolIds =>
        SearchCommands.LocalToolIds.Concat(ProjectGraphCommands.LocalToolIds)
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
}
