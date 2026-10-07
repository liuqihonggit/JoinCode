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
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,tag_name,name)", Required = false)] string? json_fields = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(result.Body, json_fields) : SummarizeReleaseList(result.Body, exclude_drafts, exclude_prereleases));
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
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,tag_name,name)", Required = false)] string? json_fields = null,
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
            if (result.Success) return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(result.Body, json_fields) : result.Body);
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
    /// 创建 Release — 支持 draft/prerelease、目标 commit/branch、自动生成 notes、discussion_category/notes_from_tag/verify_tag，调 REST API POST
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseCreate, "创建 Release(支持 draft/prerelease/自动生成 notes/discussion_category/notes_from_tag/verify_tag)", "github")]
    public async Task<ToolResult> GhReleaseCreateAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("Release 标题", Required = false)] string? title = null,
        [McpToolParameter("Release 说明(notes)", Required = false)] string? notes = null,
        [McpToolParameter("从文件读取 release notes(可选,覆盖 notes 参数)", Required = false)] string? notes_file = null,
        [McpToolParameter("是否草稿", Required = false)] bool? draft = null,
        [McpToolParameter("是否预发布", Required = false)] bool? prerelease = null,
        [McpToolParameter("目标 commit/branch(可选)", Required = false)] string? target = null,
        [McpToolParameter("自动生成 release notes(可选)", Required = false)] bool? generate_notes = null,
        [McpToolParameter("标记为 latest release(可选,值 true/false)", Required = false)] string? make_latest = null,
        [McpToolParameter("discussion 分类名(可选,为 release 创建 discussion)", Required = false)] string? discussion_category = null,
        [McpToolParameter("没有新 commit 时失败(可选,检查 git log lastTag..target)", Required = false)] bool? fail_on_no_commits = null,
        [McpToolParameter("使用 tag annotation 作为 notes(可选,git tag -n)", Required = false)] bool? notes_from_tag = null,
        [McpToolParameter("自动生成 notes 的起始 tag(可选,等价 previous_tag_name)", Required = false)] string? notes_start_tag = null,
        [McpToolParameter("验证 tag 的 GPG 签名(可选,git tag -v)", Required = false)] bool? verify_tag = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var effectiveNotes = notes;
            if (notes_file is not null) {
                if (!_fs.FileExists(notes_file))
                    return Fail($"notes_file 不存在: {notes_file}");
                effectiveNotes = await _fs.ReadAllTextAsync(notes_file, cancellationToken).ConfigureAwait(false);
            }
            if (notes_from_tag == true && _git is not null) {
                var tagResult = await _git.ExecuteAsync($"tag -n -l {tag}", working_dir, cancellationToken).ConfigureAwait(false);
                if (tagResult.Success && !string.IsNullOrWhiteSpace(tagResult.Output)) {
                    var tagLine = tagResult.Output.AsSpan().Trim();
                    var spaceIdx = tagLine.IndexOf(' ');
                    if (spaceIdx >= 0 && spaceIdx + 1 < tagLine.Length) effectiveNotes = tagLine[(spaceIdx + 1)..].Trim().ToString();
                }
            }
            if (verify_tag == true && _git is not null) {
                var verifyResult = await _git.ExecuteAsync($"tag -v {tag}", working_dir, cancellationToken).ConfigureAwait(false);
                if (!verifyResult.Success) return Fail($"tag {tag} 的 GPG 签名验证失败: {verifyResult.Output}");
            }
            if (fail_on_no_commits == true && _git is not null) {
                var startTag = notes_start_tag;
                if (string.IsNullOrWhiteSpace(startTag)) {
                    var lastReleaseResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/latest", ct: cancellationToken).ConfigureAwait(false);
                    if (lastReleaseResult.Success) startTag = TryExtractJsonField(lastReleaseResult.Body, "tag_name");
                }
                if (!string.IsNullOrWhiteSpace(startTag)) {
                    var targetRef = string.IsNullOrWhiteSpace(target) ? "HEAD" : target;
                    var logResult = await _git.ExecuteAsync($"log {startTag}..{targetRef} --oneline", working_dir, cancellationToken).ConfigureAwait(false);
                    if (logResult.Success && string.IsNullOrWhiteSpace(logResult.Output.Trim()))
                        return Fail($"tag {startTag} 到 {targetRef} 之间没有新 commit,--fail_on_no_commits 失败");
                }
            }
            var jsonBody = JsonSerializer.Serialize(new ReleaseCreateRequest {
                TagName = tag,
                Name = title,
                Body = effectiveNotes,
                Draft = draft,
                Prerelease = prerelease,
                TargetCommitish = target,
                GenerateReleaseNotes = generate_notes,
                MakeLatest = make_latest,
                DiscussionCategoryName = discussion_category,
                PreviousTagName = notes_start_tag,
            }, GitHubApiJsonContext.Safe.ReleaseCreateRequest);
            var result = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/releases", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已创建 Release {tag}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 下载 Release asset — 复用 IDownloader 多线程分片 + 断点续传，支持 asset 名称过滤、并发数控制、clobber 覆盖、skip_existing 跳过
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseDownload, "下载 Release asset(复用多线程分片+断点续传,支持 clobber/skip_existing)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseDownloadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("保存目录", Required = true)] string dir,
        [McpToolParameter("asset 名称过滤模式(可选,支持 * 通配,默认下载全部)", Required = false)] string? pattern = null,
        [McpToolParameter("最大并发线程数(1-32,默认 4)", Required = false)] int? max_threads = null,
        [McpToolParameter("是否启用断点续传(默认 true)", Required = false)] bool? resume = null,
        [McpToolParameter("覆盖已存在文件(可选,默认 false,文件存在时报错)", Required = false)] bool? clobber = null,
        [McpToolParameter("跳过已存在文件(可选)", Required = false)] bool? skip_existing = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhReleaseDownloadCoreAsync(client, owner, repoName, tag, dir, pattern, max_threads, resume, clobber, skip_existing, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhReleaseDownload 核心逻辑 — 解析 asset 列表 + 多线程分片下载 + clobber/skip_existing 处理
    /// </summary>
    private async Task<ToolResult> GhReleaseDownloadCoreAsync(IGitHubApiClient client, string owner, string repoName, string tag, string dir, string? pattern, int? max_threads, bool? resume, bool? clobber, bool? skip_existing, CancellationToken cancellationToken) {
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
        var skipCount = 0;
        foreach (var (name, url) in assets) {
            var filePath = _fs.CombinePath(dir, name);
            if (_fs.FileExists(filePath)) {
                if (skip_existing == true) {
                    skipCount++;
                    sb.AppendLine($"[SKIP] {name} (已存在)");
                    continue;
                }
                if (clobber != true) {
                    failCount++;
                    sb.AppendLine($"[FAIL] {name}: 文件已存在(用 clobber=true 覆盖或 skip_existing=true 跳过)");
                    continue;
                }
            }
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
        sb.Append($"汇总: {successCount} 成功, {failCount} 失败, {skipCount} 跳过, 共 {assets.Count} 个 asset");
        return failCount == 0
            ? Ok(sb.ToString(), $"Release {tag} 下载完成:")
            : ToolResultBuilder.Error().WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// 上传 asset 到 Release — 走 uploads.github.com 二进制上传，支持多文件逗号分隔、clobber 覆盖已有 asset
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseUpload, "上传 asset 到 Release(支持 clobber 覆盖)", "github")]
    public async Task<ToolResult> GhReleaseUploadAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("要上传的文件路径(多个用逗号分隔)", Required = true)] string files,
        [McpToolParameter("覆盖同名 asset(可选,默认 false)", Required = false)] bool? clobber = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, (client, owner, repoName)
            => GhReleaseUploadCoreAsync(client, owner, repoName, tag, files, clobber, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// GhReleaseUpload 核心逻辑 — 走 uploads.github.com 二进制上传,clobber=true 时先删除同名 asset
    /// </summary>
    private async Task<ToolResult> GhReleaseUploadCoreAsync(IGitHubApiClient client, string owner, string repoName, string tag, string files, bool? clobber, CancellationToken cancellationToken) {
        var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
        if (!viewResult.Success) return Fail(viewResult.Error);

        long releaseId;
        Dictionary<string, long> existingAssets;
        try {
            using var doc = JsonDocument.Parse(viewResult.Body);
            releaseId = doc.RootElement.GetProperty("id").GetInt64();
            existingAssets = new Dictionary<string, long>();
            if (doc.RootElement.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array) {
                foreach (var asset in assetsEl.EnumerateArray()) {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var id = asset.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(name) && id > 0) existingAssets[name] = id;
                }
            }
        } catch (Exception ex) { return Fail($"解析 Release id 失败: {ex.Message}"); }

        var filePaths = files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        var successCount = 0;
        var failCount = 0;
        foreach (var filePath in filePaths) {
            var fileName = Path.GetFileName(filePath);
            if (clobber == true && existingAssets.TryGetValue(fileName, out var existingId)) {
                var delResult = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/releases/{releaseId}/assets/{existingId}", ct: cancellationToken).ConfigureAwait(false);
                if (!delResult.Success) {
                    failCount++;
                    sb.AppendLine($"[FAIL] {fileName}: 删除已有 asset 失败({delResult.Error})");
                    continue;
                }
            }
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
    /// 删除 Release asset — 按 asset 名称查找并删除，调 REST API DELETE
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseDeleteAsset, "删除 Release asset", "github")]
    public async Task<ToolResult> GhReleaseDeleteAssetAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("asset 名称(多个用逗号分隔)", Required = true)] string asset_name,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
            if (!viewResult.Success) return Fail(viewResult.Error);
            long releaseId;
            Dictionary<string, long> assetMap;
            try {
                using var doc = JsonDocument.Parse(viewResult.Body);
                releaseId = doc.RootElement.GetProperty("id").GetInt64();
                assetMap = new Dictionary<string, long>();
                if (doc.RootElement.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array) {
                    foreach (var asset in assetsEl.EnumerateArray()) {
                        var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var id = asset.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
                        if (!string.IsNullOrEmpty(name) && id > 0) assetMap[name] = id;
                    }
                }
            } catch (Exception ex) { return Fail($"解析 Release 失败: {ex.Message}"); }

            var assetNames = asset_name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var sb = new StringBuilder();
            var successCount = 0;
            var failCount = 0;
            foreach (var name in assetNames) {
                if (!assetMap.TryGetValue(name, out var assetId)) {
                    failCount++;
                    sb.AppendLine($"[FAIL] {name}: asset 不存在");
                    continue;
                }
                var delResult = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/releases/{releaseId}/assets/{assetId}", ct: cancellationToken).ConfigureAwait(false);
                if (delResult.Success) {
                    successCount++;
                    sb.AppendLine($"[OK] {name}");
                } else {
                    failCount++;
                    sb.AppendLine($"[FAIL] {name}: {delResult.Error}");
                }
            }
            sb.AppendLine();
            sb.Append($"汇总: {successCount} 成功, {failCount} 失败, 共 {assetNames.Length} 个 asset");
            return failCount == 0 ? Ok(sb.ToString(), $"已删除 asset") : Fail(sb.ToString());
        }).ConfigureAwait(false);

    /// <summary>
    /// 编辑 Release — 修改 tag/name/notes/draft/prerelease/target/latest，调 REST API PATCH
    /// <para>notes_file 从文件读取 notes;generate_notes 调 generate-notes API 自动生成</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseEdit, "编辑 Release(tag/name/notes/draft/prerelease/target/latest)", "github")]
    public async Task<ToolResult> GhReleaseEditAsync(
        [McpToolParameter("Release tag 名称", Required = true)] string tag,
        [McpToolParameter("新 tag 名称(可选)", Required = false)] string? new_tag = null,
        [McpToolParameter("新标题(可选)", Required = false)] string? title = null,
        [McpToolParameter("新说明(notes,可选)", Required = false)] string? notes = null,
        [McpToolParameter("从文件读取 notes(可选,与 notes 互斥)", Required = false)] string? notes_file = null,
        [McpToolParameter("自动生成 release notes(默认 false)", Required = false)] bool? generate_notes = null,
        [McpToolParameter("是否草稿(可选)", Required = false)] bool? draft = null,
        [McpToolParameter("是否预发布(可选)", Required = false)] bool? prerelease = null,
        [McpToolParameter("目标 commit/branch(可选)", Required = false)] string? target = null,
        [McpToolParameter("标记为 latest(可选,true/false/legacy)", Required = false)] string? make_latest = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var viewResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/releases/tags/{tag}", ct: cancellationToken).ConfigureAwait(false);
            if (!viewResult.Success) return Fail(viewResult.Error);
            long releaseId;
            string? existingTag = null;
            string? existingTarget = null;
            try {
                using var doc = JsonDocument.Parse(viewResult.Body);
                releaseId = doc.RootElement.GetProperty("id").GetInt64();
                existingTag = doc.RootElement.TryGetProperty("tag_name", out var tnEl) ? tnEl.GetString() : null;
                existingTarget = doc.RootElement.TryGetProperty("target_commitish", out var tcEl) ? tcEl.GetString() : null;
            } catch (Exception ex) { return Fail($"解析 Release id 失败: {ex.Message}"); }

            var effectiveNotes = notes;
            if (!string.IsNullOrWhiteSpace(notes_file)) {
                if (!_fs.FileExists(notes_file)) return Fail($"notes 文件不存在: {notes_file}");
                try { effectiveNotes = await _fs.ReadAllTextAsync(notes_file, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { return Fail($"读取 notes 文件失败: {ex.Message}"); }
            }
            if (generate_notes == true) {
                var genBody = JsonSerializer.Serialize(new ReleaseGenerateNotesRequest {
                    TagName = new_tag ?? existingTag ?? tag,
                    TargetCommitish = target ?? existingTarget
                }, GitHubApiJsonContext.Safe.ReleaseGenerateNotesRequest);
                var genResult = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/releases/generate-notes", genBody, ct: cancellationToken).ConfigureAwait(false);
                if (!genResult.Success) return Fail($"生成 notes 失败: {genResult.Error}");
                try {
                    using var genDoc = JsonDocument.Parse(genResult.Body);
                    effectiveNotes = genDoc.RootElement.TryGetProperty("body", out var bEl) ? bEl.GetString() : effectiveNotes;
                } catch (JsonException ex) { _logger?.LogWarning(ex, "解析 generate-notes 响应失败,使用原 notes"); }
            }

            var jsonBody = JsonSerializer.Serialize(new ReleaseEditRequest {
                TagName = new_tag,
                Name = title,
                Body = effectiveNotes,
                Draft = draft,
                Prerelease = prerelease,
                TargetCommitish = target,
                MakeLatest = make_latest
            }, GitHubApiJsonContext.Safe.ReleaseEditRequest);
            var result = await client.SendAsync(HttpMethod.Patch, $"repos/{owner}/{repoName}/releases/{releaseId}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? OkBrief(result.Body, $"已编辑 Release {tag}") : Fail(result.Error);
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

    /// <summary>
    /// 验证 Release 签名 — 调 GitHub attestations API 获取 attestation 元数据(加密验证需 cosign CLI)
    /// <para>简化实现：列出 release 所有 asset 的 attestation 元数据，不做 sigstore 加密验证</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseVerify, "验证 Release 签名(获取 attestation 元数据)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseVerifyAsync(
        [McpToolParameter("Release tag(可选,默认最新 release)", Required = false)] string? tag = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var releasePath = string.IsNullOrWhiteSpace(tag) ? $"repos/{owner}/{repoName}/releases/latest" : $"repos/{owner}/{repoName}/releases/tags/{tag}";
            var releaseResult = await client.SendAsync(HttpMethod.Get, releasePath, ct: cancellationToken).ConfigureAwait(false);
            if (!releaseResult.Success) return Fail(releaseResult.Error);
            var sb = new StringBuilder(512);
            string? releaseTag = null;
            try {
                using var doc = JsonDocument.Parse(releaseResult.Body);
                releaseTag = doc.RootElement.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
                sb.AppendLine($"Release: {releaseTag}");
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
                    sb.AppendLine($"Assets: {assets.GetArrayLength()} 个");
                    foreach (var asset in assets.EnumerateArray()) {
                        var name = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                        var digest = asset.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";
                        sb.AppendLine($"  - {name}{(string.IsNullOrEmpty(digest) ? "" : $" (digest: {digest[..Math.Min(16, digest.Length)]}...)")}");
                    }
                }
            } catch { return Fail("解析 Release 响应失败"); }
            sb.AppendLine();
            sb.Append($"⚠ 加密签名验证需 cosign CLI: cosign verify-attestation --repo {owner}/{repoName} ghcr.io/{owner}/{repoName}:{releaseTag}");
            return Ok(sb.ToString(), "Release attestation 元数据(加密验证需 cosign)");
        }).ConfigureAwait(false);

    /// <summary>
    /// 验证 Release Asset 签名 — 计算文件 SHA256 digest 后调 attestations API(加密验证需 cosign CLI)
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhReleaseVerifyAsset, "验证 Asset 文件签名(SHA256 + attestation)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhReleaseVerifyAssetAsync(
        [McpToolParameter("Asset 文件路径", Required = true)] string file_path,
        [McpToolParameter("Release tag(可选,默认最新 release)", Required = false)] string? tag = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (!_fs.FileExists(file_path)) return Fail($"文件不存在: {file_path}");
            string sha256Digest;
            try {
                using var stream = _fs.Open(file_path, FileMode.Open);
                var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                sha256Digest = $"sha256:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
            } catch (Exception ex) { return Fail($"计算文件 SHA256 失败: {ex.Message}"); }
            var attResult = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/attestations/{sha256Digest}", ct: cancellationToken).ConfigureAwait(false);
            var sb = new StringBuilder(256);
            sb.AppendLine($"文件: {file_path}");
            sb.AppendLine($"SHA256: {sha256Digest}");
            if (attResult.Success) {
                sb.AppendLine("Attestation: 已找到");
                sb.Append(attResult.Body);
            } else {
                sb.AppendLine($"Attestation: {attResult.Error}");
                sb.Append("⚠ 加密签名验证需 cosign CLI: cosign verify-blob --artifact-reference <file> --certificate-identity <workflow>");
            }
            return Ok(sb.ToString(), "Asset attestation 验证(加密验证需 cosign)");
        }).ConfigureAwait(false);
}