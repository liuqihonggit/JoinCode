namespace McpToolDispatch;

/// <summary>
/// GitHub Release 工具 — gh release 子命令全套
/// <para>核心优化: gh_release_download 复用 IDownloader 多线程分片 + 断点续传,解决下载失败痛点</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhReleaseList, "列出 Release", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder("release list");
        sb.Append($" --limit {limit ?? 30}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhReleaseView, "查看 Release 详情(含 asset 列表)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseViewAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"release view {tag} --json tagName,name,body,assets,url,isDraft,isPrerelease,publishedAt");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output) : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhReleaseCreate, "创建 Release(支持 draft/prerelease)", "github")]
    public async Task<ToolResult> GhReleaseCreateAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("Release 标题", Required = false)] string? title = null,
        [McpToolParameter("Release 说明(notes)", Required = false)] string? notes = null,
        [McpToolParameter("是否草稿", Required = false)] bool? draft = null,
        [McpToolParameter("是否预发布", Required = false)] bool? prerelease = null,
        [McpToolParameter("目标 commit/branch(可选)", Required = false)] string? target = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"release create {tag}");
        if (!string.IsNullOrWhiteSpace(title)) sb.Append($" --title {Quote(title)}");
        if (!string.IsNullOrWhiteSpace(notes)) sb.Append($" --notes {Quote(notes)}");
        if (draft == true) sb.Append(" --draft");
        if (prerelease == true) sb.Append(" --prerelease");
        if (!string.IsNullOrWhiteSpace(target)) sb.Append($" --target {target}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已创建 Release {tag}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhReleaseDownload, "下载 Release asset(复用多线程分片+断点续传,解决下载失败)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseDownloadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("保存目录", Required = true)] string dir,
        [McpToolParameter("asset 名称过滤模式(可选,支持 * 通配,默认下载全部)", Required = false)] string? pattern = null,
        [McpToolParameter("最大并发线程数(1-32,默认 4)", Required = false)] int? max_threads = null,
        [McpToolParameter("是否启用断点续传(默认 true)", Required = false)] bool? resume = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        // 第一步: 获取 asset 列表
        var viewSb = new StringBuilder($"release view {tag} --json assets");
        viewSb.Append(RepoArg(repo));
        var viewResult = await RunGhAsync(viewSb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        if (!viewResult.Success) return Fail(viewResult);

        // 第二步: 解析 JSON 提取 asset url + name
        List<(string name, string url)> assets;
        try
        {
            using var doc = JsonDocument.Parse(viewResult.Output);
            assets = [];
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var url = asset.GetProperty("url").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (!string.IsNullOrWhiteSpace(pattern) && !SimpleMatch(pattern, name)) continue;
                assets.Add((name, url));
            }
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"解析 Release asset 列表失败: {ex.Message}").Build();
        }

        if (assets.Count == 0)
        {
            return ToolResultBuilder.Error().WithText($"Release {tag} 没有匹配的 asset{(string.IsNullOrWhiteSpace(pattern) ? string.Empty : $" (pattern={pattern})")}").Build();
        }

        // 第三步: 确保目录存在
        _fs.CreateDirectory(dir);

        // 第四步: 多线程分片并行下载每个 asset
        var options = new DownloadOptions
        {
            MaxThreads = max_threads ?? 4,
            Resume = resume ?? true,
        };

        var sb = new StringBuilder();
        var successCount = 0;
        var failCount = 0;
        foreach (var (name, url) in assets)
        {
            var filePath = Path.Combine(dir, name);
            try
            {
                var session = _downloader.StartDownload(url, filePath, options, null, cancellationToken);
                await using (session.ConfigureAwait(false))
                {
                    var dlResult = await session.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
                    if (dlResult.Success)
                    {
                        successCount++;
                        var sizeStr = ContentReplacementConstants.FormatFileSize(dlResult.TotalBytes);
                        sb.AppendLine($"[OK] {name} ({sizeStr}, {dlResult.Elapsed.TotalSeconds:F1}s)");
                    }
                    else
                    {
                        failCount++;
                        sb.AppendLine($"[FAIL] {name}: {dlResult.ErrorMessage ?? "下载失败"}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return ToolResultBuilder.Error().WithText("下载已取消").Build();
            }
            catch (Exception ex)
            {
                failCount++;
                sb.AppendLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        sb.AppendLine();
        sb.Append($"汇总: {successCount} 成功, {failCount} 失败, 共 {assets.Count} 个 asset");
        return failCount == 0
            ? Ok(sb.ToString(), $"Release {tag} 下载完成:")
            : ToolResultBuilder.Error().WithText(sb.ToString()).Build();
    }

    [McpTool(GitHubToolNameConstants.GhReleaseUpload, "上传 asset 到 Release", "github")]
    public async Task<ToolResult> GhReleaseUploadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("要上传的文件路径(多个用逗号分隔)", Required = true)] string files,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"release upload {tag} {files}");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已上传 asset 到 Release {tag}") : Fail(result);
    }

    [McpTool(GitHubToolNameConstants.GhReleaseDelete, "删除 Release", "github")]
    public async Task<ToolResult> GhReleaseDeleteAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("是否跳过确认(默认 true)", Required = false)] bool? yes = null,
        [McpToolParameter("仓库(可选)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder($"release delete {tag}");
        if (yes != false) sb.Append(" --yes");
        sb.Append(RepoArg(repo));
        var result = await RunGhAsync(sb.ToString(), ResolveWorkDir(working_dir), cancellationToken);
        return result.Success ? Ok(result.Output, $"已删除 Release {tag}") : Fail(result);
    }

    /// <summary>
    /// 简单通配符匹配 — 支持 * 通配
    /// </summary>
    private static bool SimpleMatch(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (pattern == "*") return true;
        if (!pattern.Contains('*')) return name == pattern;
        // 转为正则: * → .*
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
