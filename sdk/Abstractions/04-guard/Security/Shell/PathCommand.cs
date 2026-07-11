namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径约束命令枚举 — 对齐 TS pathValidation.ts PathCommand 联合类型
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 ToValue/FromValue 映射
/// </summary>
public enum PathCommand
{
    [EnumValue("cd")] Cd,
    [EnumValue("ls")] Ls,
    [EnumValue("find")] Find,
    [EnumValue("mkdir")] Mkdir,
    [EnumValue("touch")] Touch,
    [EnumValue("rm")] Rm,
    [EnumValue("rmdir")] Rmdir,
    [EnumValue("mv")] Mv,
    [EnumValue("cp")] Cp,
    [EnumValue("cat")] Cat,
    [EnumValue("head")] Head,
    [EnumValue("tail")] Tail,
    [EnumValue("sort")] Sort,
    [EnumValue("uniq")] Uniq,
    [EnumValue("wc")] Wc,
    [EnumValue("cut")] Cut,
    [EnumValue("paste")] Paste,
    [EnumValue("column")] Column,
    [EnumValue("tr")] Tr,
    [EnumValue("file")] File,
    [EnumValue("stat")] Stat,
    [EnumValue("diff")] Diff,
    [EnumValue("awk")] Awk,
    [EnumValue("strings")] Strings,
    [EnumValue("hexdump")] Hexdump,
    [EnumValue("od")] Od,
    [EnumValue("base64")] Base64,
    [EnumValue("nl")] Nl,
    [EnumValue("grep")] Grep,
    [EnumValue("rg")] Rg,
    [EnumValue("sed")] Sed,
    [EnumValue("git")] Git,
    [EnumValue("jq")] Jq,
    [EnumValue("sha256sum")] Sha256sum,
    [EnumValue("sha1sum")] Sha1sum,
    [EnumValue("md5sum")] Md5sum,
}

/// <summary>
/// 文件操作类型 — 对齐 TS pathValidation.ts FileOperationType
/// 统一枚举: 合并原 JoinCode.Abstractions.Interfaces.FileOperationType (Edit) 和 PsFileOperationType (Create)
/// 同时用于 RecordFileMetrics 的 operation 参数
/// </summary>
public enum FileOperationType
{
    /// <summary>
    /// 读取操作
    /// </summary>
    [EnumValue("read")] Read,

    /// <summary>
    /// 写入操作（修改/删除已有文件）
    /// </summary>
    [EnumValue("write")] Write,

    /// <summary>
    /// 创建操作（新建文件/目录）
    /// </summary>
    [EnumValue("create")] Create,

    /// <summary>
    /// 编辑操作（原地修改文件内容）
    /// </summary>
    [EnumValue("edit")] Edit,

    /// <summary>
    /// 删除操作
    /// </summary>
    [EnumValue("delete")] Delete,

    /// <summary>
    /// 列表操作（目录列表）
    /// </summary>
    [EnumValue("list")] List,

    /// <summary>
    /// 正则编辑操作
    /// </summary>
    [EnumValue("edit_regex")] EditRegex,

    /// <summary>
    /// 插入行操作
    /// </summary>
    [EnumValue("insert_lines")] InsertLines,

    /// <summary>
    /// 删除行操作
    /// </summary>
    [EnumValue("delete_lines")] DeleteLines,

    /// <summary>
    /// 批量编辑操作
    /// </summary>
    [EnumValue("batch_edit")] BatchEdit,

    /// <summary>
    /// 裁剪行操作
    /// </summary>
    [EnumValue("snip_lines")] SnipLines,

    /// <summary>
    /// 裁剪预览操作
    /// </summary>
    [EnumValue("snip_preview")] SnipPreview,

    /// <summary>
    /// 行范围编辑操作
    /// </summary>
    [EnumValue("edit_line_range")] EditLineRange,

    /// <summary>
    /// 复制操作
    /// </summary>
    [EnumValue("copy")] Copy,

    /// <summary>
    /// 移动操作
    /// </summary>
    [EnumValue("move")] Move,
}

/// <summary>
/// 文件操作结果 — RecordFileMetrics 的 result 参数枚举化
/// </summary>
public enum FileOperationResult
{
    [EnumValue("ok")] Ok,
    [EnumValue("failed")] Failed,
    [EnumValue("rejected")] Rejected,
    [EnumValue("stale")] Stale,
    [EnumValue("token_exceeded")] TokenExceeded,
    [EnumValue("partial")] Partial,
    [EnumValue("resize_failed")] ResizeFailed,
    [EnumValue("api_limit_exceeded")] ApiLimitExceeded,
    [EnumValue("pdf_failed")] PdfFailed,
    [EnumValue("notebook_failed")] NotebookFailed,
}
