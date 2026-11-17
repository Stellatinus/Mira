using System.Text.Json;

namespace Mira.Server.Storage;

/// <summary>
/// Le preferenze dell'utente che devono valere per tutta Mira (desktop e server autonomo) e non per un solo browser.
/// <c>CheckUpdatesOnStart</c>: all'apertura Mira controlla se c'è una release stabile più nuova (solo un avviso: non scarica né
/// installa nulla). Spenta di base.
/// </summary>
public sealed record UserSettings(bool CheckUpdatesOnStart = false);

/// <summary>Il file <c>settings.json</c> nella cartella dei dati: non contiene segreti e sopravvive agli aggiornamenti.</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    /// <summary>Le preferenze; un file mancante o illeggibile vale «tutto di base».</summary>
    public async Task<UserSettings> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(UserSettings settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Si scrive su un file accanto e poi si sostituisce: uno spegnimento a metà non lascia un file troncato.
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, Json), ct);
            File.Move(temp, _path, overwrite: true);
        }
        finally { _gate.Release(); }
    }

    private async Task<UserSettings> ReadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_path)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(_path, ct), Json) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }
}
