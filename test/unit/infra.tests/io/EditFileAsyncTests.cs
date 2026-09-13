namespace Infra.IO.Tests;

/// <summary>
/// IFileSystem.EditFileAsync 并发安全与原子性测试。
/// 验证 per-file AsyncLock 串行化：同一文件无丢失更新，不同文件可并行。
/// </summary>
public class EditFileAsyncTests
{
    [Fact]
    public async Task EditFileAsync_ConcurrentSameFile_NoLostUpdates()
    {
        var fs = new InMemoryFileSystem();
        var path = "/test/concurrent.txt";
        fs.WriteAllText(path, "");

        const int taskCount = 50;
        var tasks = new Task[taskCount];
        for (var i = 0; i < taskCount; i++)
        {
            tasks[i] = fs.EditFileAsync<int>(path, async (bytes, ct) =>
            {
                var content = Encoding.UTF8.GetString(bytes);
                var lineCount = string.IsNullOrEmpty(content) ? 0 : content.Split('\n').Length;
                var newContent = lineCount == 0 ? "0" : content + "\n" + lineCount;
                var newBytes = Encoding.UTF8.GetBytes(newContent);
                return (newBytes, lineCount);
            }, default);
        }
        await Task.WhenAll(tasks);

        var finalContent = fs.ReadAllText(path);
        var lines = finalContent.Split('\n');
        lines.Length.Should().Be(taskCount);
    }

    [Fact]
    public async Task EditFileAsync_DifferentFiles_Parallel()
    {
        var fs = new InMemoryFileSystem();
        var path1 = "/test/file1.txt";
        var path2 = "/test/file2.txt";
        fs.WriteAllText(path1, "a");
        fs.WriteAllText(path2, "b");

        var task1 = fs.EditFileAsync<int>(path1, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (Encoding.UTF8.GetBytes(content + "1"), 1);
        }, default);
        var task2 = fs.EditFileAsync<int>(path2, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (Encoding.UTF8.GetBytes(content + "2"), 2);
        }, default);

        await Task.WhenAll(task1, task2);

        fs.ReadAllText(path1).Should().Be("a1");
        fs.ReadAllText(path2).Should().Be("b2");
    }

    [Fact]
    public async Task EditFileAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var fs = new InMemoryFileSystem();
        var path = "/test/nonexistent.txt";

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fs.EditFileAsync<int>(path, async (bytes, ct) => (bytes, 0), default));
    }

    [Fact]
    public async Task EditFileAsync_TransformReturnsNull_NoWrite()
    {
        var fs = new InMemoryFileSystem();
        var path = "/test/skip.txt";
        fs.WriteAllText(path, "original");

        var result = await fs.EditFileAsync<int>(path, async (bytes, ct) => (null, 42), default);

        result.Should().Be(42);
        fs.ReadAllText(path).Should().Be("original");
    }

    [Fact]
    public async Task EditFileAsync_TransformReceivesCurrentContent()
    {
        var fs = new InMemoryFileSystem();
        var path = "/test/receive.txt";
        fs.WriteAllText(path, "hello world");

        var receivedContent = await fs.EditFileAsync<string>(path, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (null, content);
        }, default);

        receivedContent.Should().Be("hello world");
    }

    [Fact]
    public async Task EditFileAsync_SequentialEdits_ComposeCorrectly()
    {
        var fs = new InMemoryFileSystem();
        var path = "/test/sequential.txt";
        fs.WriteAllText(path, "0");

        await fs.EditFileAsync<int>(path, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (Encoding.UTF8.GetBytes(content + "1"), 1);
        }, default);

        await fs.EditFileAsync<int>(path, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (Encoding.UTF8.GetBytes(content + "2"), 2);
        }, default);

        await fs.EditFileAsync<int>(path, async (bytes, ct) =>
        {
            var content = Encoding.UTF8.GetString(bytes);
            return (Encoding.UTF8.GetBytes(content + "3"), 3);
        }, default);

        fs.ReadAllText(path).Should().Be("0123");
    }
}
