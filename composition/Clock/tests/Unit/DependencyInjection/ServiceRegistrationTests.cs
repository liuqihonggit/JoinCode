namespace Clock.Tests.Unit.DependencyInjection;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddClockServices_RegistersGoalServices()
    {
        var services = new ServiceCollection();

        services.AddClockServices();

        Assert.Contains(services, s => s.ServiceType == typeof(IGoalEvaluator) && s.ImplementationType == typeof(GoalEvaluator));
        Assert.Contains(services, s => s.ServiceType == typeof(IGoalHeartbeat) && s.ImplementationType == typeof(GoalHeartbeat));
        Assert.Contains(services, s => s.ServiceType == typeof(IGoalEngine) && s.ImplementationType == typeof(GoalEngine));
    }

    [Fact]
    public void AddGoalServices_RegistersRequiredServicesAsSingletons()
    {
        var services = new ServiceCollection();

        services.AddGoalServices();

        var evaluatorDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IGoalEvaluator));
        Assert.NotNull(evaluatorDescriptor);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, evaluatorDescriptor.Lifetime);
        Assert.Equal(typeof(GoalEvaluator), evaluatorDescriptor.ImplementationType);

        var heartbeatDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IGoalHeartbeat));
        Assert.NotNull(heartbeatDescriptor);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, heartbeatDescriptor.Lifetime);
        Assert.Equal(typeof(GoalHeartbeat), heartbeatDescriptor.ImplementationType);

        var engineDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IGoalEngine));
        Assert.NotNull(engineDescriptor);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, engineDescriptor.Lifetime);
        Assert.Equal(typeof(GoalEngine), engineDescriptor.ImplementationType);
    }

    [Fact]
    public void AddClockServices_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddClockServices();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddGoalServices_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddGoalServices();

        Assert.Same(services, result);
    }
}
