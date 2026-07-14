namespace Structura.Primitives;

/// <summary>
/// 通用 Shell 重定向信息 — 统一 Bash/Ps 重定向类型
/// </summary>
public sealed record ShellRedirect(
    string Operator,
    string Target,
    int? Fd = null,
    bool IsMerging = false);
