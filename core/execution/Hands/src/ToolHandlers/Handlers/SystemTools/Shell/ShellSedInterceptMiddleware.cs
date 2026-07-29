namespace Tools.Shell;

/// <summary>
/// Shell sed 编辑拦截中间件 — 对齐 TS SedEditPermissionRequest 预览-确认-应用流程
/// 首次 sed -i 返回预览，存储预计算结果；二次调用确认后写入
/// </summary>
[Register]
public sealed partial class ShellSedInterceptMiddleware : IShellMiddleware
{
    [Inject] private readonly IFileSystem? _fs;

    /// <summary>
    /// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
    /// 首次 sed -i 返回预览，存储预计算结果；二次调用确认后写入
    /// key: 文件路径, value: (新内容, 创建时间)
    /// </summary>
    private static readonly ConcurrentDictionary<string, PendingSedConfirmation> _pendingSedEdits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// sed 确认窗口 — 60s 内有效
    /// </summary>
    private static readonly TimeSpan SedConfirmationWindow = TimeSpan.FromSeconds(60);

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var sedEditInfo = SedEditParser.ParseSedEditCommand(context.Command);
        if (sedEditInfo is not null)
        {
            var result = await HandleSedEditAsync(sedEditInfo, context.WorkingDirectory, ct).ConfigureAwait(false);
            context.SedResult = result;
            context.Result = result;
            return; // 短路
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理 sed -i 编辑 — 对齐 TS SedEditPermissionRequest 预览-确认-应用流程
    /// 首次调用：解析→读取→模拟替换→返回diff预览→存储待确认编辑
    /// 二次调用（确认）：从待确认中取出→直接写入预计算内容
    /// </summary>
    private async Task<ToolResult> HandleSedEditAsync(SedEditInfo sedInfo, string? workingDirectory, CancellationToken cancellationToken)
    {
        if (_fs is null)
        {
            return ResultBuilder.Error().WithText("sed -i requires file system access but IFileSystem is not available").Build();
        }

        var filePath = sedInfo.FilePath;

        // 解析相对路径
        if (!Path.IsPathRooted(filePath))
        {
            var cwd = workingDirectory ?? _fs.GetCurrentDirectory();
            filePath = Path.Combine(cwd, filePath);
        }

        // 二次调用确认：检查是否有待确认的编辑 — 对齐 TS _simulatedSedEdit
        if (_pendingSedEdits.TryGetValue(filePath, out var pending) && !pending.IsExpired)
        {
            // 验证 sed 信息匹配（防止模型伪造不同编辑）
            if (pending.SedPattern == sedInfo.Pattern && pending.SedReplacement == sedInfo.Replacement)
            {
                _pendingSedEdits.TryRemove(filePath, out _);

                // 直接写入预计算的新内容 — 对齐 TS applySedEdit
                try
                {
                    var lineEnding = pending.OriginalLineEnding;
                    var normalizedNewContent = pending.NewContent.Replace("\n", lineEnding);
                    await _fs.WriteAllTextAsync(filePath, normalizedNewContent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return ResultBuilder.Error().WithText($"Failed to write file: {ex.Message}").Build();
                }

                return ResultBuilder.Success().WithText($"Applied sed substitution to {sedInfo.FilePath}").Build();
            }

            // sed 信息不匹配，清除旧的 pending 并重新预览
            _pendingSedEdits.TryRemove(filePath, out _);
        }

        // 清除过期的 pending edits
        foreach (var kvp in _pendingSedEdits)
        {
            if (kvp.Value.IsExpired) _pendingSedEdits.TryRemove(kvp.Key, out _);
        }

        // 首次调用：读取文件→模拟替换→返回预览
        if (!_fs.FileExists(filePath))
        {
            return ResultBuilder.Error().WithText($"File not found: {sedInfo.FilePath}").Build();
        }

        string oldContent;
        string originalLineEnding;
        try
        {
            var rawContent = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            originalLineEnding = rawContent.Contains("\r\n") ? "\r\n" : "\n";
            oldContent = rawContent.Replace("\r\n", "\n");
        }
        catch (Exception ex)
        {
            return ResultBuilder.Error().WithText($"Failed to read file: {ex.Message}").Build();
        }

        // 模拟替换
        var newContent = SedEditParser.ApplySedSubstitution(oldContent, sedInfo);

        // 检查是否有变更
        if (oldContent == newContent)
        {
            var noChangeMsg = string.IsNullOrEmpty(oldContent)
                ? "File is empty, pattern did not match"
                : "Pattern did not match any content";
            return ResultBuilder.Success().WithText(noChangeMsg).Build();
        }

        // 存储待确认编辑 — 对齐 TS _simulatedSedEdit 注入
        _pendingSedEdits[filePath] = new PendingSedConfirmation(
            newContent,
            originalLineEnding,
            sedInfo.Pattern,
            sedInfo.Replacement);

        // 返回 diff 预览 — 对齐 TS SedEditPermissionRequest 展示 FileEditToolDiff
        var preview = new StringBuilder();
        preview.AppendLine($"Sed edit preview for {sedInfo.FilePath}:");
        preview.AppendLine($"  Pattern: {sedInfo.Pattern}");
        preview.AppendLine($"  Replacement: {sedInfo.Replacement}");
        preview.AppendLine($"  Flags: {sedInfo.Flags}");
        preview.AppendLine();

        // 生成简易 diff
        var oldLines = oldContent.Split('\n');
        var newLines = newContent.Split('\n');
        var maxLines = Math.Max(oldLines.Length, newLines.Length);
        var changeCount = 0;

        for (var i = 0; i < maxLines && changeCount < 20; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;

            if (oldLine != newLine)
            {
                changeCount++;
                if (oldLine is not null)
                    preview.AppendLine($"- {oldLine.TrimEnd('\r')}");
                if (newLine is not null)
                    preview.AppendLine($"+ {newLine.TrimEnd('\r')}");
            }
        }

        if (changeCount == 0) changeCount = Math.Abs(oldLines.Length - newLines.Length);

        preview.AppendLine();
        preview.AppendLine($"{changeCount} line(s) changed. Re-run the same sed command to confirm and apply this edit.");

        return ResultBuilder.Success().WithText(preview.ToString()).Build();
    }

    /// <summary>
    /// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
    /// 存储首次 sed -i 调用的预计算结果，二次调用确认后写入
    /// </summary>
    private sealed record PendingSedConfirmation(
        string NewContent,
        string OriginalLineEnding,
        string SedPattern,
        string SedReplacement)
    {
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 是否已过期（60s 窗口）
        /// </summary>
        public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromSeconds(60);
    }
}
