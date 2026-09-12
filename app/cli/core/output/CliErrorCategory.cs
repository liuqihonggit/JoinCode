namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 错误分类 — 对齐架构指南5类退出码语义
/// 与 ExitCode 枚举并存：ExitCode 是细粒度退出值，CliErrorCategory 是逻辑分组
/// </summary>
public enum CliErrorCategory
{
    /// <summary>成功 (0)</summary>
    Success = 0,

    /// <summary>参数错误 (1) — 命令行参数格式无效、缺少必需参数</summary>
    ArgumentError = 1,

    /// <summary>认证失败 (2) — API Key 缺失/无效、Token 过期</summary>
    AuthError = 2,

    /// <summary>资源未找到 (3) — 会话不存在、工具未注册</summary>
    NotFound = 3,

    /// <summary>临时失败/可重试 (4) — 网络超时、速率限制、MCP 连接失败</summary>
    Transient = 4,

    /// <summary>冲突/不可重试 (5) — 权限拒绝、工作目录未信任</summary>
    Conflict = 5,
}
