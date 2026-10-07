namespace Mcp.Tests;

/// <summary>
/// P4 命令测试 — gist/org/ssh-key/gpg-key/secret/variable
/// </summary>
public sealed partial class GitHubToolHandlersTests {
    [Fact]
    public async Task GistList_ListsGists() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":"abc123","description":"test","public":false,"files":{"a.txt":{"filename":"a.txt"}}}]""" };

        var result = await _handler.GhGistListAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("gists");
        result.GetFirstText().Should().Contain("abc123");
    }

    [Fact]
    public async Task GistView_ReturnsGistContent() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"id":"abc","description":"test","public":false,"files":{"a.txt":{"content":"hello"}}}""" };

        var result = await _handler.GhGistViewAsync("abc");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("hello");
    }

    [Fact]
    public async Task GistCreate_PostsGistDto() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = """{"html_url":"https://gist.github.com/abc"}""" };

        await _handler.GhGistCreateAsync("a.txt", "hello", description: "test");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastPath.Should().Be("gists");
        _api.LastBody.Should().Contain("\"files\"");
        _api.LastBody.Should().Contain("hello");
    }

    [Fact]
    public async Task GistDelete_DeletesGist() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        await _handler.GhGistDeleteAsync("abc");

        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("gists/abc");
    }

    [Fact]
    public async Task OrgList_ListsOrgs() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"login":"myorg","description":"My Org"}]""" };

        var result = await _handler.GhOrgListAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("user/orgs");
        result.GetFirstText().Should().Contain("myorg");
    }

    [Fact]
    public async Task SshKeyList_ListsKeys() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """[{"id":1,"title":"laptop"}]""" };

        var result = await _handler.GhSshKeyListAsync();

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("user/keys");
    }

    [Fact]
    public async Task SshKeyAdd_PostsKeyDto() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" };

        await _handler.GhSshKeyAddAsync("laptop", "ssh-rsa AAA...");

        _api.LastMethod.Should().Be(HttpMethod.Post);
        _api.LastBody.Should().Contain("\"title\":\"laptop\"");
    }

    [Fact]
    public async Task SecretList_ListsSecrets() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 200, Body = """{"total_count":1,"secrets":[{"name":"MY_SECRET","created_at":"2026-01-01"}]}""" };

        var result = await _handler.GhSecretListAsync(repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/actions/secrets");
        result.GetFirstText().Should().Contain("MY_SECRET");
    }

    [Fact]
    public async Task VariableSet_PostsVariableDto() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Body = "" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 201, Body = "{}" });

        var result = await _handler.GhVariableSetAsync("NAME", "value", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastBody.Should().Contain("\"name\":\"NAME\"");
        _api.LastBody.Should().Contain("\"value\":\"value\"");
    }

    [Fact]
    public async Task SecretSet_PutsEncryptedSecretDto() {
        Skip.IfNot(OperatingSystem.IsWindows(), "X25519/curve25519 ECDiffieHellmanCng 仅 Windows CNG 支持, Linux 需 libsodium 加密库");
        using var ecdh = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        var pubKey = ecdh.ExportParameters(false).Q.X!;
        var keyB64 = Convert.ToBase64String(pubKey);
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = $$"""{"key_id":"123","key":"{{keyB64}}"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        var result = await _handler.GhSecretSetAsync("MY_SECRET", "secret_value", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Put);
        _api.LastPath.Should().Be("repos/owner/repo/actions/secrets/MY_SECRET");
        _api.LastBody.Should().Contain("\"encrypted_value\"");
        _api.LastBody.Should().Contain("\"key_id\":\"123\"");
        result.GetFirstText().Should().Contain("MY_SECRET");
    }

    [Fact]
    public async Task SecretSet_EnvSecret_UsesEnvEndpoint() {
        Skip.IfNot(OperatingSystem.IsWindows(), "X25519/curve25519 ECDiffieHellmanCng 仅 Windows CNG 支持, Linux 需 libsodium 加密库");
        using var ecdh = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        var pubKey = ecdh.ExportParameters(false).Q.X!;
        var keyB64 = Convert.ToBase64String(pubKey);
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 200, Body = $$"""{"key_id":"456","key":"{{keyB64}}"}""" });
        _api.EnqueueResponse(new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" });

        var result = await _handler.GhSecretSetAsync("ENV_SECRET", "val", repo: "owner/repo", env: "production");

        result.IsError.Should().BeFalse();
        _api.LastPath.Should().Be("repos/owner/repo/environments/production/secrets/ENV_SECRET");
    }

    [Fact]
    public async Task SecretSet_EmptyBody_ReturnsFail() {
        var result = await _handler.GhSecretSetAsync("MY_SECRET", body: null, repo: "owner/repo");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SecretSet_PublicKeyFetchFails_ReturnsFail() {
        _api.EnqueueResponse(new GitHubApiResponse { Success = false, StatusCode = 404, Body = "not found" });

        var result = await _handler.GhSecretSetAsync("MY_SECRET", "val", repo: "owner/repo");

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SecretDelete_DeletesSecret() {
        _api.NextResponse = new GitHubApiResponse { Success = true, StatusCode = 204, Body = "" };

        var result = await _handler.GhSecretDeleteAsync("MY_SECRET", repo: "owner/repo");

        result.IsError.Should().BeFalse();
        _api.LastMethod.Should().Be(HttpMethod.Delete);
        _api.LastPath.Should().Be("repos/owner/repo/actions/secrets/MY_SECRET");
    }
}
