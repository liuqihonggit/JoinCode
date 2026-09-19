namespace Core.Configuration;

/// <summary>
/// 自定义命令定义 — 用户通过 markdown 文件定义的斜杠命令
/// </summary>
public sealed record CustomCommand {
    /// <summary>命令名称</summary>
    public required string Name { get; init; }
    /// <summary>命令正文模板(可含 $ARGUMENTS 占位符)</summary>
    public required string Content { get; init; }
    /// <summary>命令描述</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>命令来源文件路径</summary>
    public string SourcePath { get; init; } = string.Empty;
    /// <summary>是否禁止模型主动调用此命令</summary>
    public bool DisableModelInvocation { get; init; }
    /// <summary>命令命名空间(来自相对路径的目录层级)</summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// 命令全名 — 含命名空间前缀(形如 "ns:name"),无命名空间时仅返回 Name
    /// </summary>
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";

    /// <summary>
    /// 将参数替换到命令正文的 $ARGUMENTS 占位符
    /// </summary>
    /// <param name="arguments">要替换的参数文本</param>
    /// <returns>替换后的命令正文</returns>
    public string ApplyArguments(string arguments) {
        return Content.Replace("$ARGUMENTS", arguments, StringComparison.Ordinal);
    }
}