namespace Core.Tests.Plugins;

public sealed class ServiceLookupTests
{
    [Fact]
    public void ServiceUnavailableException_ContainsServiceTypeAndReason()
    {
        var ex = new ServiceUnavailableException(typeof(string), ServiceLookup.ProviderDead);
        Assert.Equal(typeof(string), ex.ServiceType);
        Assert.Equal(ServiceLookup.ProviderDead, ex.Reason);
        Assert.Contains("String", ex.Message);
        Assert.Contains("ProviderDead", ex.Message);
    }

    [Fact]
    public void ServiceUnavailableException_NotRegistered_MessageContainsReason()
    {
        var ex = new ServiceUnavailableException(typeof(int), ServiceLookup.NotRegistered);
        Assert.Contains("NotRegistered", ex.Message);
    }

    [Fact]
    public void ServiceLookup_AllValuesAreDistinct()
    {
        var values = Enum.GetValues<ServiceLookup>();
        var distinct = values.Distinct().Count();
        Assert.Equal(values.Length, distinct);
    }
}
