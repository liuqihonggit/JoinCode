namespace JoinCode.Abstractions.Models.Git;

/// <summary>
/// Git CLI 子命令枚举 — 替代 GitToolHandlers 中的硬编码命令字符串
/// </summary>
public enum GitSubCommand
{
    [EnumValue("status")] Status,
    [EnumValue("add")] Add,
    [EnumValue("commit")] Commit,
    [EnumValue("push")] Push,
    [EnumValue("pull")] Pull,
    [EnumValue("log")] Log,
    [EnumValue("diff")] Diff,
    [EnumValue("branch")] Branch,
    [EnumValue("switch")] Switch,
    [EnumValue("clone")] Clone,
    [EnumValue("reset")] Reset,
    [EnumValue("clean")] Clean,
    [EnumValue("ls-files")] LsFiles,
    [EnumValue("rev-parse")] RevParse,
    [EnumValue("rev-list")] RevList,
    [EnumValue("sparse-checkout")] SparseCheckout,
}
