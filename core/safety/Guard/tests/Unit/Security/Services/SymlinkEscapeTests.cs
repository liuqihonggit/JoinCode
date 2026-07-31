#pragma warning disable JCC9001, JCC9002

namespace Guard.Tests.Security.Services;

using JoinCode.Abstractions.Security.Sandbox;

public sealed class SymlinkEscapeTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public async Task SoftSandbox_ResolvePath_SymlinkOutsideSandbox_ShouldResolveToSandboxPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jcc-symlink-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var outsideDir = Path.Combine(Path.GetTempPath(), $"jcc-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);

        try
        {
            var outsideFile = Path.Combine(outsideDir, "secret.txt");
            File.WriteAllText(outsideFile, "sensitive data");

            var symlinkPath = Path.Combine(tempRoot, "link-to-outside");
            try
            {
                Directory.CreateSymbolicLink(symlinkPath, outsideDir);
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            if (!Directory.Exists(symlinkPath))
            {
                return;
            }

            var linkDirInfo = new DirectoryInfo(symlinkPath);
            if (!linkDirInfo.Exists || !linkDirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }

            var provider = new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance);
            var options = new SandboxOptions
            {
                Type = SandboxType.Soft,
                SandboxRoot = tempRoot,
                RestrictFileSystem = true
            };

            var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

            var pathThroughSymlink = Path.Combine(symlinkPath, "secret.txt");
            var resolved = provider.ResolvePath(pathThroughSymlink, info.SandboxId);

            var sandboxRoot = Path.GetFullPath(tempRoot);
            resolved.Should().StartWith(sandboxRoot, because: "符号链接指向沙箱外时，ResolvePath应降级重定向到沙箱内");
            resolved.Should().Contain("redirected", because: "符号链接逃逸应降级到redirected目录");

            await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch (Exception ex) { Console.WriteLine($"清理失败: {ex.Message}"); }
            try { Directory.Delete(outsideDir, true); } catch (Exception ex) { Console.WriteLine($"清理失败: {ex.Message}"); }
        }
    }

    [Fact]
    public async Task SoftSandbox_ResolvePath_SymlinkInsideSandbox_ShouldResolveCorrectly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jcc-symlink-inner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var targetDir = Path.Combine(tempRoot, "target");
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "file.txt"), "content");

        try
        {
            var symlinkPath = Path.Combine(tempRoot, "link-to-target");
            try
            {
                Directory.CreateSymbolicLink(symlinkPath, targetDir);
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            if (!Directory.Exists(symlinkPath))
            {
                return;
            }

            var provider = new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance);
            var options = new SandboxOptions
            {
                Type = SandboxType.Soft,
                SandboxRoot = tempRoot,
                RestrictFileSystem = true
            };

            var info = await provider.CreateSandboxAsync(options).ConfigureAwait(true);

            var pathThroughSymlink = Path.Combine(symlinkPath, "file.txt");
            var resolved = provider.ResolvePath(pathThroughSymlink, info.SandboxId);

            var sandboxRoot = Path.GetFullPath(tempRoot);
            resolved.Should().StartWith(sandboxRoot, because: "沙箱内符号链接解析后仍应在沙箱内");

            await provider.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch (Exception ex) { Console.WriteLine($"清理失败: {ex.Message}"); }
        }
    }
}
