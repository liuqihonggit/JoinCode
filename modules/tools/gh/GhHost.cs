namespace JoinCode.Gh;

internal static class GhHost {
    private static GitHubToolHandlers? s_handler;

    internal static GitHubToolHandlers BuildHandler() {
        if (s_handler is not null) return s_handler;
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var httpClient = new HttpClient();
        var apiClient = new IO.ProcessService.GitHubApiClient(httpClient, fs);
        var downloader = new SimpleDownloader(httpClient);
        var processService = IO.ProcessService.ProcessServiceFactory.Create();
        var git = new IO.ProcessService.GitCommandRunner(processService);
        var kvDir = System.IO.Path.Combine(JoinCode.Abstractions.Configuration.AppData.AppDataConstants.Paths.GhCacheDirectory, "kvstore");
        System.IO.Directory.CreateDirectory(kvDir);
        var kvStore = new Infrastructure.Persistence.PithosKvStore(kvDir);
        s_handler = new GitHubToolHandlers(downloader, fs, apiClient, git, kvStore: kvStore);
        return s_handler;
    }
}

internal sealed class SimpleDownloader(HttpClient http) : IDownloader {
    public IDownloadSession StartDownload(
        string url, string filePath, DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        using var src = resp.Content.ReadAsStream(cancellationToken);
        using var dst = File.Create(filePath);
        src.CopyTo(dst);
        sw.Stop();
        var info = new FileInfo(filePath);
        return new SimpleDownloadSession(new DownloadResult(
            Success: true, FilePath: filePath, TotalBytes: total,
            DownloadedBytes: info.Length, Elapsed: sw.Elapsed,
            FinalState: DownloadState.Completed));
    }
}

internal sealed class SimpleDownloadSession(DownloadResult result) : IDownloadSession {
    public DownloadState State => result.FinalState;
    public Task PauseAsync(CancellationToken ct = default) => throw new NotSupportedException("SimpleDownloader 不支持暂停");
    public Task ResumeAsync(CancellationToken ct = default) => throw new NotSupportedException("SimpleDownloader 不支持恢复");
    public Task CancelAsync(CancellationToken ct = default) => throw new NotSupportedException("SimpleDownloader 不支持取消");
    public Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default) => Task.FromResult(result);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
