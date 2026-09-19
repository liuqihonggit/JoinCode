
namespace JoinCode.ChatCommands;

/// <summary>
/// /release-notes 命令 — 查看版本发布说明
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.ReleaseNotes, Description = "查看版本发布说明", Usage = "/release-notes [version]", Category = ChatCommandCategory.Info, ArgumentHint = "[version]", IsHidden = true)]
[ChatCommandArg("version", Type = "string", Description = "要查看的版本号,省略则显示最近 5 个版本的发布说明")]
public sealed class ReleaseNotesCommand : ChatCommandBase {
    /// <summary>
    /// 执行发布说明命令,从发布说明服务获取最近 5 个版本并输出到终端
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数与取消令牌</param>
    /// <returns>表示命令执行结果的任务,始终返回 Continue 以继续会话</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var releaseNotesService = ChatCommandBase.GetService<IReleaseNotesService>(context);
        var version = ChatCommandBase.GetNormalizedArgs(context);
        var currentVersion = typeof(ReleaseNotesCommand).Assembly.GetName().Version?.ToString() ?? "unknown";

        TerminalHelper.WriteLine($"发布说明 (当前版本: {currentVersion})");
        TerminalHelper.NewLine();

        if (releaseNotesService is null) {
            if (!string.IsNullOrEmpty(version))
                TerminalHelper.WriteLine($"查看版本 {version} 的发布说明");

            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
                TerminalHelper.WriteLine("发布说明服务未初始化");
            }
            TerminalHelper.WriteLine("请访问 GitHub Releases 页面查看完整发布说明");
            return ChatCommandResult.Continue();
        }

        try {
            var releases = await releaseNotesService.GetRecentReleasesAsync(5, context.CancellationToken).ConfigureAwait(false);

            if (releases.Count == 0) {
                TerminalHelper.WriteLine("暂无发布说明数据");
                TerminalHelper.WriteLine("请访问 GitHub Releases 页面查看完整发布说明");
                return ChatCommandResult.Continue();
            }

            foreach (var release in releases) {
                TerminalHelper.WriteLine($"Version {release.Version}:");
                var lines = release.Notes.Split('\n');
                foreach (var line in lines) {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        TerminalHelper.WriteLine($"  · {trimmed}");
                }
                TerminalHelper.WriteLine($"  发布于: {release.PublishedAt:yyyy-MM-dd}");
                TerminalHelper.NewLine();
            }
        } catch (Exception ex) {
            ChatCommandBase.HandleError("获取发布说明", ex);
            TerminalHelper.WriteLine("请访问 GitHub Releases 页面查看完整发布说明");
        }

        return ChatCommandResult.Continue();
    }
}