namespace Tools.Shell;

/// <summary>
/// Shell sed 编辑拦截中间件 — 对齐 TS SedEditPermissionRequest 预览-确认-应用流程
/// 首次 sed -i 返回预览，存储预计算结果；二次调用确认后写入
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellSedInterceptMiddleware : ServiceEntity, IShellMiddleware {

    /// <summary>
    /// 构造 sed 拦截中间件
    /// </summary>
    /// <param name="fs">文件系统（可选，为 null 时返回不可用诊断）</param>
    public ShellSedInterceptMiddleware(IFileSystem? fs = null) {
        _fs = fs;
    }
    private readonly IFileSystem? _fs;

    /// <summary>
    /// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
    /// 首次 sed -i 返回预览，存储预计算结果；二次调用确认后写入
    /// key: 文件路径, value: (新内容, 创建时间)
    /// </summary>
    private ImmutableHamT<string, PendingSedConfirmation> _fallbackEdits = ImmutableHamT<string, PendingSedConfirmation>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    private static ISessionCache? GetCurrentCache() {
        var sessionId = SessionContext.Current;
        if (sessionId is null) return null;
        return SessionRouter.GetScope(sessionId.Value)?.Cache;
    }

    /// <summary>
    /// sed 确认窗口 — 60s 内有效
    /// </summary>
    private static readonly TimeSpan SedConfirmationWindow = TimeSpan.FromSeconds(60);

    /// <inheritdoc />

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct) {
        var sedEditInfo = SedEditParser.ParseSedEditCommand(context.Command);
        if (sedEditInfo is not null) {
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
    private async Task<ToolResult> HandleSedEditAsync(SedEditInfo sedInfo, string? workingDirectory, CancellationToken cancellationToken) {
        if (_fs is null) {
            var diag = BuildFileSystemUnavailableDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var filePath = ResolveFilePath(sedInfo.FilePath, workingDirectory, _fs.GetCurrentDirectory());

        // 二次调用确认：检查是否有待确认的编辑 — 对齐 TS _simulatedSedEdit
        var cache = GetCurrentCache();
        var pending = cache?.Get<PendingSedConfirmation>(filePath);
        if (pending is null && Volatile.Read(ref _fallbackEdits).TryGetValue(filePath, out var fallbackPending))
            pending = fallbackPending;

        if (pending is not null && !pending.IsExpired) {
            // 验证 sed 信息匹配（防止模型伪造不同编辑）
            if (pending.SedPattern == sedInfo.Pattern && pending.SedReplacement == sedInfo.Replacement) {
                await ClearPendingAsync(cache, filePath).ConfigureAwait(false);

                // 用 EditFileAsync 原子编辑：重新读→替换→写，基于最新内容应用 sed
                try {
                    await _fs.EditFileAsync<bool>(filePath, async (bytes, ct) => {
                        var (content, encoding) = FileEncodingDetector.DecodeBytes(bytes);
                        var lineEnding = DetectLineEnding(content);
                        var normalizedContent = NormalizeLineEndings(content);
                        var newContent = SedEditParser.ApplySedSubstitution(normalizedContent, sedInfo);
                        var finalContent = newContent.Replace("\n", lineEnding);
                        var newBytes = FileEncodingDetector.EncodeString(finalContent, encoding);
                        return (newBytes, true);
                    }, cancellationToken).ConfigureAwait(false);
                } catch (Exception ex) {
                    var diag = BuildWriteFailedDiagnostic(filePath, ex.Message);
                    return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
                }

                return ToolResultBuilder.Success().WithText($"Applied sed substitution to {sedInfo.FilePath}").Build();
            }

            // sed 信息不匹配，清除旧的 pending 并重新预览
            await ClearPendingAsync(cache, filePath).ConfigureAwait(false);
        }

        // 首次调用：读取文件→模拟替换→返回预览
        if (!_fs.FileExists(filePath)) {
            var diag = BuildFileNotFoundDiagnostic(sedInfo.FilePath);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        string oldContent;
        string originalLineEnding;
        try {
            var rawContent = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            originalLineEnding = DetectLineEnding(rawContent);
            oldContent = NormalizeLineEndings(rawContent);
        } catch (Exception ex) {
            var diag = BuildReadFailedDiagnostic(filePath, ex.Message);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        // 模拟替换
        var newContent = SedEditParser.ApplySedSubstitution(oldContent, sedInfo);

        // 检查是否有变更
        if (oldContent == newContent) {
            return ToolResultBuilder.Success().WithText(BuildNoChangeMessage(oldContent)).Build();
        }

        // 存储待确认编辑 — 对齐 TS _simulatedSedEdit 注入
        var confirmation = new PendingSedConfirmation(
            newContent,
            originalLineEnding,
            sedInfo.Pattern,
            sedInfo.Replacement);
        if (cache is not null)
            await cache.SetAsync(filePath, confirmation, SedConfirmationWindow).ConfigureAwait(false);
        else
            ImmutableInterlocked.Update(ref _fallbackEdits, d => d.SetItem(filePath, confirmation));

        // 返回 diff 预览 — 对齐 TS SedEditPermissionRequest 展示 FileEditToolDiff
        return ToolResultBuilder.Success().WithText(BuildSedPreview(sedInfo, oldContent, newContent)).Build();
    }

    internal static ToolDiagnostic BuildFileSystemUnavailableDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "服务不可用",
            formattedMessage: "sed -i requires file system access but IFileSystem is not available");

    internal static ToolDiagnostic BuildWriteFailedDiagnostic(string filePath, string errorMessage) =>
        ToolDiagnostic.Create(
            reason: "写入文件失败",
            formattedMessage: $"Failed to write file: {errorMessage}",
            details:
            [
                new DiagnosticDetail("file_path", filePath),
                new DiagnosticDetail("error", errorMessage)
            ]);

    internal static ToolDiagnostic BuildFileNotFoundDiagnostic(string filePath) =>
        ToolDiagnostic.Create(
            reason: "文件未找到",
            formattedMessage: $"File not found: {filePath}",
            details: [new DiagnosticDetail("file_path", filePath)],
            suggestions: ["确认文件路径是否正确", "使用绝对路径或检查工作目录"]);

    internal static ToolDiagnostic BuildReadFailedDiagnostic(string filePath, string errorMessage) =>
        ToolDiagnostic.Create(
            reason: "读取文件失败",
            formattedMessage: $"Failed to read file: {errorMessage}",
            details:
            [
                new DiagnosticDetail("file_path", filePath),
                new DiagnosticDetail("error", errorMessage)
            ]);

    /// <summary>
    /// 解析文件路径为绝对路径 — 纯计算,无 IO
    /// </summary>
    /// <param name="filePath">原始路径（可能相对）</param>
    /// <param name="workingDirectory">工作目录（可选,优先于 currentDir）</param>
    /// <param name="currentDir">当前目录（当 workingDirectory 为 null 时使用）</param>
    /// <returns>绝对路径</returns>
    internal static string ResolveFilePath(string filePath, string? workingDirectory, string currentDir) {
        if (Path.IsPathRooted(filePath)) return filePath;
        var cwd = workingDirectory ?? currentDir;
        return Path.Combine(cwd, filePath);
    }

    /// <summary>
    /// 检测内容的行尾风格 — 纯计算
    /// </summary>
    /// <param name="content">文件内容</param>
    /// <returns>"\r\n"（CRLF）或 "\n"（LF）</returns>
    internal static string DetectLineEnding(string content) =>
        content.Contains("\r\n") ? "\r\n" : "\n";

    /// <summary>
    /// 规范化行尾为 LF — 纯计算
    /// </summary>
    /// <param name="content">原始内容（可能含 CRLF）</param>
    /// <returns>仅含 LF 的内容</returns>
    internal static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n");

    /// <summary>
    /// 构造无变更消息 — 纯计算
    /// </summary>
    /// <param name="oldContent">原始内容</param>
    /// <returns>空文件消息或模式未匹配消息</returns>
    internal static string BuildNoChangeMessage(string oldContent) =>
        string.IsNullOrEmpty(oldContent)
            ? "File is empty, pattern did not match"
            : "Pattern did not match any content";

    /// <summary>
    /// 生成 sed 编辑 diff 预览文本 — 纯计算,无 IO
    /// </summary>
    /// <param name="sedInfo">sed 编辑信息</param>
    /// <param name="oldContent">原始内容（已规范化为 LF）</param>
    /// <param name="newContent">替换后内容（已规范化为 LF）</param>
    /// <returns>预览文本,含头部信息 + 最多 20 行 diff + 变更统计</returns>
    internal static string BuildSedPreview(SedEditInfo sedInfo, string oldContent, string newContent) {
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

        for (var i = 0; i < maxLines && changeCount < 20; i++) {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;

            if (oldLine != newLine) {
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

        return preview.ToString();
    }

    /// <summary>
    /// 清除待确认的 sed 编辑缓存项
    /// </summary>
    private async Task ClearPendingAsync(ISessionCache? cache, string filePath) {
        if (cache is not null)
            await cache.RemoveAsync(filePath).ConfigureAwait(false);
        ImmutableInterlocked.Update(ref _fallbackEdits, d => d.Remove(filePath));
    }

    /// <summary>
    /// 待确认的 sed 编辑 — 对齐 TS _simulatedSedEdit
    /// 存储首次 sed -i 调用的预计算结果，二次调用确认后写入
    /// </summary>
    private sealed record PendingSedConfirmation(
        string NewContent,
        string OriginalLineEnding,
        string SedPattern,
        string SedReplacement) {
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