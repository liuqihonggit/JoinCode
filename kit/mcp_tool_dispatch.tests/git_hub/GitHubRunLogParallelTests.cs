namespace McpToolDispatch.Tests;

/// <summary>
/// GitHubRunLogFilterRunner 并行下载单元测试 — 验证多 job 并行获取 + 逗号分隔 job_id
/// </summary>
public sealed class GitHubRunLogParallelTest {
    /// <summary>
    /// 逗号分隔 job_id="123,456" 应并行下载两个 job 的日志,而非 fallthrough 到 run zip
    /// <para>红测试: 当前 StreamAndFilterAsync 的 long.TryParse("123,456") 失败,fallthrough 到 GetRunLogsAsync</para>
    /// </summary>
    [Fact]
    public async Task StreamAndFilterAsync_JobIdCommaSeparated_ParallelDownload() {
        var fake = new FakeGitHubApiClient(
            jobLogs: new() {
                [123] = ["job123-line1", "job123-line2"],
                [456] = ["job456-line1", "job456-line2"],
            },
            runLogLines: ["run-zip-fallback"]);
        var runner = new GitHubRunLogFilterRunner(fake);

        var result = await runner.StreamAndFilterAsync(
            owner: "foo", repo: "bar", runId: "999",
            jobId: "123,456", failedOnly: false,
            scope: "test", markers: null, filterLevel: null,
            maxLines: 100, ct: default);

        result.IsError.Should().BeFalse("逗号分隔 job_id 应成功解析");
        var text = result.Content.FirstOrDefault(c => c.Text is not null)?.Text ?? "";
        text.Should().Contain("job123-line1", "job 123 的日志应被下载");
        text.Should().Contain("job456-line1", "job 456 的日志应被下载");
        text.Should().NotContain("run-zip-fallback", "不应 fallthrough 到 run zip");
    }

    /// <summary>
    /// 多失败 job 并行获取 — 所有失败 job 的行都应出现(顺序不限,因并行交错)
    /// </summary>
    [Fact]
    public async Task GetFailedJobLogsAsync_MultipleFailedJobs_ReturnsAllLines() {
        var fake = new FakeGitHubApiClient(
            jobLogs: new() {
                [111] = ["job111-A", "job111-B"],
                [222] = ["job222-A", "job222-B"],
                [333] = ["job333-A", "job333-B"],
            },
            runLogLines: [],
            jobsJson: """{"jobs":[{"id":111,"conclusion":"failure"},{"id":222,"conclusion":"failure"},{"id":333,"conclusion":"failure"}]}""");
        var runner = new GitHubRunLogFilterRunner(fake);

        var lines = new List<string>();
        await foreach (var line in runner.GetFailedJobLogsAsync("foo", "bar", "999", default)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(6, "3 个失败 job 各 2 行");
        lines.Should().Contain("job111-A");
        lines.Should().Contain("job111-B");
        lines.Should().Contain("job222-A");
        lines.Should().Contain("job222-B");
        lines.Should().Contain("job333-A");
        lines.Should().Contain("job333-B");
    }

    /// <summary>最小 Fake IGitHubApiClient — 按 jobId 返回预设日志行</summary>
    private sealed class FakeGitHubApiClient : IGitHubApiClient {
        private readonly Dictionary<long, string[]> _jobLogs;
        private readonly string[] _runLogLines;
        private readonly string _jobsJson;

        public FakeGitHubApiClient(
            Dictionary<long, string[]> jobLogs,
            string[] runLogLines,
            string? jobsJson = null) {
            _jobLogs = jobLogs;
            _runLogLines = runLogLines;
            _jobsJson = jobsJson ?? """{"jobs":[]}""";
        }

        public Task<GitHubApiResponse> SendAsync(
            HttpMethod method, string path, string? body = null,
            IReadOnlyDictionary<string, string>? query = null,
            bool paginate = false, CancellationToken ct = default) {
            return Task.FromResult(new GitHubApiResponse {
                Success = true, StatusCode = 200, Body = _jobsJson
            });
        }

        public IAsyncEnumerable<string> GetJobLogsAsync(
            string owner, string repo, long jobId, CancellationToken ct = default)
            => GetJobLogsImpl(jobId, ct);

        private async IAsyncEnumerable<string> GetJobLogsImpl(
            long jobId, [EnumeratorCancellation] CancellationToken ct) {
            if (_jobLogs.TryGetValue(jobId, out var lines)) {
                foreach (var line in lines) {
                    ct.ThrowIfCancellationRequested();
                    yield return line;
                }
            }
        }

        public IAsyncEnumerable<string> GetRunLogsAsync(
            string owner, string repo, long runId, CancellationToken ct = default)
            => GetRunLogsImpl(ct);

        private async IAsyncEnumerable<string> GetRunLogsImpl(
            [EnumeratorCancellation] CancellationToken ct) {
            foreach (var line in _runLogLines) {
                ct.ThrowIfCancellationRequested();
                yield return line;
            }
        }

        public Task<GitHubApiResponse> UploadAssetAsync(
            string owner, string repo, long releaseId,
            string fileName, Stream fileStream, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
