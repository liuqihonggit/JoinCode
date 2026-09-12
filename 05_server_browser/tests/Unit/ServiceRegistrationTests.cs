namespace Browser.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddBrowserServices_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddBrowserServices();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBrowserServices_DoesNotAddDuplicateRegistration()
    {
        var services = new ServiceCollection();
        var countBefore = services.Count(d => d.ServiceType == typeof(IBrowserAutomationService));

        services.AddBrowserServices();

        var countAfter = services.Count(d => d.ServiceType == typeof(IBrowserAutomationService));
        countAfter.Should().Be(countBefore);
    }
}
