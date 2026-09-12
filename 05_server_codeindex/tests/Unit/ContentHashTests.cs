namespace JoinCode.CodeIndex.Tests;

public sealed class ContentHashTests
{
    [Fact]
    public void ComputeContentHash_String_Empty_ReturnsKnownSha256()
    {
        var hash = HashUtility.ComputeContentHash(string.Empty);

        Assert.Equal(64, hash.Length);
        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", hash);
    }

    [Fact]
    public void ComputeContentHash_String_KnownContent_IsDeterministic()
    {
        var hash1 = HashUtility.ComputeContentHash("hello world");
        var hash2 = HashUtility.ComputeContentHash("hello world");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void ComputeContentHash_String_DifferentContent_DifferentHash()
    {
        var hash1 = HashUtility.ComputeContentHash("a");
        var hash2 = HashUtility.ComputeContentHash("b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeContentHash_Utf8Span_MatchesStringHash()
    {
        var content = "hello world";
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        var fromString = HashUtility.ComputeContentHash(content);
        var fromSpan = HashUtility.ComputeContentHash(bytes);

        Assert.Equal(fromString, fromSpan);
    }

    [Fact]
    public async Task ReadFileAndComputeHashAsync_ExistingFile_ReturnsContentAndHash()
    {
        var fs = new InMemoryFileSystem();
        fs.WriteAllText("/tmp/sample.cs", "public class Foo { }");

        var (content, hash) = await HashUtility.ReadFileAndComputeHashAsync("/tmp/sample.cs", fs, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("public class Foo { }", content);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void ComputeContentHash_NullString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HashUtility.ComputeContentHash((string)null!));
    }
}
