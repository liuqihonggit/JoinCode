namespace Core.Tests.Configuration;

public sealed class CustomCommandLoaderTests
{
    [Fact]
    public void ParseCommandFile_SimpleFile_Should_Create_Command()
    {
        var result = CustomCommandLoader.ParseCommandFile("test.md", "Hello $ARGUMENTS", "/path/test.md");

        Assert.NotNull(result);
        Assert.Equal("test", result.Name);
        Assert.Equal("Hello $ARGUMENTS", result.Content);
        Assert.Null(result.Namespace);
        Assert.False(result.DisableModelInvocation);
    }

    [Fact]
    public void ParseCommandFile_WithNamespace_Should_Set_Namespace()
    {
        var result = CustomCommandLoader.ParseCommandFile("deploy\\staging.md", "Deploy to staging", "/path/deploy/staging.md");

        Assert.NotNull(result);
        Assert.Equal("staging", result.Name);
        Assert.Equal("deploy", result.Namespace);
        Assert.Equal("deploy:staging", result.FullName);
    }

    [Fact]
    public void ParseCommandFile_WithFrontmatter_Should_Parse_Metadata()
    {
        var content = "---\ndescription: Deploy command\ndisable-model-invocation: true\n---\nDeploy $ARGUMENTS";
        var result = CustomCommandLoader.ParseCommandFile("deploy.md", content, "/path/deploy.md");

        Assert.NotNull(result);
        Assert.Equal("Deploy $ARGUMENTS", result.Content);
        Assert.Equal("Deploy command", result.Description);
        Assert.True(result.DisableModelInvocation);
    }

    [Fact]
    public void ParseCommandFile_FrontmatterWithoutEnd_Should_Return_Raw_Content()
    {
        var content = "---\ndescription: test\nNo closing frontmatter";
        var result = CustomCommandLoader.ParseCommandFile("test.md", content, "/path/test.md");

        Assert.NotNull(result);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public void ParseCommandFile_NoFrontmatter_Should_Return_Raw_Content()
    {
        var content = "Just a plain command";
        var result = CustomCommandLoader.ParseCommandFile("plain.md", content, "/path/plain.md");

        Assert.NotNull(result);
        Assert.Equal("Just a plain command", result.Content);
        Assert.Equal(string.Empty, result.Description);
        Assert.False(result.DisableModelInvocation);
    }

    [Fact]
    public void ApplyArguments_Should_Replace_Placeholder()
    {
        var cmd = new CustomCommand { Name = "review", Content = "Review $ARGUMENTS please" };
        var result = cmd.ApplyArguments("my-code.cs");

        Assert.Equal("Review my-code.cs please", result);
    }

    [Fact]
    public void ApplyArguments_NoPlaceholder_Should_Return_Original()
    {
        var cmd = new CustomCommand { Name = "test", Content = "No placeholder here" };
        var result = cmd.ApplyArguments("args");

        Assert.Equal("No placeholder here", result);
    }

    [Fact]
    public void ApplyArguments_EmptyArguments_Should_Replace_With_Empty()
    {
        var cmd = new CustomCommand { Name = "greet", Content = "Hello $ARGUMENTS" };
        var result = cmd.ApplyArguments("");

        Assert.Equal("Hello ", result);
    }

    [Fact]
    public void ParseFrontmatter_WithQuotedDescription_Should_Strip_Quotes()
    {
        var content = "---\ndescription: \"My custom command\"\n---\nBody";
        var (body, desc, _) = CustomCommandLoader.ParseFrontmatter(content);

        Assert.Equal("My custom command", desc);
        Assert.Equal("Body", body);
    }

    [Fact]
    public void ParseFrontmatter_WithSingleQuotedDescription_Should_Strip_Quotes()
    {
        var content = "---\ndescription: 'My command'\n---\nBody";
        var (_, desc, _) = CustomCommandLoader.ParseFrontmatter(content);

        Assert.Equal("My command", desc);
    }

    [Fact]
    public void CustomCommand_FullName_WithoutNamespace_Should_Be_Name()
    {
        var cmd = new CustomCommand { Name = "test", Content = "test" };
        Assert.Equal("test", cmd.FullName);
    }

    [Fact]
    public void CustomCommand_FullName_WithNamespace_Should_Combine()
    {
        var cmd = new CustomCommand { Name = "staging", Content = "test", Namespace = "deploy" };
        Assert.Equal("deploy:staging", cmd.FullName);
    }
}
