namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 更新源类型 — 决定从哪里获取版本清单和下载二进制
/// 通过 settings.json 的 update.sourceType 或 JCC_UPDATE_SOURCE_TYPE 环境变量配置
/// > ADR: 0064
/// </summary>
public enum UpdateSourceType
{
    /// <summary>
    /// 静态文件托管 — 服务器只托管 manifest.json + exe 二进制，无服务端逻辑
    /// </summary>
    [EnumValue("static")] Static,

    /// <summary>
    /// HTTP API 服务器 — 动态端点 /api/version/check + /api/download/{version}
    /// </summary>
    [EnumValue("api")] HttpApi,

    /// <summary>
    /// GitHub Release 镜像代理 — 镜像 GitHub API 响应格式，解决国内访问慢
    /// </summary>
    [EnumValue("github-mirror")] GitHubMirror,

    /// <summary>
    /// GitLab Release 镜像代理 — 从 GitLab API 拉取 Release，转换为 UpdateManifest
    /// </summary>
    [EnumValue("gitlab-mirror")] GitLabMirror,

    /// <summary>
    /// Gitea Release 镜像代理 — 从 Gitea API 拉取 Release，转换为 UpdateManifest
    /// </summary>
    [EnumValue("gitea-mirror")] GiteaMirror,

    /// <summary>
    /// 本地文件清单 — 从本地路径或 UNC 路径读取 manifest.json + exe
    /// </summary>
    [EnumValue("local")] LocalFile,
}
