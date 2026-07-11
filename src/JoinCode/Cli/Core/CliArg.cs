namespace JoinCode;

/// <summary>
/// 命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 CliArgParser + CliArgParseResult
/// </summary>
public enum CliArg
{
    [CliOption("--help", "-h", "显示帮助信息")]
    Help,

    [CliOption("--version", "-v", "显示版本信息")]
    Version,

    [CliOption("--pipe", "", "命名管道通信", AcceptsValue = true)]
    Pipe,

    [CliOption("--prompt", "-p", "非交互模式提示词", AcceptsValue = true)]
    Prompt,

    [CliOption("--model", "-m", "指定模型", AcceptsValue = true)]
    Model,

    [CliOption("--non-interactive", "", "强制非交互模式")]
    NonInteractive,

    [CliOption("--trust", "", "自动信任工作目录")]
    Trust,

    [CliOption("--brief", "", "简要模式")]
    Brief,

    [CliOption("--force-interactive", "", "强制交互模式（即使stdin重定向也启用REPL，用于E2E测试）")]
    ForceInteractive,

    [CliOption("--await", "", "超时自动关闭秒数（超时返回1234，用于测试诊断卡死）", AcceptsValue = true)]
    Await,
}
