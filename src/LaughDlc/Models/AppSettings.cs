namespace LaughDlc.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? AudioFilePath { get; set; }

    public bool HotkeyEnabled { get; set; } = true;

    public HotkeyDefinition Hotkey { get; set; } = HotkeyDefinition.CreateDefault();

    public static AppSettings CreateDefault()
    {
        return new AppSettings();
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SchemaVersion = SchemaVersion,
            AudioFilePath = AudioFilePath,
            HotkeyEnabled = HotkeyEnabled,
            Hotkey = Hotkey.Clone()
        };
    }
}
