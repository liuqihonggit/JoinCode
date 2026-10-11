namespace IO.Services.Update;

/// <summary>
/// GitHub Release JSON 响应 DTO — 镜像 GitHub API /releases/latest 格式
/// </summary>
public sealed class GitHubReleaseDto {
    /// <summary>标签名（如 v1.0.0）</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>发布时间</summary>
    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Release 正文（发布说明）</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>资产列表</summary>
    [JsonPropertyName("assets")]
    public List<GitHubAssetDto> Assets { get; set; } = [];
}

/// <summary>
/// GitHub Release 资产 DTO
/// </summary>
public sealed class GitHubAssetDto {
    /// <summary>文件名</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>浏览器下载 URL</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    /// <summary>文件大小（字节）</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

/// <summary>
/// Gitea Release JSON 响应 DTO — 镜像 Gitea API /releases 格式（数组）
/// </summary>
public sealed class GiteaReleaseDto {
    /// <summary>标签名</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Release 正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>资产列表</summary>
    [JsonPropertyName("assets")]
    public List<GiteaAssetDto> Assets { get; set; } = [];
}

/// <summary>
/// Gitea Release 资产 DTO
/// </summary>
public sealed class GiteaAssetDto {
    /// <summary>文件名</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>下载 URL（Gitea 原生字段，优先）</summary>
    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    /// <summary>浏览器下载 URL（GitHub 兼容字段，fallback）</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    /// <summary>文件大小（字节）</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

/// <summary>
/// GitLab Release JSON 响应 DTO — 镜像 GitLab API /releases 格式（数组）
/// </summary>
public sealed class GitLabReleaseDto {
    /// <summary>标签名</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>发布时间</summary>
    [JsonPropertyName("released_at")]
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>Release 描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>资产容器</summary>
    [JsonPropertyName("assets")]
    public GitLabAssetsDto? Assets { get; set; }
}

/// <summary>
/// GitLab Release 资产容器 DTO — 包含 links 数组
/// </summary>
public sealed class GitLabAssetsDto {
    /// <summary>下载链接列表</summary>
    [JsonPropertyName("links")]
    public List<GitLabLinkDto> Links { get; set; } = [];
}

/// <summary>
/// GitLab Release 下载链接 DTO
/// </summary>
public sealed class GitLabLinkDto {
    /// <summary>文件名</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>直接资产 URL（优先）</summary>
    [JsonPropertyName("direct_asset_url")]
    public string? DirectAssetUrl { get; set; }

    /// <summary>通用 URL（fallback）</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
