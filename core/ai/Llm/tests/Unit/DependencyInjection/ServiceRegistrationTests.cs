namespace Llm.Tests.DependencyInjection;

using Api.LLM.QueryServices;
using JoinCode.Abstractions.Transport;
using JoinCode.Llm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

public class ServiceRegistrationTests
{
    public ServiceRegistrationTests()
    {
        Environment.SetEnvironmentVariable("JCC_RESILIENCE_ENABLED", "0");
    }

    [Fact]
    public void AddLlmServices_RegistersQueryService()
    {
        var services = new ServiceCollection();
        var config = new ProviderConfig { Provider = "openai", ApiKey = "sk-test", ModelId = "gpt-4o" };

        services.AddLlmServices(config);

        var provider = services.BuildServiceProvider();
        var service = provider.GetService<IQueryService>();
        service.Should().NotBeNull();
    }

    [Fact]
    public void AddLlmServicesWithCustomQuery_RegistersCustomService()
    {
        var services = new ServiceCollection();
        var custom = new Mock<IQueryService>().Object;

        services.AddLlmServicesWithCustomQuery(custom);

        var provider = services.BuildServiceProvider();
        provider.GetService<IQueryService>().Should().BeSameAs(custom);
    }

    [Fact]
    public void CreateEmptyKernel_ReturnsChatClientWithEmptyQueryService()
    {
        var kernel = ServiceRegistration.CreateEmptyKernel();

        kernel.Should().NotBeNull();
        kernel.GetChatCompletionService().Should().BeOfType<EmptyQueryService>();
    }

    [Fact]
    public void AddPipeQueryService_RegistersPipeQueryService()
    {
        var services = new ServiceCollection();
        var pipeConfig = new PipeTransportConfig { PipeName = "test-pipe" };
        var config = new ProviderConfig { Provider = "openai", ApiKey = "sk-test" };

        services.AddPipeQueryService(pipeConfig, config.ApiKey);

        var provider = services.BuildServiceProvider();
        var service = provider.GetService<IQueryService>();
        service.Should().NotBeNull();
    }

    [Fact]
    public void AddKernelWithPlugins_WithPipeEndpoint_RegistersPipeQueryService()
    {
        var services = new ServiceCollection();
        var providerConfig = new ProviderConfig { Provider = "openai", ApiKey = "sk-test", ModelId = "gpt-4o" };
        var pipeConfig = new PipeTransportConfig { PipeName = "pipe" };

        services.AddKernelWithPlugins(providerConfig, pipeConfig);

        var provider = services.BuildServiceProvider();
        provider.GetService<IQueryService>().Should().NotBeNull();
    }

    [Fact]
    public void AddKernelWithPlugins_WithoutPipeEndpoint_RegistersStandardQueryService()
    {
        var services = new ServiceCollection();
        var providerConfig = new ProviderConfig { Provider = "openai", ApiKey = "sk-test", ModelId = "gpt-4o" };

        services.AddKernelWithPlugins(providerConfig);

        var provider = services.BuildServiceProvider();
        provider.GetService<IQueryService>().Should().NotBeNull();
    }

    [Fact]
    public void AddKernelWithDynamicPlugins_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var providerConfig = new ProviderConfig { Provider = "openai", ApiKey = "sk-test", ModelId = "gpt-4o" };

        var act = () => services.AddKernelWithDynamicPlugins(providerConfig);

        act.Should().NotThrow();
    }
}
