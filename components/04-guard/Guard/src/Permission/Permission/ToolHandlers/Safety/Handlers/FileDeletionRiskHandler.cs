
namespace Core.Permission;

/// <summary>
/// 文件删除风险处理器 — CommandRisk.FileDeletion 的拦截策略
/// Auto 模式: 拒绝 + 引导使用 Move-Item 移动到 .xxx/ 目录
/// Ask 模式: 提示确认 + 建议移动到 .xxx/ 目录
/// </summary>
[Register(typeof(ICommandRiskHandler))]
public sealed partial class FileDeletionRiskHandler : ICommandRiskHandler
{
    private const string TrashDir = ".xxx";

    /// <inheritdoc />
    public CommandRisk RiskType => CommandRisk.FileDeletion;

    /// <inheritdoc />
    public string BuildRejectionMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            var trashPath = BuildTrashPath(targetPath);
            return $"文件删除操作已被阻止（{context.Details}）。请使用 Shell 工具将文件移动到 {TrashDir}/ 目录: Move-Item '{targetPath}' '{trashPath}'";
        }

        return $"文件删除操作已被阻止（{context.Details}）。请使用 Shell 工具将文件移动到 {TrashDir}/ 目录，格式: .xxx/{{原文件名}}.{{原后缀}}.{{时间戳}}.del";
    }

    /// <inheritdoc />
    public string BuildConfirmationMessage(CommandRiskContext context)
    {
        var targetPath = context.ReferencedPaths.FirstOrDefault();

        if (targetPath is not null)
        {
            var trashPath = BuildTrashPath(targetPath);
            return $"工具 '{context.ToolName}' 请求删除文件 '{targetPath}'（{context.Details}）。建议使用 Shell 工具移动到 {TrashDir}/ 目录: Move-Item '{targetPath}' '{trashPath}'。是否允许删除？";
        }

        return $"工具 '{context.ToolName}' 请求删除文件（{context.Details}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
    }

    /// <summary>
    /// 构建回收路径 — 格式: .xxx/{原文件名}.{原后缀}.{时间戳}.del
    /// </summary>
    internal static string BuildTrashPath(string originalPath)
    {
        var fileName = Path.GetFileName(originalPath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var dotIndex = fileName.IndexOf('.', StringComparison.Ordinal);

        var trashFileName = dotIndex > 0
            ? $"{fileName[..dotIndex]}.{fileName[(dotIndex + 1)..]}.{timestamp}.del"
            : $"{fileName}.{timestamp}.del";

        return Path.Combine(TrashDir, trashFileName);
    }
}
