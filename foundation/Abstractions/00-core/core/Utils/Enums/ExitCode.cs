namespace JoinCode.Abstractions.Utils;

/// <summary>
/// jcc 进程退出码 — 分段预留，方便定位错误类型。
/// 遵循 POSIX 约定：0=成功，1-125=自定义错误，128+N=信号终止。
/// 新增退出码时挑选对应分段的空位，禁止复用已有值。
/// </summary>
public enum ExitCode
{
    /// <summary>成功</summary>
    Success = 0,

    // ── 1-9: 通用与配置错误 ──

    /// <summary>通用错误（未分类异常）</summary>
    GeneralError = 1,

    /// <summary>配置错误（ConfigurationException）— 配置文件/环境变量问题，用户可自行修复</summary>
    ConfigurationError = 2,

    /// <summary>参数解析错误 — 命令行参数格式无效</summary>
    ArgumentParseError = 3,

    /// <summary>API Key 缺失或无效 — 需要配置认证信息</summary>
    ApiKeyMissing = 4,

    /// <summary>会话恢复失败 — 会话文件损坏或不存在</summary>
    SessionResumeFailed = 5,

    // ── 10-19: 运行时错误 ──

    /// <summary>LLM 调用失败 — API 请求错误、速率限制、模型不可用等</summary>
    LlmCallFailed = 10,

    /// <summary>工具执行失败 — 工具调用抛出异常或返回错误</summary>
    ToolExecutionFailed = 11,

    /// <summary>MCP 连接失败 — MCP 服务器连接/握手失败</summary>
    McpConnectionFailed = 12,

    /// <summary>子进程异常退出 — 病人进程/Bridge 子进程崩溃</summary>
    SubprocessCrashed = 13,

    // ── 12xx: 超时与卡死（按类型细分，看到退出码即可定位是哪种超时） ──

    /// <summary>--await 诊断超时强制退出 — 历史契约值，CI/E2E 脚本依赖此值</summary>
    AwaitTimeout = 1234,

    /// <summary>LLM 调用超时 — API 请求超过时限未响应</summary>
    LlmCallTimeout = 1240,

    /// <summary>工具执行超时 — 工具运行超过时限（如 Bash 命令卡死）</summary>
    ToolExecutionTimeout = 1241,

    /// <summary>MCP 连接超时 — MCP 服务器连接/握手超过时限</summary>
    McpConnectionTimeout = 1242,

    /// <summary>子进程超时 — 病人/Bridge 子进程超过时限未退出</summary>
    SubprocessTimeout = 1243,

    /// <summary>流式响应超时 — SSE/streaming 连接超过时限无数据</summary>
    StreamResponseTimeout = 1244,

    // ── 128+N: 信号终止（POSIX 约定） ──

    /// <summary>用户中断 Ctrl+C（128 + SIGINT=2）— POSIX 标准，便于 shell 脚本区分中断与正常错误</summary>
    Interrupted = 130,
}
