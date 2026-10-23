namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// workspace/changes 收集器与存储测试（TASK066 C7）：轮次起点快照 → 轮次终点对比，
/// 产出 deliverables 插件消费的 summary/diff 契约（文件数、增删行、hunk 行前缀、binary/oversized）。
/// </summary>
public sealed class DshTurnChangeCollectorTests {
    private const string Cwd = "/ws";

    private static string Arguments(object value) => JsonSerializer.SerializeToElement(value).GetRawText();

    [Fact]
    public async Task Collect_EditExistingFile_ProducesTextDiffAndLineCounts() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "one\ntwo\nthree\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("edit", Arguments(new { file_path = "a.txt" }));
        await fs.WriteAllTextAsync("/ws/a.txt", "one\nTWO\nthree\nfour\n");
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("编辑已有文件应产出改动摘要");
        value.Turn.Should().Be(1);
        value.Total.Should().Be(1);
        value.Added.Should().Be(2);
        value.Deleted.Should().Be(1);
        var file = value.Files.Should().ContainSingle().Subject;
        file.Path.Should().Be("/ws/a.txt");
        file.Display.Should().Be("a.txt");
        file.Added.Should().Be(2);
        file.Deleted.Should().Be(1);
        file.Binary.Should().BeNull();
        file.Oversized.Should().BeNull();

        var diff = value.Diffs.Should().ContainSingle().Subject;
        diff.Kind.Should().Be("text");
        diff.Path.Should().Be("/ws/a.txt");
        diff.Display.Should().Be("a.txt");
        diff.Before.Should().BeTrue();
        diff.After.Should().BeTrue();
        diff.Coarse.Should().BeFalse();
        var hunks = diff.Hunks ?? throw new Xunit.Sdk.XunitException("文本差异必须带 hunk");
        hunks.Should().NotBeEmpty();
        var lines = hunks.SelectMany(hunk => hunk.Lines).ToList();
        lines.Should().Contain("-two").And.Contain("+TWO").And.Contain("+four");
        lines.Should().OnlyContain(line => line.Length > 0 && (line[0] == '+' || line[0] == '-' || line[0] == ' '));
        hunks[0].OldStart.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Collect_CreateFile_MarksBeforeFalseAndCountsAdditions() {
        var fs = new InMemoryFileSystem();
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("write", Arguments(new { file_path = "new.txt", content = "a\nb\n" }));
        await fs.WriteAllTextAsync("/ws/new.txt", "a\nb\n");
        var summary = await collector.CollectAsync(2);

        var value = summary ?? throw new Xunit.Sdk.XunitException("新建文件应产出改动摘要");
        value.Added.Should().Be(2);
        value.Deleted.Should().Be(0);
        var file = value.Files.Should().ContainSingle().Subject;
        file.Display.Should().Be("new.txt");
        var diff = value.Diffs.Should().ContainSingle().Subject;
        diff.Before.Should().BeFalse();
        diff.After.Should().BeTrue();
        diff.Coarse.Should().BeFalse();
        (diff.Hunks ?? throw new Xunit.Sdk.XunitException("新建文件差异必须带 hunk"))
            .SelectMany(hunk => hunk.Lines).Should().OnlyContain(line => line.StartsWith('+'));
    }

    [Fact]
    public async Task Collect_DeleteFile_MarksAfterFalseAndCountsDeletions() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/gone.txt", "a\nb\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("file_delete", Arguments(new { file_path = "gone.txt" }));
        fs.DeleteFile("/ws/gone.txt");
        var summary = await collector.CollectAsync(3);

        var value = summary ?? throw new Xunit.Sdk.XunitException("删除文件应产出改动摘要");
        value.Added.Should().Be(0);
        value.Deleted.Should().Be(2);
        var diff = value.Diffs.Should().ContainSingle().Subject;
        diff.Before.Should().BeTrue();
        diff.After.Should().BeFalse();
        (diff.Hunks ?? throw new Xunit.Sdk.XunitException("删除文件差异必须带 hunk"))
            .SelectMany(hunk => hunk.Lines).Should().OnlyContain(line => line.StartsWith('-'));
    }

    [Fact]
    public async Task Collect_UnchangedFile_ReturnsNull() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "same\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("edit", Arguments(new { file_path = "a.txt" }));

        (await collector.CollectAsync(1)).Should().BeNull("文件未变化时不发 workspace/changes");
    }

    [Fact]
    public async Task Collect_NonMutationTool_DoesNotObserve() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "before\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("read", Arguments(new { file_path = "a.txt" }));
        await fs.WriteAllTextAsync("/ws/a.txt", "after\n");

        (await collector.CollectAsync(1)).Should().BeNull("只读工具不进入改动收集");
    }

    [Fact]
    public async Task Collect_RepeatedObservation_DiffsTurnStartToTurnEnd() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "a\nb\nc\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("edit", Arguments(new { file_path = "a.txt" }));
        await fs.WriteAllTextAsync("/ws/a.txt", "a\nB\nc\n");
        await collector.ObserveAsync("edit", Arguments(new { file_path = "a.txt" }));
        await fs.WriteAllTextAsync("/ws/a.txt", "a\nB\nC\n");
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("多编辑应合并为一条文件改动");
        value.Total.Should().Be(1);
        value.Added.Should().Be(2);
        value.Deleted.Should().Be(2);
        value.Diffs.Should().ContainSingle();
    }

    [Fact]
    public async Task Collect_BatchEdit_ObservesEveryFilePath() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "a\n");
        await fs.WriteAllTextAsync("/ws/b.txt", "b\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("file_batch_edit", Arguments(new { file_paths = new[] { "a.txt", "b.txt" } }));
        await fs.WriteAllTextAsync("/ws/a.txt", "A\n");
        await fs.WriteAllTextAsync("/ws/b.txt", "B\n");
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("批量编辑应产出两条文件改动");
        value.Total.Should().Be(2);
        value.Files.Select(file => file.Display).Should().Equal("a.txt", "b.txt");
    }

    [Fact]
    public async Task Collect_ApplyPatch_ObservesPatchTargets() {
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory("/ws/src");
        await fs.WriteAllTextAsync("/ws/src/a.txt", "old\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);
        const string patch = "--- a/src/a.txt\n+++ b/src/a.txt\n@@ -1 +1 @@\n-old\n+new\n";

        await collector.ObserveAsync("apply_patch", Arguments(new { patch, dry_run = false }));
        await fs.WriteAllTextAsync("/ws/src/a.txt", "new\n");
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("apply_patch 应收集 +++ 目标文件");
        value.Files.Should().ContainSingle().Which.Display.Should().Be("src/a.txt");
        value.Added.Should().Be(1);
        value.Deleted.Should().Be(1);
    }

    [Fact]
    public async Task Collect_ApplyPatchDryRun_DoesNotObserve() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/ws/a.txt", "old\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("apply_patch", Arguments(new {
            patch = "--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+new\n",
            dry_run = true,
        }));
        await fs.WriteAllTextAsync("/ws/a.txt", "new\n");

        (await collector.CollectAsync(1)).Should().BeNull("dry_run 不写盘");
    }

    [Fact]
    public async Task Collect_BinaryFile_MarksBinaryWithoutHunks() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllBytesAsync("/ws/b.bin", [0, 1, 2]);
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("write", Arguments(new { file_path = "b.bin" }));
        await fs.WriteAllBytesAsync("/ws/b.bin", [0, 1, 3]);
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("二进制改动应产出摘要");
        var file = value.Files.Should().ContainSingle().Subject;
        file.Binary.Should().BeTrue();
        var diff = value.Diffs.Should().ContainSingle().Subject;
        diff.Kind.Should().Be("binary");
        diff.Hunks.Should().BeEmpty("二进制文件不提供逐行对比");
    }

    [Fact]
    public async Task Collect_OversizedFile_MarksOversizedWithoutDiff() {
        var fs = new InMemoryFileSystem();
        var before = new string('a', (1024 * 1024) + 8);
        await fs.WriteAllTextAsync("/ws/big.txt", before);
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("write", Arguments(new { file_path = "big.txt" }));
        await fs.WriteAllTextAsync("/ws/big.txt", before + "tail");
        var summary = await collector.CollectAsync(1);

        var value = summary ?? throw new Xunit.Sdk.XunitException("超限文件改动应产出摘要");
        var file = value.Files.Should().ContainSingle().Subject;
        file.Oversized.Should().BeTrue();
        var diff = value.Diffs.Should().ContainSingle().Subject;
        diff.Kind.Should().Be("oversized");
        diff.Hunks.Should().BeEmpty("超限文件不提供逐行对比");
    }

    [Fact]
    public async Task Collect_FileOutsideCwd_UsesNormalizedAbsoluteDisplay() {
        var fs = new InMemoryFileSystem();
        await fs.WriteAllTextAsync("/outside/x.txt", "old\n");
        var collector = new DshTurnChangeCollector(fs, Cwd);

        await collector.ObserveAsync("edit", Arguments(new { file_path = "/outside/x.txt" }));
        await fs.WriteAllTextAsync("/outside/x.txt", "new\n");
        var summary = await collector.CollectAsync(1);

        var file = (summary ?? throw new Xunit.Sdk.XunitException("cwd 外改动也应收集")).Files.Should().ContainSingle().Subject;
        file.Path.Should().Be("/outside/x.txt");
        file.Display.Should().Be("/outside/x.txt");
    }

    [Fact]
    public void Store_RecordAndLookup_KeyedBySessionAndSeq() {
        var summary = new DshChangesSummary {
            Turn = 4,
            Total = 1,
            Added = 1,
            Deleted = 2,
            Files = [
                new DshChangedFile { Path = "/ws/a.txt", Display = "a.txt", Added = 1, Deleted = 2 },
            ],
            Diffs = [
                new DshChangesDiff {
                    Kind = "text",
                    Path = "/ws/a.txt",
                    Display = "a.txt",
                    Before = true,
                    After = true,
                    Coarse = false,
                    Hunks = [
                        new DshChangesHunk { OldStart = 1, OldLines = 2, NewStart = 1, NewLines = 1, Lines = ["-a", "+b"] },
                    ],
                },
            ],
        };
        var store = new DshWorkspaceChangesStore();

        store.Record("s1", 9, summary);

        store.Summary("s1", 9).Should().BeSameAs(summary);
        store.Diff("s1", 9, 0).Should().BeSameAs(summary.Diffs[0]);
        store.Diff("s1", 9, 1).Should().BeNull();
        store.Diff("s1", 9, -1).Should().BeNull();
        store.Summary("s1", 10).Should().BeNull();
        store.Summary("s2", 9).Should().BeNull();
    }

    [Fact]
    public void Summary_SerializesDeliverablesContract() {
        var summary = new DshChangesSummary {
            Turn = 3,
            Total = 1,
            Added = 1,
            Deleted = 0,
            Files = [new DshChangedFile { Path = "/ws/a.txt", Display = "a.txt", Added = 1, Deleted = 0 }],
            Diffs = [
                new DshChangesDiff {
                    Kind = "text",
                    Path = "/ws/a.txt",
                    Display = "a.txt",
                    Before = false,
                    After = true,
                    Coarse = false,
                    Hunks = [new DshChangesHunk { OldStart = 0, OldLines = 0, NewStart = 1, NewLines = 1, Lines = ["+hello"] }],
                },
            ],
        };

        var json = JsonSerializer.Serialize(summary, DshJsonContext.Default.DshChangesSummary);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("turn").GetInt32().Should().Be(3);
        root.GetProperty("total").GetInt32().Should().Be(1);
        root.GetProperty("added").GetInt32().Should().Be(1);
        var file = root.GetProperty("files")[0];
        file.GetProperty("display").GetString().Should().Be("a.txt");
        file.TryGetProperty("binary", out _).Should().BeFalse("未标记的字段不得出现（前端只接受 true 或缺失）");
        file.TryGetProperty("oversized", out _).Should().BeFalse();

        var diffJson = JsonSerializer.Serialize(summary.Diffs[0], DshJsonContext.Default.DshChangesDiff);
        using var diffDocument = JsonDocument.Parse(diffJson);
        var hunk = diffDocument.RootElement.GetProperty("hunks")[0];
        hunk.GetProperty("oldStart").GetInt32().Should().Be(0);
        hunk.GetProperty("lines")[0].GetString().Should().Be("+hello");
    }
}
