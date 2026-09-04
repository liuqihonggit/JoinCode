namespace IO.Services.Update;

/// <summary>
/// 更新源工厂 — 根据 UpdateSourceType 创建对应的 IUpdateSource 实现
/// > ADR: 0064
/// </summary>
public static class UpdateSourceFactory
{
    /// <summary>
    /// 创建更新源
    /// </summary>
    /// <param name="config">更新源配置</param>
    /// <param name="httpClient">HTTP 客户端（Static/HttpApi/GitHubMirror 需要）</param>
    /// <param name="fs">文件系统（LocalFile 需要）</param>
    /// <param name="logger">日志器</param>
    /// <returns>更新源实例</returns>
    public static IUpdateSource Create(
        UpdateSourceConfig config,
        HttpClient httpClient,
        IFileSystem fs,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(fs);

        var sourceType = config.GetSourceType();
        var manifestUrl = config.GetManifestUrl();

        return sourceType switch
        {
            UpdateSourceType.Static => new StaticFileUpdateSource(
                httpClient,
                manifestUrl,
                logger as ILogger<StaticFileUpdateSource>),

            UpdateSourceType.LocalFile => new LocalFileUpdateSource(
                manifestUrl,
                fs,
                logger as ILogger<LocalFileUpdateSource>),

            UpdateSourceType.HttpApi => new HttpApiUpdateSource(
                httpClient,
                manifestUrl,
                logger as ILogger<HttpApiUpdateSource>),

            UpdateSourceType.GitHubMirror => new GitHubMirrorUpdateSource(
                httpClient,
                manifestUrl,
                logger as ILogger<GitHubMirrorUpdateSource>),

            UpdateSourceType.GitLabMirror => new GitLabMirrorUpdateSource(
                httpClient,
                manifestUrl,
                logger as ILogger<GitLabMirrorUpdateSource>),

            UpdateSourceType.GiteaMirror => new GiteaMirrorUpdateSource(
                httpClient,
                manifestUrl,
                logger as ILogger<GiteaMirrorUpdateSource>),

            _ => throw new ArgumentOutOfRangeException(nameof(config), sourceType, "不支持的更新源类型")
        };
    }
}
