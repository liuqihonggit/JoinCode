using JoinCode.Abstractions.Hooks;

namespace Brain.Tests.Context.Compact;

/// <summary>
/// MagicDocsManager 单元测试 — 对齐 TS magicDocs.ts::trackedMagicDocs
/// 验证 FileRead 监听、PostSampling 回调、追踪计数、清除等
/// </summary>
public sealed class MagicDocsManagerTests
{
    private static Testing.Common.Services.InMemoryFileSystem CreateFileSystem()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        fs.SetCurrentDirectory("/test/project");
        return fs;
    }

    private static MagicDocsManager CreateManager(
        Testing.Common.Services.InMemoryFileSystem fs,
        IForkSubAgentManager? forkManager = null)
    {
        return new MagicDocsManager(
            fs,
            forkManager: forkManager);
    }

    [Fact]
    public void OnFileRead_WithMagicDocHeader_TracksDoc()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/docs/arch.md",
            Content = "# MAGIC DOC: Architecture Guide\nSome content"
        });

        manager.TrackedCount.Should().Be(1);
    }

    [Fact]
    public void OnFileRead_WithoutMagicDocHeader_DoesNotTrack()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/docs/normal.md",
            Content = "# Regular File\nNo magic here"
        });

        manager.TrackedCount.Should().Be(0);
    }

    [Fact]
    public void OnFileRead_SameFileUpdated_UpdatesEntry()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Old Title\nContent"
        });

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: New Title\nUpdated"
        });

        manager.TrackedCount.Should().Be(1);
    }

    [Fact]
    public void OnFileRead_MultipleDifferentFiles_TracksAll()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/a.md",
            Content = "# MAGIC DOC: Doc A\nA"
        });

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/b.md",
            Content = "# MAGIC DOC: Doc B\nB"
        });

        manager.TrackedCount.Should().Be(2);
    }

    [Fact]
    public async Task OnPostSamplingAsync_NonReplSource_DoesNothing()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        var context = new PostSamplingContext
        {
            QuerySource = "subagent",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        manager.TrackedCount.Should().Be(1);
    }

    [Fact]
    public async Task OnPostSamplingAsync_NoTrackedDocs_DoesNothing()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        manager.TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task OnPostSamplingAsync_FileDeleted_RemovesTrackedDoc()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/guide.md", "# MAGIC DOC: Guide\nContent");
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        manager.TrackedCount.Should().Be(1);

        fs.DeleteFile("/test/project/guide.md");

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        manager.TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task OnPostSamplingAsync_FileNoLongerMagicDoc_RemovesTrackedDoc()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/guide.md", "# MAGIC DOC: Guide\nContent");
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        fs.WriteAllText("/test/project/guide.md", "# Regular File\nNo magic anymore");

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        manager.TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithForkManager_CallsForkAsync()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/guide.md", "# MAGIC DOC: Guide\nContent");
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Completed });

        var manager = CreateManager(fs, forkMock.Object);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(
            It.Is<ForkOptions>(o =>
                o.ParentSessionId == "session-1" &&
                o.TaskDescription == "magic_docs" &&
                o.AllowedTools!.Contains("Edit")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithoutForkManager_DoesNotThrow()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/guide.md", "# MAGIC DOC: Guide\nContent");
        var manager = CreateManager(fs, forkManager: null);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        var act = async () => await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void Clear_RemovesAllTrackedDocs()
    {
        var fs = CreateFileSystem();
        var manager = CreateManager(fs);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/a.md",
            Content = "# MAGIC DOC: A\nA"
        });

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/b.md",
            Content = "# MAGIC DOC: B\nB"
        });

        manager.TrackedCount.Should().Be(2);
        manager.Clear();
        manager.TrackedCount.Should().Be(0);
    }

    [Fact]
    public async Task OnPostSamplingAsync_ForkFailure_DoesNotThrow()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/guide.md", "# MAGIC DOC: Guide\nContent");
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fork failed"));

        var manager = CreateManager(fs, forkMock.Object);

        manager.OnFileRead(new FileReadEventArgs
        {
            FilePath = "/test/project/guide.md",
            Content = "# MAGIC DOC: Guide\nContent"
        });

        var context = new PostSamplingContext
        {
            QuerySource = "repl_main_thread",
            SessionId = "session-1",
            CancellationToken = CancellationToken.None
        };

        var act = async () => await manager.OnPostSamplingAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }
}
