namespace JoinCode.Dream;

/// <summary>
/// Dream 插件命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 DreamCliArgParser + DreamCliArgParseResult
/// 参数名引用 JccCliArgConstants（由 JccCliArg 枚举 + [EnumValue] 生成），确保参数名单一数据源
/// </summary>
public enum DreamCliArg
{
    [CliOption(JccCliArgConstants.Help, "-h", "显示帮助")]
    Help,

    [CliOption(JccCliArgConstants.Project, "-p", "项目目录", AcceptsValue = true)]
    Project,

    [CliOption(JccCliArgConstants.Force, "-f", "强制执行")]
    Force,
}
