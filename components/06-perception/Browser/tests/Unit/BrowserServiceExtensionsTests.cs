namespace Browser.Tests;

public class BrowserServiceExtensionsTests
{
    [Fact]
    public void AddBrowserAutomation_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddBrowserAutomation();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBrowserAutomation_DoesNotAddDuplicateRegistration()
    {
        var services = new ServiceCollection();
        var countBefore = services.Count(d => d.ServiceType == typeof(IBrowserAutomationService));

        services.AddBrowserAutomation();

        var countAfter = services.Count(d => d.ServiceType == typeof(IBrowserAutomationService));
        countAfter.Should().Be(countBefore);
    }
}
