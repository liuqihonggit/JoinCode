namespace JoinCode.Cli;

/// <summary>
/// Onboarding 完成状态持久化 - 使用 JSON 文件存储完成标记，兼容 NativeAOT
/// </summary>
[Register]
public sealed partial class OnboardingStatePersistence
{
    private readonly string _filePath;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;

    public OnboardingStatePersistence(IFileSystem fs, IClockService? clock = null)
    {
        _fs = fs;
        _clock = clock ?? SystemClockService.Instance;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder);
        _filePath = Path.Combine(appDataPath, "onboarding_complete.json");
    }

    internal OnboardingStatePersistence(IFileSystem fs, string filePath, IClockService? clock = null)
    {
        _fs = fs;
        _filePath = filePath;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 检查 Onboarding 是否已完成
    /// </summary>
    public async Task<bool> IsCompleteAsync(CancellationToken ct = default)
    {
        if (!_fs.FileExists(_filePath))
        {
            return false;
        }

        try
        {
            await using var stream = _fs.CreateStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var data = await JsonSerializer.DeserializeAsync(stream, OnboardingPersistenceContext.Default.OnboardingCompletionData, ct).ConfigureAwait(false);
            return data?.IsComplete ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 标记 Onboarding 已完成
    /// </summary>
    public async Task MarkCompleteAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        var data = new OnboardingCompletionData { IsComplete = true, CompletedAt = _clock.GetUtcNowOffset() };
        await using var stream = _fs.CreateStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, data, OnboardingPersistenceContext.Default.OnboardingCompletionData, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 重置 Onboarding 完成状态（删除标记文件）
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (!_fs.FileExists(_filePath))
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        await Task.Run(() => _fs.DeleteFile(_filePath), ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Onboarding 完成状态数据
/// </summary>
public sealed class OnboardingCompletionData
{
    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }
}

[JsonSerializable(typeof(OnboardingCompletionData))]
public sealed partial class OnboardingPersistenceContext : JsonSerializerContext;
