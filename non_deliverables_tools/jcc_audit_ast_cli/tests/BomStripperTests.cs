using System.IO;
using System.Linq;
using System.Text;

#pragma warning disable JCC9001 // 豁免理由：BOM 检测需字节级精确控制（IFileSystem 不提供 ReadAllBytes/字节级 BOM 写入），测试用临时目录隔离

namespace JccAuditCli.Tests;

/// <summary>
/// BomStripper 的单元测试 — 验证 UTF-8 BOM 检测与移除逻辑
/// </summary>
public sealed class BomStripperTests
{
    /// <summary>
    /// 创建临时测试目录，返回目录路径，测试结束后自动清理
    /// </summary>
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "BomStripperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 写入 .cs 文件，可选是否带 UTF-8 BOM
    /// </summary>
    private static void WriteCsFile(string path, string content, bool withBom)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (withBom)
        {
            var withBomBytes = new byte[bytes.Length + 3];
            withBomBytes[0] = 0xEF;
            withBomBytes[1] = 0xBB;
            withBomBytes[2] = 0xBF;
            Array.Copy(bytes, 0, withBomBytes, 3, bytes.Length);
            File.WriteAllBytes(path, withBomBytes);
        }
        else
        {
            File.WriteAllBytes(path, bytes);
        }
    }

    [Fact]
    public void HasUtf8Bom_WithBom_ReturnsTrue()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "Test.cs");
            WriteCsFile(path, "namespace Foo;", withBom: true);

            BomStripper.HasUtf8Bom(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void HasUtf8Bom_WithoutBom_ReturnsFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "Test.cs");
            WriteCsFile(path, "namespace Foo;", withBom: false);

            BomStripper.HasUtf8Bom(path).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void HasUtf8Bom_EmptyFile_ReturnsFalse()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "Empty.cs");
            File.WriteAllBytes(path, []);

            BomStripper.HasUtf8Bom(path).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_WithBomFiles_RemovesBomAndPreservesContent()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "WithBom.cs");
            const string content = "namespace Foo;\npublic class Bar { }\n";
            WriteCsFile(path, content, withBom: true);

            var report = BomStripper.Strip(dir);

            report.StrippedCount.Should().Be(1);
            report.WithBomCount.Should().Be(1);
            BomStripper.HasUtf8Bom(path).Should().BeFalse();

            var actualContent = File.ReadAllText(path);
            actualContent.Should().Be(content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_WithoutBomFiles_DoesNothing()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "NoBom.cs");
            const string content = "namespace Foo;";
            WriteCsFile(path, content, withBom: false);

            var report = BomStripper.Strip(dir);

            report.StrippedCount.Should().Be(0);
            report.WithBomCount.Should().Be(0);
            File.ReadAllText(path).Should().Be(content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_DryRun_DoesNotModifyFiles()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "WithBom.cs");
            WriteCsFile(path, "namespace Foo;", withBom: true);

            var report = BomStripper.Strip(dir, dryRun: true);

            report.DryRun.Should().BeTrue();
            report.WithBomCount.Should().Be(1);
            report.StrippedCount.Should().Be(0);
            report.ScannedFiles.Should().Be(1);
            BomStripper.HasUtf8Bom(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_MixedFiles_OnlyStripsBomFiles()
    {
        var dir = CreateTempDir();
        try
        {
            var withBomPath = Path.Combine(dir, "WithBom.cs");
            var noBomPath = Path.Combine(dir, "NoBom.cs");
            WriteCsFile(withBomPath, "namespace A;", withBom: true);
            WriteCsFile(noBomPath, "namespace B;", withBom: false);

            var report = BomStripper.Strip(dir);

            report.StrippedCount.Should().Be(1);
            report.TotalCsFiles.Should().Be(2);
            report.ScannedFiles.Should().Be(2);
            BomStripper.HasUtf8Bom(withBomPath).Should().BeFalse();
            BomStripper.HasUtf8Bom(noBomPath).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_SkipsExcludedDirectories()
    {
        var dir = CreateTempDir();
        try
        {
            var srcPath = Path.Combine(dir, "Src.cs");
            var binDir = Path.Combine(dir, "bin");
            Directory.CreateDirectory(binDir);
            var binPath = Path.Combine(binDir, "BinFile.cs");
            WriteCsFile(srcPath, "namespace A;", withBom: true);
            WriteCsFile(binPath, "namespace B;", withBom: true);

            var report = BomStripper.Strip(dir);

            report.StrippedCount.Should().Be(1);
            report.TotalCsFiles.Should().Be(2);
            report.SkippedFiles.Should().Be(1);
            report.ScannedFiles.Should().Be(1);
            BomStripper.HasUtf8Bom(srcPath).Should().BeFalse();
            BomStripper.HasUtf8Bom(binPath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Strip_NonExistentDirectory_ThrowsArgumentException()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "DefinitelyDoesNotExist_" + Guid.NewGuid().ToString("N"));

        var act = () => BomStripper.Strip(nonExistent);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Strip_PreservesNonAsciiContent()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "Chinese.cs");
            const string content = "// 中文注释\nnamespace 测试;\npublic class 中文类 { }\n";
            WriteCsFile(path, content, withBom: true);

            var report = BomStripper.Strip(dir);

            report.StrippedCount.Should().Be(1);
            BomStripper.HasUtf8Bom(path).Should().BeFalse();
            File.ReadAllText(path).Should().Be(content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
