namespace JoinCode.Gh;

internal static class GhHost {
    private static GitHubToolHandlers? s_handler;

    internal static GitHubToolHandlers BuildHandler() {
        if (s_handler is not null) return s_handler;
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var httpClient = new HttpClient();
        var apiClient = new IO.ProcessService.GitHubApiClient(httpClient, fs);
        var downloader = new SimpleDownloader(httpClient);
        s_handler = new GitHubToolHandlers(downloader, fs, apiClient);
        return s_handler;
    }
}

internal sealed class SimpleDownloader(HttpClient http) : IDownloader {
    public IDownloadSession StartDownload(
        string url, string filePath, DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) {
        throw new NotImplementedException("SimpleDownloader 不支持下载，请使用完整 jcc 进行 Release asset 下载");
    }
}
