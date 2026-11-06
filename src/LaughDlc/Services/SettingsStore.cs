using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaughDlc.Models;

namespace LaughDlc.Services;

public enum SettingsLoadStatus
{
    Loaded,
    CreatedDefaults,
    ReadFailed,
    RecoveredFromCorruptFile,
    RecoveredFromIncompatibleVersion,
    RecoveredInvalidValues
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    SettingsLoadStatus Status,
    string? WarningMessage,
    string? BackupPath);

public sealed class SettingsSaveException : Exception
{
    public SettingsSaveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string _settingsPath;
    private readonly string _directory;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(AppLog.AppDataDirectory, "settings.json");
        _directory = Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!;
    }

    public string SettingsFilePath => _settingsPath;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_settingsPath))
        {
            AppLog.Info($"Settings file not found; using defaults. Path={_settingsPath}");
            return new SettingsLoadResult(
                AppSettings.CreateDefault(),
                SettingsLoadStatus.CreatedDefaults,
                null,
                null);
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Error($"Failed to read settings file. Path={_settingsPath}", ex);
            var warning = $"无法读取配置文件：{_settingsPath}\n\n本次运行将使用默认设置，原文件不会被覆盖。\n\n详情：{ex.Message}";
            return new SettingsLoadResult(
                AppSettings.CreateDefault(),
                SettingsLoadStatus.ReadFailed,
                warning,
                null);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var version = TryGetSchemaVersion(document.RootElement);
            if (version != AppSettings.CurrentSchemaVersion)
            {
                return RecoverFromIncompatibleVersion(version);
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                ?? throw new JsonException("配置文件内容为空。");

            var invalidValues = false;
            if (settings.Hotkey is null || !settings.Hotkey.IsValid)
            {
                settings.Hotkey = HotkeyDefinition.CreateDefault();
                invalidValues = true;
            }

            if (string.IsNullOrWhiteSpace(settings.AudioFilePath))
            {
                settings.AudioFilePath = null;
            }

            if (invalidValues)
            {
                return RecoverFromInvalidValues(settings);
            }

            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            AppLog.Info($"Settings loaded. Path={_settingsPath}");
            return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded, null, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return RecoverFromCorruptFile(ex);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Hotkey is null || !settings.Hotkey.IsValid)
        {
            throw new SettingsSaveException("配置中的快捷键无效，未写入配置文件。", new InvalidOperationException("Invalid hotkey."));
        }

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            temporaryPath = Path.Combine(
                _directory,
                $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
            AppLog.Info($"Settings saved. Path={_settingsPath}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save settings. Path={_settingsPath}", ex);
            throw new SettingsSaveException(
                $"无法保存配置文件：{_settingsPath}\n\n详情：{ex.Message}",
                ex);
        }
        finally
        {
            try
            {
                if (temporaryPath is not null && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private SettingsLoadResult RecoverFromCorruptFile(Exception exception)
    {
        AppLog.Error($"Settings file is corrupt. Path={_settingsPath}", exception);

        var backupPath = TryBackup("corrupt");
        var defaults = AppSettings.CreateDefault();
        var saveWarning = SaveRecoveredDefaults(defaults);

        var warning = new StringBuilder()
            .AppendLine("配置文件内容损坏，已恢复默认设置。")
            .AppendLine()
            .AppendLine($"配置文件：{_settingsPath}");

        if (backupPath is not null)
        {
            warning.AppendLine($"原文件备份：{backupPath}");
        }
        else
        {
            warning.AppendLine("原文件备份失败，请检查磁盘和目录权限。");
        }

        warning.AppendLine($"损坏原因：{exception.Message}");

        if (saveWarning is not null)
        {
            warning.AppendLine().Append(saveWarning);
        }

        return new SettingsLoadResult(
            defaults,
            SettingsLoadStatus.RecoveredFromCorruptFile,
            warning.ToString().TrimEnd(),
            backupPath);
    }

    private SettingsLoadResult RecoverFromIncompatibleVersion(int version)
    {
        var reason = $"配置文件版本 {version} 与当前支持的版本 {AppSettings.CurrentSchemaVersion} 不兼容。";
        AppLog.Error($"Incompatible settings version. Version={version}; Path={_settingsPath}");

        var backupPath = TryBackup($"incompatible-v{version}");
        var defaults = AppSettings.CreateDefault();
        var saveWarning = SaveRecoveredDefaults(defaults);

        var warning = new StringBuilder()
            .AppendLine("配置文件版本不兼容，已恢复默认设置。")
            .AppendLine()
            .AppendLine(reason)
            .AppendLine($"配置文件：{_settingsPath}");

        if (backupPath is not null)
        {
            warning.AppendLine($"原文件备份：{backupPath}");
        }
        else
        {
            warning.AppendLine("原文件备份失败，请检查磁盘和目录权限。");
        }

        if (saveWarning is not null)
        {
            warning.AppendLine().Append(saveWarning);
        }

        return new SettingsLoadResult(
            defaults,
            SettingsLoadStatus.RecoveredFromIncompatibleVersion,
            warning.ToString().TrimEnd(),
            backupPath);
    }

    private SettingsLoadResult RecoverFromInvalidValues(AppSettings settings)
    {
        AppLog.Error($"Settings contained invalid values; invalid hotkey was reset. Path={_settingsPath}");

        var backupPath = TryBackup("invalid");
        var warning = new StringBuilder()
            .AppendLine("配置中的快捷键无效，已重置为默认快捷键 Ctrl + Alt + L。")
            .AppendLine()
            .AppendLine($"配置文件：{_settingsPath}");

        if (backupPath is not null)
        {
            warning.AppendLine($"原文件备份：{backupPath}");
        }

        try
        {
            Save(settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save repaired settings.", ex);
            warning.AppendLine().AppendLine(ex.Message);
        }

        return new SettingsLoadResult(
            settings,
            SettingsLoadStatus.RecoveredInvalidValues,
            warning.ToString().TrimEnd(),
            backupPath);
    }

    private string? SaveRecoveredDefaults(AppSettings defaults)
    {
        try
        {
            Save(defaults);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to save recovered default settings.", ex);
            return $"默认设置也未能保存：{ex.Message}";
        }
    }

    private string? TryBackup(string suffix)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var extension = Path.GetExtension(_settingsPath);
            var baseName = Path.GetFileNameWithoutExtension(_settingsPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var backupPath = Path.Combine(_directory, $"{baseName}.{suffix}-{timestamp}{extension}");

            var counter = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(_directory, $"{baseName}.{suffix}-{timestamp}-{counter}{extension}");
                counter++;
            }

            File.Copy(_settingsPath, backupPath, overwrite: false);
            AppLog.Info($"Settings backup created. Path={backupPath}");
            return backupPath;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to back up settings. Path={_settingsPath}", ex);
            return null;
        }
    }

    private static int TryGetSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, nameof(AppSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var version)
                ? version
                : 0;
        }

        return 0;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };

        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
