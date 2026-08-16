namespace JoinCode;

/// <summary>
/// 命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 CliArgParser + CliArgParseResult
/// 参数名引用 JccCliArgConstants（由 JccCliArg 枚举 + [EnumValue] 生成），确保参数名单一数据源
/// </summary>
public enum CliArg
{
    [CliOption(JccCliArgConstants.Help, "-h", "显示帮助信息", Category = "基础")]
    Help,

    [CliOption(JccCliArgConstants.Version, "-v", "显示版本信息", Category = "基础")]
    Version,

    [CliOption(JccCliArgConstants.Pipe, "", "命名管道通信", AcceptsValue = true, Category = "基础")]
    Pipe,

    [CliOption(JccCliArgConstants.Prompt, "-p", "非交互模式提示词", AcceptsValue = true, Category = "基础", Example = "jcc -p \"解释这段代码\"")]
    Prompt,

    [CliOption(JccCliArgConstants.Model, "-m", "指定模型", AcceptsValue = true, Category = "基础", Example = "jcc -m gpt-4o -p \"hello\"")]
    Model,

    [CliOption(JccCliArgConstants.Vendor, "", "切换供应商（自动匹配 settings.json vendor 预设中的同名配置）", AcceptsValue = true, Category = "基础", Example = "jcc --vendor agnes -p \"hello\"")]
    Vendor,

    [CliOption(JccCliArgConstants.NonInteractive, "", "强制非交互模式", Category = "输出")]
    NonInteractive,

    [CliOption(JccCliArgConstants.NoConfirm, "", "跳过所有确认提示（AI 驱动用，走交互模式但不弹确认框）", Category = "权限")]
    NoConfirm,

    [CliOption(JccCliArgConstants.Trust, "", "自动信任工作目录", Category = "权限", Example = "jcc --trust -p \"hello\"")]
    Trust,

    [CliOption(JccCliArgConstants.Brief, "", "简要模式", Category = "输出")]
    Brief,

    [CliOption(JccCliArgConstants.ForceInteractive, "", "强制交互模式（即使stdin重定向也启用REPL，用于E2E测试）", Category = "诊断")]
    ForceInteractive,

    [CliOption(JccCliArgConstants.Tui, "", "启用TUI模式（Terminal.Gui渲染层，见docs/plans/TUI架构与消息管道重构设计.md）", Category = "输出")]
    Tui,

    [CliOption(JccCliArgConstants.Await, "", "超时自动关闭秒数（超时返回 AwaitTimeout=1234，用于测试诊断卡死）", AcceptsValue = true, Category = "诊断", Example = "jcc --await 20 -p \"hello\"")]
    Await,

    [CliOption(JccCliArgConstants.DebugLog, "-d", "启用调试日志输出（[WIRE] [STEP] [READY] 等，等效于 JCC_DEBUGLOG=1）", Category = "诊断")]
    DebugLog,

    [CliOption(JccCliArgConstants.Continue, "-c", "继续最近的会话（自动选择上次会话）", Category = "会话", Example = "jcc -c")]
    Continue,

    [CliOption(JccCliArgConstants.Resume, "-r", "恢复指定会话（按 session-id 或标题关键字）", AcceptsValue = true, Category = "会话", Example = "jcc -r abc123")]
    Resume,

    [CliOption(JccCliArgConstants.PermissionMode, "", "设置权限模式 (plan/auto/ask/bypass)", AcceptsValue = true, Category = "权限", RiskLevel = "write")]
    PermissionMode,

    [CliOption(JccCliArgConstants.DangerouslySkipPermissions, "", "跳过所有权限检查（等价于 --permission-mode bypass，仅在受信任环境使用）", Category = "权限", RiskLevel = "dangerous")]
    DangerouslySkipPermissions,

    [CliOption(JccCliArgConstants.AllowedTools, "", "工具白名单（逗号分隔，如 'Read,Edit,Bash(git:*)'），仅这些工具可用", AcceptsValue = true, Category = "权限")]
    AllowedTools,

    [CliOption(JccCliArgConstants.DisallowedTools, "", "工具黑名单（逗号分隔），这些工具被禁用", AcceptsValue = true, Category = "权限")]
    DisallowedTools,

    [CliOption(JccCliArgConstants.SystemPrompt, "", "替换系统提示词（完全覆盖默认系统提示词）", AcceptsValue = true, Category = "提示词")]
    SystemPrompt,

    [CliOption(JccCliArgConstants.AppendSystemPrompt, "", "追加系统提示词（在默认/已加载系统提示词后附加，不覆盖）", AcceptsValue = true, Category = "提示词")]
    AppendSystemPrompt,

    [CliOption(JccCliArgConstants.Doctor, "", "医生模式：spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题", Category = "医生")]
    Doctor,

    [CliOption(JccCliArgConstants.DoctorServer, "", "医生服务器模式：监听病人 SSE 连接，支持 1:N 多病人监控（需配合 --doctor）", Category = "医生")]
    DoctorServer,

    [CliOption(JccCliArgConstants.DoctorEndpoint, "", "医生 SSE 端点 URL（病人端使用，连接到医生的 SSE 服务器，如 http://localhost:9902）", AcceptsValue = true, Category = "医生")]
    DoctorEndpoint,

    [CliOption(JccCliArgConstants.DoctorPort, "", "医生 SSE 服务器端口（医生端使用，默认 9902）", AcceptsValue = true, Category = "医生")]
    DoctorPort,

    [CliOption(JccCliArgConstants.Json, "", "结构化 JSON 输出模式（子命令和非交互模式生效，交互模式保持彩色输出）", Category = "输出", Example = "jcc tool list --json")]
    Json,

    [CliOption(JccCliArgConstants.Format, "", "输出格式 (text/json/ndjson)，默认 text", AcceptsValue = true, Category = "输出")]
    Format,

    [CliOption(JccCliArgConstants.DryRun, "", "试跑模式：只显示将要执行的操作，不实际执行", Category = "权限", RiskLevel = "read", Example = "jcc --dry-run -p \"删除文件\"")]
    DryRun,

    [CliOption(JccCliArgConstants.Yes, "-y", "跳过所有确认提示（等价于 --no-confirm，对齐架构指南 AX 模式）", Category = "权限", Example = "jcc -y -p \"hello\"")]
    Yes,

    [CliOption(JccCliArgConstants.Force, "", "强制执行：跳过权限检查和确认（等价于 --dangerously-skip-permissions 的轻量版）", Category = "权限", RiskLevel = "dangerous")]
    Force,

    [CliOption(JccCliArgConstants.Quiet, "-q", "静默模式：只输出错误信息，抑制正常输出", Category = "输出")]
    Quiet,

    [CliOption(JccCliArgConstants.Print, "", "非交互模式（从stdin读取，输出到stdout）", Category = "内部")]
    Print,

    [CliOption(JccCliArgConstants.SdkUrl, "", "SDK服务URL", AcceptsValue = true, Category = "内部")]
    SdkUrl,

    [CliOption(JccCliArgConstants.InputFormat, "", "输入格式", AcceptsValue = true, Category = "内部")]
    InputFormat,

    [CliOption(JccCliArgConstants.OutputFormat, "", "输出格式", AcceptsValue = true, Category = "内部")]
    OutputFormat,

    [CliOption(JccCliArgConstants.ReplayUserMessages, "", "重放用户消息", Category = "内部")]
    ReplayUserMessages,
}
