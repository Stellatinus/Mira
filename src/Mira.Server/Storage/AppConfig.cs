using System.Text.Json;

namespace Mira.Server.Storage;

/// <summary>
/// Le impostazioni di avvio, nel file <c>config.json</c> della cartella dei dati (accanto ai token, ma non
/// cifrato: non contiene segreti). Oggi c'è solo la porta: <c>{ "port": 5080 }</c>. Il file sopravvive agli
/// aggiornamenti perché sta fuori dalla cartella del programma; lo scrive anche l'installer (#93).
/// Precedenza della porta: variabile d'ambiente <c>MIRA_URLS</c>, poi il file, poi 5080.
/// </summary>
public sealed record AppConfig(int? Port = null)
{
    public const string FileName = "config.json";
    public const int DefaultPort = 5080;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>Legge il file; senza file torna alla configurazione vuota. File illeggibile o non valido: <see cref="InvalidOperationException"/> con un messaggio chiaro.</summary>
    public static AppConfig Load(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path))
            return new AppConfig();

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Impossibile leggere {path}: {ex.Message}", ex);
        }

        config ??= new AppConfig();
        if (config.Port is { } port && !IsValidPort(port))
            throw new InvalidOperationException($"{path}: la porta {port} non è valida (deve essere tra 1 e 65535).");
        return config;
    }

    /// <summary>L'indirizzo su cui ascoltare: MIRA_URLS se impostata, altrimenti la porta del file, altrimenti 5080. Solo su 127.0.0.1.</summary>
    public string ResolveUrls(string? envUrls) =>
        !string.IsNullOrWhiteSpace(envUrls) ? envUrls : $"http://127.0.0.1:{Port ?? DefaultPort}";
}
