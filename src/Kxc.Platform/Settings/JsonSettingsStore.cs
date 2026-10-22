using System.Text.Json;
using System.Text.Json.Serialization;
using Kxc.Core.Search;
using Kxc.Core.Settings;

namespace Kxc.Platform.Settings;

/// <summary>Impostazioni in un file JSON scritto in modo atomico, leggibile solo dall'utente.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly AppPaths _paths;

    public JsonSettingsStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public AppSettings Load()
    {
        var file = _paths.SettingsFile;
        if (!File.Exists(file))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // File rovinato: si conserva una copia per l'utente e si riparte dai valori predefiniti.
            File.Copy(file, file + ".bak", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _paths.EnsureConfigDirectory();
        var file = _paths.SettingsFile;
        var temp = file + ".tmp";

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
        }

        File.Move(temp, file, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RecentConnection))]
[JsonSerializable(typeof(WindowPlacement))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
