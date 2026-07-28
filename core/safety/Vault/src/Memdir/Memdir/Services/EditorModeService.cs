namespace Core.Memdir;

[Register]
public sealed partial class EditorModeService : ConfigPersistentServiceBase<EditorMode>, IEditorModeService
{
    public EditorModeService(IConfigurationService? configService = null)
        : base(EditorMode.Normal, configService) { }

    protected override string ConfigKey => "editor.mode";
    protected override bool TryParseConfigValue(string? raw, out EditorMode result)
    {
        if (raw is not null && EditorModeExtensions.FromValue(raw) is { } mode)
        {
            result = mode;
            return true;
        }
        result = default;
        return false;
    }
    protected override string FormatConfigValue(EditorMode value)
        => value.ToString().ToLowerInvariant();

    public EditorMode CurrentMode => Value;

    public void SetMode(EditorMode mode) => SetValue(mode);

    public EditorMode Toggle()
    {
        var newMode = Value == EditorMode.Normal ? EditorMode.Vim : EditorMode.Normal;
        SetValue(newMode);
        return newMode;
    }
}
