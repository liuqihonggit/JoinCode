namespace JoinCode.CliCommands;

/// <summary>
/// config-print 元命令 — 打印合并后的完整配置树（settings.json + providers_llm/ + providers_search/ + current/）。
/// <para>用于验证配置树加载是否正确 — TASK054 P8 手动验收工具。</para>
/// </summary>
internal static class ConfigPrintCommand {
    /// <summary>
    /// 执行 config-print — [--json]（默认 JSON 输出，--format text 输出缩进 JSON 纯文本）。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var unknownError = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames);
        if (unknownError is not null) {
            TerminalHelper.WriteError(unknownError);
            return 1;
        }

        var fs = IO.FileSystem.FileSystemFactory.Create();
        var settings = await Core.Configuration.SettingsLoader.LoadAllSourcesAsync(fs, null, null, ct).ConfigureAwait(false);
        var json = RelaxedJsonSerializer.SerializeIndented(settings, Core.Configuration.ConfigIndentedJsonContext.Default);
        System.Console.WriteLine(json);
        return 0;
    }
}
