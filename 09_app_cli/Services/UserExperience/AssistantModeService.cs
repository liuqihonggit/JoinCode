namespace IO.Services;

[Register(typeof(IAssistantModeService), ServiceLifetime.Singleton)]
public sealed partial class AssistantModeService : ServiceEntity, IAssistantModeService
{
    public bool IsAssistantMode => IsAssistantModeEnabled;

    public bool IsAssistantModeEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(JccEnvVar.AssistantMode.ToValue());
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
