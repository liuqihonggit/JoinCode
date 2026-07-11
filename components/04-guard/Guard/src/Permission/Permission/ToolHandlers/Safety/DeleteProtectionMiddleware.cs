
namespace Core.Permission;

/// <summary>
/// 删除操作检测器接口 — 可扩展的删除操作检测机制
/// 实现此接口以支持不同工具类型的删除操作检测（file_delete、Shell rm/del、PowerShell Remove-Item 等）
/// </summary>
public interface IDeleteOperationDetector
{
    /// <summary>
    /// 检测当前工具调用是否为删除操作
    /// </summary>
    /// <returns>删除操作信息，如果不是删除操作则返回 null</returns>
    DeleteOperationInfo? Detect(string toolName, Dictionary<string, JsonElement>? arguments);
}

/// <summary>
/// 删除操作信息 — 描述检测到的删除操作详情
/// </summary>
public sealed record DeleteOperationInfo
{
    /// <summary>
    /// 被删除的目标路径（可能为 null，如 Shell 命令中路径无法提取）
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// 删除操作来源描述（如 "file_delete 工具"、"Shell rm 命令"）
    /// </summary>
    public required string SourceDescription { get; init; }

    /// <summary>
    /// 建议的替代命令（如 "Move-Item 'path' '.xxx/path.timestamp.del'"）
    /// </summary>
    public string? SuggestedAlternative { get; init; }
}

/// <summary>
/// 删除保护中间件 — 拦截文件删除操作，防止不可逆的数据丢失
/// Auto 模式: 拒绝删除操作，返回引导提示让 AI 使用 move 命令移动到 .xxx/ 目录
/// Ask 模式: 返回待确认，提示用户选择允许删除或移动到 .xxx/ 目录
/// Plan 模式: 拒绝删除操作
/// 通过 IDeleteOperationDetector 集合支持多种工具类型的删除检测
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class DeleteProtectionMiddleware : IPermissionMiddleware
{
    private const string TrashDir = ".xxx";

    private readonly IReadOnlyList<IDeleteOperationDetector> _detectors;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 创建 DeleteProtectionMiddleware
    /// </summary>
    public DeleteProtectionMiddleware(IEnumerable<IDeleteOperationDetector>? detectors = null)
    {
        _detectors = detectors?.ToList() ?? [];
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        var deleteInfo = DetectDeleteOperation(context);

        if (deleteInfo is null)
            return next(context, ct);

        var trashPath = deleteInfo.TargetPath is not null
            ? BuildTrashPath(deleteInfo.TargetPath)
            : null;

        switch (context.CurrentMode)
        {
            case PermissionMode.Auto:
            case PermissionMode.Default:
                var autoHint = trashPath is not null && deleteInfo.TargetPath is not null
                    ? $"文件删除操作已被阻止（{deleteInfo.SourceDescription}）。请使用 Shell 工具将文件移动到 {TrashDir}/ 目录: Move-Item '{deleteInfo.TargetPath}' '{trashPath}'"
                    : $"文件删除操作已被阻止（{deleteInfo.SourceDescription}）。请使用 Shell 工具将文件移动到 {TrashDir}/ 目录，格式: .xxx/{{原文件名}}.{{原后缀}}.{{时间戳}}.del";
                context.Result = ToolPermissionCheckResult.Rejected(autoHint);
                return Task.CompletedTask;

            case PermissionMode.Ask:
                var askPrompt = trashPath is not null && deleteInfo.TargetPath is not null
                    ? $"工具 '{context.ToolName}' 请求删除文件 '{deleteInfo.TargetPath}'（{deleteInfo.SourceDescription}）。建议使用 Shell 工具移动到 {TrashDir}/ 目录: Move-Item '{deleteInfo.TargetPath}' '{trashPath}'。是否允许删除？"
                    : $"工具 '{context.ToolName}' 请求删除文件（{deleteInfo.SourceDescription}）。建议移动到 {TrashDir}/ 目录而非直接删除。是否允许删除？";
                context.Result = ToolPermissionCheckResult.PendingConfirmation(askPrompt);
                return Task.CompletedTask;

            case PermissionMode.Plan:
                context.Result = ToolPermissionCheckResult.Rejected(
                    $"Plan 模式下禁止文件删除操作（{deleteInfo.SourceDescription}）。请使用 Shell 工具将文件移动到 {TrashDir}/ 目录");
                return Task.CompletedTask;

            default:
                return next(context, ct);
        }
    }

    /// <summary>
    /// 使用检测器集合检测删除操作
    /// </summary>
    private DeleteOperationInfo? DetectDeleteOperation(PermissionCheckContext context)
    {
        for (var i = 0; i < _detectors.Count; i++)
        {
            var info = _detectors[i].Detect(context.ToolName, context.Arguments);
            if (info is not null)
                return info;
        }

        return null;
    }

    /// <summary>
    /// 构建回收路径 — 格式: .xxx/{原文件名}.{原后缀}.{时间戳}.del
    /// </summary>
    private static string BuildTrashPath(string originalPath)
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
