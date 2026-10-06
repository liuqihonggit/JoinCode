namespace McpToolDispatch;

/// <summary>
/// GitHub Release 工具 — 直调 GitHub REST API（ADR 0073），替代原 gh release 子命令包装
/// <para>核心优化: gh_release_download 复用 IDownloader 多线程分片 + 断点续传,解决下载失败痛点</para>
/// <para>gh_release_upload 走 uploads.github.com 二进制上传（IGitHubApiClient.UploadAssetAsync）</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出 Release — 调 REST API 获取 Release 列表，支持排除 draft/prerelease，精简输出（含 asset 摘要）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseList, "列出 Release(支持排除 draft/prerelease)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("排除 draft release(可选)", Required = false)] bool? exclude_drafts = null,
        [McpToolParameter("排除 prerelease(可选)", Required = false)] bool? exclude_prereleases = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases", query: query, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok(SummarizeReleaseList(result.Body, exclude_drafts, exclude_prereleases)) : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Release 列表 JSON — 只保留关键字段，去掉冗余 URL 和 author 对象，便于人类浏览和 AI 解析；支持排除 draft/prerelease
    /// </summary>
    private static string SummarizeReleaseList(string json, bool? excludeDrafts = null, bool? excludePrereleases = null) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
                writer.WriteStartArray();
                foreach (var release in doc.RootElement.EnumerateArray()) {
                    if (excludeDrafts == true && release.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                    if (excludePrereleases == true && release.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;
                    writer.WriteStartObject();
                    CopyProperty(release, writer, "id");
                    CopyProperty(release, writer, "tag_name");
                    CopyProperty(release, writer, "name");
                    CopyProperty(release, writer, "draft");
                    CopyProperty(release, writer, "prerelease");
                    CopyProperty(release, writer, "created_at");
                    CopyProperty(release, writer, "published_at");
                    if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
                        writer.WritePropertyName("assets");
                        writer.WriteStartArray();
                        foreach (var asset in assets.EnumerateArray()) {
                            writer.WriteStartObject();
                            CopyProperty(asset, writer, "name");
                            CopyProperty(asset, writer, "size");
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        } catch (Exception) {
            return json;
        }
    }

    /// <summary>
    /// 查看 Release 详情 — 调 REST API 获取 Release 信息（含 asset 列表）
    /// <para>先按 tag 查(releases/tags/{tag}),404 时 fallback 列出全部 release 按 tag_name 匹配(支持 draft release)</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseView, "查看 Release 详情(含 asset 列表)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseViewAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("web=true 只返回 Release 浏览器 URL", Required = false)] bool? web = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选,默认当前目录)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (web == true) {
                var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
                if (!viewResult.Success) return Fail(viewResult.Error);
                var url = ExtractHtmlUrl(viewResult.Body);
                return string.IsNullOrEmpty(url) ? Fail("无法从 Release 响应中解析 html_url") : Ok(url);
            }
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
            if (result.Success) return Ok(result.Body);
            // 404 时 fallback: draft release 没有关联 tag,需列出所有 release 按 tag_name 匹配
            if (IsNotFound(result)) return await FindReleaseByTagNameAsync(client, owner, repoName, tag, cancellationToken).ConfigureAwait(false);
            return Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 判断 API 响应是否为 404 Not Found
    /// </summary>
    private static bool IsNotFound(GitHubApiResponse response)
        => response.StatusCode == 404;

    /// <summary>
    /// 列出所有 release(含 draft)按 tag_name 匹配 — fallback 查 draft release
    /// </summary>
    private async Task<ToolResult> FindReleaseByTagNameAsync(IGitHubApiClient client, string owner, string repo, string tag, CancellationToken ct) {
        var listResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/releases", query: new Dictionary<string, string> { ["per_page"] = "100" }, ct: ct).ConfigureAwait(false);
        if (!listResult.Success) return Fail(listResult.Error);
        try {
            using var doc = JsonDocument.Parse(listResult.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Fail("Release 列表格式异常");
            foreach (var release in doc.RootElement.EnumerateArray()) {
                if (release.TryGetProperty("tag_name", out var tagEl) && tagEl.GetString() == tag) {
                    var id = release.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                    var isDraft = release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean();
                    var detailResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/releases/{id}", ct: ct).ConfigureAwait(false);
                    if (detailResult.Success) return Ok(detailResult.Body);
                    return Fail(detailResult.Error);
                }
            }
            return Fail($"未找到 Release: {tag}（已列出 {doc.RootElement.GetArrayLength()} 个 release，均不匹配 tag_name={tag}）");
        } catch (Exception ex) {
            return Fail($"查找 Release 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 创建 Release — 支持 draft/prerelease、目标 commit/branch、自动生成 notes，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseCreate, "创建 Release(支持 draft/prerelease/自动生成 notes)", "github")]
    public async Task<ToolResult> GhReleaseCreateAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("Release 标题", Required = false)] string? title = null,
        [McpToolParameter("Release 说明(notes)", Required = false)] string? notes = null,
        [McpToolParameter("是否草稿", Required = false)] bool? draft = null,
        [McpToolParameter("是否预发布", Required = false)] bool? prerelease = null,
        [McpToolParameter("目标 commit/branch(可选)", Required = false)] string? target = null,
        [McpToolParameter("自动生成 release notes(可选)", Required = false)] bool? generate_notes = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = new GitHubJsonObjectBuilder()
                .String("tag_name", tag)
                .StringIf("name", title)
                .StringIf("body", notes)
                .BoolIfTrue("draft", draft)
                .BoolIfTrue("prerelease", prerelease)
                .StringIf("target_commitish", target)
                .BoolIfTrue("generate_release_notes", generate_notes)
                .Build();
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/releases", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已创建 Release {tag}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 下载 Release asset — 复用 IDownloader 多线程分片 + 断点续传，支持 asset 名称过滤和并发数控制
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseDownload, "下载 Release asset(复用多线程分片+断点续传,解决下载失败)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseDownloadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("保存目录", Required = true)] string dir,
        [McpToolParameter("asset 名称过滤模式(可选,支持 * 通配,默认下载全部)", Required = false)] string? pattern = null,
        [McpToolParameter("最大并发线程数(1-32,默认 4)", Required = false)] int? max_threads = null,
        [McpToolParameter("是否启用断点续传(默认 true)", Required = false)] bool? resume = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhReleaseDownloadCoreAsync(client, owner, repoName, tag, dir, pattern, max_threads, resume, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhReleaseDownload 核心逻辑 — 解析 asset 列表 + 多线程分片下载
    /// </summary>
    private async Task<ToolResult> GhReleaseDownloadCoreAsync(IGitHubApiClient client, string owner, string repoName, string tag, string dir, string? pattern, int? max_threads, bool? resume, CancellationToken cancellationToken) {
        var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
        if (!viewResult.Success) return Fail(viewResult.Error);

        List<(string name, string url)> assets;
        try {
            using var doc = JsonDocument.Parse(viewResult.Body);
            assets = [];
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray()) {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (!string.IsNullOrWhiteSpace(pattern) && !SimpleMatch(pattern, name)) continue;
                assets.Add((name, url));
            }
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"解析 Release asset 列表失败: {ex.Message}").Build();
        }

        if (assets.Count == 0) {
            return ToolResultBuilder.Error().WithText($"Release {tag} 没有匹配的 asset{(string.IsNullOrWhiteSpace(pattern) ? string.Empty : $" (pattern={pattern})")}").Build();
        }

        _fs.CreateDirectory(dir);

        var options = new DownloadOptions {
            MaxThreads = max_threads ?? 4,
            Resume = resume ?? true,
        };

        var sb = new StringBuilder();
        var successCount = 0;
        var failCount = 0;
        foreach (var (name, url) in assets) {
            var filePath = _fs.CombinePath(dir, name);
            try {
                await using var session = _downloader.StartDownload(url, filePath, options, null, cancellationToken);
                var dlResult = await session.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
                if (dlResult.Success) {
                    successCount++;
                    var sizeStr = ContentReplacementConstants.FormatFileSize(dlResult.TotalBytes);
                    sb.AppendLine($"[OK] {name} ({sizeStr}, {dlResult.Elapsed.TotalSeconds:F1}s)");
                } else {
                    failCount++;
                    sb.AppendLine($"[FAIL] {name}: {dlResult.ErrorMessage ?? "下载失败"}");
                }
            } catch (OperationCanceledException) {
                return ToolResultBuilder.Error().WithText("下载已取消").Build();
            } catch (Exception ex) {
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

    /// <summary>
    /// 上传 asset 到 Release — 走 uploads.github.com 二进制上传，支持多文件逗号分隔
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseUpload, "上传 asset 到 Release", "github")]
    public async Task<ToolResult> GhReleaseUploadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("要上传的文件路径(多个用逗号分隔)", Required = true)] string files,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhReleaseUploadCoreAsync(client, owner, repoName, tag, files, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhReleaseUpload 核心逻辑 — 走 uploads.github.com 二进制上传
    /// </summary>
    private async Task<ToolResult> GhReleaseUploadCoreAsync(IGitHubApiClient client, string owner, string repoName, string tag, string files, CancellationToken cancellationToken) {
        var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
        if (!viewResult.Success) return Fail(viewResult.Error);

        long releaseId;
        try {
            using var doc = JsonDocument.Parse(viewResult.Body);
            releaseId = doc.RootElement.GetProperty("id").GetInt64();
        } catch (Exception ex) { return Fail($"解析 Release id 失败: {ex.Message}"); }

        var filePaths = files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        var successCount = 0;
        var failCount = 0;
        foreach (var filePath in filePaths) {
            var fileName = Path.GetFileName(filePath);
            try {
                await using var fileStream = _fs.OpenRead(filePath);
                var uploadResult = await client.UploadAssetAsync(owner, repoName, releaseId, fileName, fileStream, cancellationToken).ConfigureAwait(false);
                if (uploadResult.Success) {
                    successCount++;
                    sb.AppendLine($"[OK] {fileName}");
                } else {
                    failCount++;
                    sb.AppendLine($"[FAIL] {fileName}: {uploadResult.Error}");
                }
            } catch (Exception ex) {
                failCount++;
                sb.AppendLine($"[FAIL] {fileName}: {ex.Message}");
            }
        }

        sb.AppendLine();
        sb.Append($"汇总: {successCount} 成功, {failCount} 失败, 共 {filePaths.Length} 个文件");
        return failCount == 0
            ? Ok(sb.ToString(), $"已上传 asset 到 Release {tag}")
            : ToolResultBuilder.Error().WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// 删除 Release — 可选同时删除 git tag，调 REST API DELETE
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseDelete, "删除 Release(可选同时删除 git tag)", "github")]
    public async Task<ToolResult> GhReleaseDeleteAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("是否跳过确认(默认 true)", Required = false)] bool? yes = null,
        [McpToolParameter("同时删除 git tag(可选)", Required = false)] bool? cleanup_tag = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
            if (!viewResult.Success) return Fail(viewResult.Error);
            long releaseId;
            try {
                using var doc = JsonDocument.Parse(viewResult.Body);
                releaseId = doc.RootElement.GetProperty("id").GetInt64();
            } catch (Exception ex) { return Fail($"解析 Release id 失败: {ex.Message}"); }
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/releases/{releaseId}", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            if (cleanup_tag == true) {
                var tagResult = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/git/refs/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
                if (!tagResult.Success) return OkBrief(result.Body, $"已删除 Release {tag}(git tag 删除失败: {tagResult.Error})");
            }
            return OkBrief(result.Body, $"已删除 Release {tag}{(cleanup_tag == true ? " 及 git tag" : "")}");
        }).ConfigureAwait(false);

    /// <summary>
    /// 简单通配符匹配 — 支持 * 通配
    /// </summary>
    private static bool SimpleMatch(string pattern, string name) {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (pattern == "*") return true;
        if (!pattern.Contains('*')) return name == pattern;
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}