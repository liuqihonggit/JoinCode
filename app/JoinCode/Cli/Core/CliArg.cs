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

    [CliOption("--verbose", "", "启用诊断输出（[WIRE] [STEP] [READY] 等，等效于 JCC_VERBOSE=1）")]
    Verbose,

    [CliOption("--continue", "-c", "继续最近的会话（自动选择上次会话）")]
    Continue,

    [CliOption("--resume", "-r", "恢复指定会话（按 session-id 或标题关键字）", AcceptsValue = true)]
    Resume,

    [CliOption("--permission-mode", "", "设置权限模式 (default/plan/auto/ask/deny/acceptEdits/bypassPermissions)", AcceptsValue = true)]
    PermissionMode,

    [CliOption("--dangerously-skip-permissions", "", "跳过所有权限检查（等价于 --permission-mode bypassPermissions，仅在受信任环境使用）")]
    DangerouslySkipPermissions,

    [CliOption("--allowed-tools", "", "工具白名单（逗号分隔，如 'Read,Edit,Bash(git:*)'），仅这些工具可用", AcceptsValue = true)]
    AllowedTools,

    [CliOption("--disallowed-tools", "", "工具黑名单（逗号分隔），这些工具被禁用", AcceptsValue = true)]
    DisallowedTools,

    [CliOption("--system-prompt", "", "替换系统提示词（完全覆盖默认系统提示词）", AcceptsValue = true)]
    SystemPrompt,

    [CliOption("--append-system-prompt", "", "追加系统提示词（在默认/已加载系统提示词后附加，不覆盖）", AcceptsValue = true)]
    AppendSystemPrompt,

    [CliOption("--doctor", "", "医生模式：spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题")]
    Doctor,

    [CliOption("--doctor-server", "", "医生服务器模式：监听病人 SSE 连接，支持 1:N 多病人监控（需配合 --doctor）")]
    DoctorServer,

    [CliOption("--doctor-endpoint", "", "医生 SSE 端点 URL（病人端使用，连接到医生的 SSE 服务器，如 http://localhost:9902）", AcceptsValue = true)]
    DoctorEndpoint,

    [CliOption("--doctor-port", "", "医生 SSE 服务器端口（医生端使用，默认 9902）", AcceptsValue = true)]
    DoctorPort,

    [CliOption("--doctor-test-suite", "", "医生测试套件模式：执行内置功能测试用例（T001-T006），验证 jcc.exe 各项能力")]
    DoctorTestSuite,
}
