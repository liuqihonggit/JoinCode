namespace JoinCode.Dream;

/// <summary>
/// Dream 插件命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 DreamCliArgParser + DreamCliArgParseResult
/// </summary>
public enum DreamCliArg
{
    [CliOption("--help", "-h", "显示帮助")]
    Help,

    [CliOption("--project", "-p", "项目目录", AcceptsValue = true)]
    Project,

    [CliOption("--force", "-f", "强制执行")]
    Force,
}
