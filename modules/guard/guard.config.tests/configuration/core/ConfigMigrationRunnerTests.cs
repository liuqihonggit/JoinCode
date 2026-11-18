namespace Core.Tests.Configuration;

/// <summary>
/// 配置迁移框架（TASK091）— 版本号 + 迁移目录 + 每步落盘 + 失败停驻。
/// <para>原来的迁移是一个手写方法，靠「搬完就没得搬」的结构幂等自保，这只对搬键成立；
/// 改值、合并字段、换格式都无法自证跑过没有。框架把「读版本 → 按序执行 → 每步落盘 → 失败停在这里」
/// 收进一处，这里锁的就是这几条行为本身。</para>
/// <para>与其它配置测试共享 <c>AppDataConstants</c> 全局状态，必须串行。</para>
/// </summary>
[Collection("AppDataConstantsCollection")]
public sealed class ConfigMigrationRunnerTests : IDisposable {
    private readonly string? _originalAppDataFolder;
    private readonly string _tempAppDataDir;
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private bool _disposed;

    /// <summary>准备隔离的 AppData 目录 — 不碰用户真实 ~/.jcc。</summary>
    public ConfigMigrationRunnerTests() {
        _originalAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder);

        _tempAppDataDir = $"/test/jcc-test-migration-{Guid.NewGuid():N}";
        _fs.CreateDirectory(_tempAppDataDir);
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, _tempAppDataDir);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, _originalAppDataFolder);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    private string MarkerPath => AppDataConstants.Paths.ConfigMigrationsFilePath;

    /// <summary>读版本标记；没写出来就直接失败 —— 那正是一条测试要防的回归。</summary>
    private async Task<ConfigMigrationState> ReadMarkerAsync() {
        if (!_fs.FileExists(MarkerPath))
            throw new InvalidOperationException($"迁移没有写出版本标记: {MarkerPath}");

        var text = await _fs.ReadAllTextAsync(MarkerPath, CancellationToken.None);
        return JsonSerializer.Deserialize(text, ConfigIndentedJsonContext.Default.ConfigMigrationState)
            ?? throw new InvalidOperationException("版本标记无法反序列化");
    }

    [Fact]
    public async Task RunAsync_AppliesPendingMigrationsInVersionOrder() {
        var order = new List<int>();
        var report = await ConfigMigrationRunner.RunAsync(_fs, [
            new FakeMigration(2, order),
            new FakeMigration(1, order),
        ]);

        order.Should().Equal([1, 2], "按版本号升序执行，与登记顺序无关");
        report.ToVersion.Should().Be(2);
        report.Failed.Should().BeFalse();

        var marker = await ReadMarkerAsync();
        marker.AppliedVersion.Should().Be(2);
        marker.History.Select(h => h.Version).Should().Equal([1, 2]);
    }

    [Fact]
    public async Task RunAsync_SkipsMigrationsAtOrBelowAppliedVersion() {
        await ConfigMigrationRunner.RunAsync(_fs, [new FakeMigration(1, [])]);

        var order = new List<int>();
        var report = await ConfigMigrationRunner.RunAsync(_fs, [
            new FakeMigration(1, order),
            new FakeMigration(2, order),
        ]);

        order.Should().Equal([2], "v1 已应用过，不能再跑一遍");
        report.FromVersion.Should().Be(1);
        report.ToVersion.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_NothingToDo_WhenAlreadyLatest() {
        await ConfigMigrationRunner.RunAsync(_fs, [new FakeMigration(1, [])]);

        var order = new List<int>();
        var report = await ConfigMigrationRunner.RunAsync(_fs, [new FakeMigration(1, order)]);

        order.Should().BeEmpty();
        report.Applied.Should().BeEmpty();
        report.ToVersion.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_StopsAtFailingMigration_AndKeepsVersionAtLastSuccess() {
        var order = new List<int>();
        var report = await ConfigMigrationRunner.RunAsync(_fs, [
            new FakeMigration(1, order),
            new FakeMigration(2, order, fail: true),
            new FakeMigration(3, order),
        ]);

        order.Should().Equal([1, 2], "失败即停，v3 不能越过 v2 先跑");
        report.Failed.Should().BeTrue();
        report.FailedVersion.Should().Be(2);
        report.ToVersion.Should().Be(1, "失败的迁移不推进版本 —— 否则下次启动不会重试");

        var marker = await ReadMarkerAsync();
        marker.AppliedVersion.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_RetriesFailedMigrationOnNextRun() {
        var ordered = new IConfigMigration[] {
            new FakeMigration(1, []),
            new ToggleFailMigration(),
        };

        await ConfigMigrationRunner.RunAsync(_fs, ordered);
        (await ReadMarkerAsync()).AppliedVersion.Should().Be(1);

        // 第二次运行：v2 这次不再失败，应当补上
        var report = await ConfigMigrationRunner.RunAsync(_fs, ordered);

        report.Failed.Should().BeFalse();
        report.FromVersion.Should().Be(1);
        report.ToVersion.Should().Be(2);
        (await ReadMarkerAsync()).AppliedVersion.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_CorruptMarker_TreatedAsNeverMigrated() {
        _fs.CreateDirectory(AppDataConstants.Paths.JccDirectory);
        await _fs.WriteAllTextAsync(MarkerPath, "{ this is not json", CancellationToken.None);

        var order = new List<int>();
        var report = await ConfigMigrationRunner.RunAsync(_fs, [new FakeMigration(1, order)]);

        order.Should().Equal([1], "标记坏了就当 v0 —— 迁移本身幂等，重跑一遍无害");
        report.ToVersion.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HistoryIsCappedButVersionKeepsAdvancing() {
        var many = Enumerable.Range(1, 14).Select(v => (IConfigMigration)new FakeMigration(v, [])).ToArray();

        var report = await ConfigMigrationRunner.RunAsync(_fs, many);

        report.ToVersion.Should().Be(14);

        var marker = await ReadMarkerAsync();
        marker.AppliedVersion.Should().Be(14);
        marker.History.Should().HaveCount(10, "只留最近几步，避免标记文件随时间无限增长");
        marker.History[^1].Version.Should().Be(14);
    }

    [Fact]
    public void ProductionMigrations_AreVersionedAndNamed() {
        ConfigMigrationRunner.Migrations.Should().NotBeEmpty();
        ConfigMigrationRunner.Migrations.Select(m => m.Version).Should().OnlyHaveUniqueItems();
        ConfigMigrationRunner.Migrations.Should().OnlyContain(m => m.Version > 0 && !string.IsNullOrWhiteSpace(m.Name));
        ConfigMigrationRunner.LatestVersion.Should().Be(ConfigMigrationRunner.Migrations.Max(m => m.Version));
    }

    /// <summary>可控的假迁移 —— 记录执行顺序，可指定失败。</summary>
    private sealed class FakeMigration(int version, List<int> order, bool fail = false) : IConfigMigration {
        public int Version => version;
        public string Name => $"fake-{version}";

        public Task<ConfigMigrationOutcome> ApplyAsync(IFileSystem fs, ILogger? logger, CancellationToken cancellationToken) {
            order.Add(version);
            if (fail)
                throw new InvalidOperationException($"fake migration {version} failed");
            return Task.FromResult(ConfigMigrationOutcome.None);
        }
    }

    /// <summary>第一次抛、第二次成功的迁移 —— 验证「失败后下次会补上」。</summary>
    private sealed class ToggleFailMigration : IConfigMigration {
        private bool _failedOnce;

        public int Version => 2;
        public string Name => "toggle-fail";

        public Task<ConfigMigrationOutcome> ApplyAsync(IFileSystem fs, ILogger? logger, CancellationToken cancellationToken) {
            if (!_failedOnce) {
                _failedOnce = true;
                throw new InvalidOperationException("first attempt fails");
            }
            return Task.FromResult(ConfigMigrationOutcome.None);
        }
    }
}
