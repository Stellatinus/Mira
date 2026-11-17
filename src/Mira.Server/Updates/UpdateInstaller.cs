using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Mira.Server.Models;
using Mira.Server.Services;

namespace Mira.Server.Updates;

/// <summary>L'esito dell'ultimo aggiornamento, scritto dal programma di appoggio in <c>update-result.json</c> nella cartella dei dati.</summary>
public sealed record UpdateLastResult(string Version, bool Ok, string Result, string? Error, DateTimeOffset At);

/// <summary>
/// Lo stato dell'aggiornamento visto dall'interfaccia. <c>State</c> è uno tra <c>idle</c>, <c>downloading</c>, <c>verifying</c>,
/// <c>extracting</c>, <c>restarting</c>, <c>failed</c>. Se <c>Supported</c> è falso, <c>Reason</c> dice perché.
/// </summary>
public sealed record InstallStatus(
    bool Supported,
    string? Reason,
    string State,
    string? Version,
    int? Percent,
    ErrorInfo? Error,
    UpdateLastResult? LastResult);

/// <summary>Lancia il programma di appoggio. Nei test si sostituisce, per non avviare processi veri.</summary>
public interface IUpdateLauncher
{
    void Launch(string updaterExe, IReadOnlyList<string> arguments);
}

public sealed class ProcessUpdateLauncher : IUpdateLauncher
{
    public void Launch(string updaterExe, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(updaterExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(updaterExe)!,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        Process.Start(info)?.Dispose();
    }
}

/// <summary>
/// L'aggiornamento dall'interno (#92), solo per il guscio desktop: scarica il pacchetto della release che l'utente ha scelto,
/// lo verifica (somma SHA-256 contro <c>SHA256SUMS.txt</c>), lo estrae accanto all'installazione senza toccare quella in uso e
/// lancia il programma di appoggio (<c>Mira.Updater</c>), poi chiede al guscio di chiudersi. Parte solo da una richiesta
/// esplicita dell'utente e solo per una release stabile più nuova di quella in uso, letta da GitHub dal server, non dal browser.
/// Non verifica la firma Sigstore: vedi docs/RELEASING.md.
/// </summary>
public sealed class UpdateInstaller(
    UpdateService updates,
    IHttpClientFactory httpFactory,
    IDesktopIntegration? desktop,
    InstallLocation location,
    string dataDir,
    IUpdateLauncher launcher,
    string repository = "Stellatinus/Mira")
{
    public const string ResultFileName = "update-result.json";
    private const long MaxPackageBytes = 600L * 1024 * 1024;
    private const long MaxSumsBytes = 64 * 1024;

    private readonly object _lock = new();
    private string _state = "idle";
    private string? _version;
    private int? _percent;
    private ErrorInfo? _error;

    /// <summary>L'identificativo del sistema (<c>win-x64</c>); null dove il pacchetto desktop non esiste. I test lo fissano.</summary>
    public Func<string?> Runtime { get; init; } = UpdatePackages.CurrentRuntime;

    /// <summary>Quanto aspettare, dopo aver lanciato il programma di appoggio, prima di chiudere Mira (l'interfaccia legge «riavvio»).</summary>
    public TimeSpan ExitDelay { get; init; } = TimeSpan.FromSeconds(1);

    private string ResultPath => Path.Combine(dataDir, ResultFileName);

    /// <summary>Si può aggiornare da qui? Solo nel guscio desktop, su un sistema con pacchetto, in una cartella dove si può scrivere.</summary>
    public string? UnsupportedReason(Func<string, bool>? canWrite = null)
    {
        if (desktop is null) return "update-desktop-only";
        if (Runtime() is null) return "update-unsupported-os";
        return location.Check(canWrite);
    }

    public InstallStatus GetStatus()
    {
        lock (_lock)
        {
            var reason = UnsupportedReason();
            return new InstallStatus(reason is null, reason, _state, _version, _percent, _error, ReadLastResult());
        }
    }

    /// <summary>Toglie l'esito dell'ultimo aggiornamento (l'utente l'ha letto).</summary>
    public void DismissLastResult()
    {
        try { File.Delete(ResultPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private UpdateLastResult? ReadLastResult()
    {
        try
        {
            if (!File.Exists(ResultPath)) return null;
            var result = JsonSerializer.Deserialize<UpdateLastResult>(File.ReadAllText(ResultPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            // Un aggiornamento riuscito si racconta solo se è la versione in uso; un fallimento, finché non lo si chiude.
            return result is not null && (!result.Ok || result.Version == AppVersion.Current) ? result : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Avvia l'aggiornamento a <paramref name="version"/>, che deve essere l'ultima release stabile e più nuova di quella in uso.
    /// Ritorna subito il motivo del rifiuto (un codice), oppure null: il lavoro prosegue in background e si segue con <see cref="GetStatus"/>.
    /// </summary>
    public async Task<string?> StartAsync(string? version, string healthUrl, CancellationToken ct)
    {
        if (UnsupportedReason() is { } unsupported)
            return unsupported;

        lock (_lock)
        {
            if (_state is not ("idle" or "failed"))
                return "update-busy";
            _state = "downloading";
            _version = version;
            _percent = 0;
            _error = null;
        }

        var (plan, error) = await ResolveAsync(version, ct);
        if (plan is null)
        {
            Fail(error ?? "update-unreadable");
            return error;
        }

        Running = Task.Run(() => RunAsync(plan, healthUrl, CancellationToken.None), CancellationToken.None);
        return null;
    }

    /// <summary>Il lavoro in corso in background (per i test, che lo aspettano).</summary>
    public Task? Running { get; private set; }

    private async Task<(UpdatePlan? Plan, string? Error)> ResolveAsync(string? version, CancellationToken ct)
    {
        // Il numero lo manda il browser, ma la release la legge il server da GitHub: si installa solo ciò che l'avviso ha indicato.
        var info = await updates.GetAsync(force: false, ct);
        if (info.Error is not null || !info.Available || info.Latest is null || info.Latest != version || updates.LatestRelease is null)
            return (null, "update-not-offered");

        return UpdatePackages.Select(updates.LatestRelease, Runtime()!, repository);
    }

    private async Task RunAsync(UpdatePlan plan, string healthUrl, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "Mira-update-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            CleanOldWorkDirs();
            Directory.CreateDirectory(work);
            var http = httpFactory.CreateClient("update");

            // 1. Le somme di controllo, poi il pacchetto con il progresso.
            var sums = await DownloadTextAsync(http, plan.Sums, ct);
            var expected = Sha256Sums.Find(sums, plan.Package.Name);
            if (expected is null)
                throw new MiraException("update-no-checksums", "SHA256SUMS.txt non elenca il pacchetto.");

            var zip = Path.Combine(work, plan.Package.Name);
            await DownloadFileAsync(http, plan.Package, zip, percent => SetState("downloading", percent), ct);

            // 2. Verifica: se la somma non torna, non si estrae niente.
            SetState("verifying", null);
            if (!await Sha256Sums.VerifyAsync(zip, expected, ct))
                throw new MiraException("update-checksum-mismatch", "La somma SHA-256 del pacchetto non corrisponde.");

            // 3. Estrazione accanto all'installazione: quella in uso non si tocca.
            SetState("extracting", null);
            var staged = Extract(zip, location.ExtractDir);
            File.Delete(zip);

            // 4. Il programma di appoggio parte da una copia in una cartella temporanea: i file di Mira in uso verranno spostati.
            var updaterCopy = Path.Combine(work, InstallLocation.UpdaterName);
            File.Copy(location.UpdaterPath, updaterCopy, overwrite: true);

            SetState("restarting", null);
            launcher.Launch(updaterCopy,
            [
                "--pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--install", location.InstallDir,
                "--staged", staged,
                "--backup", location.BackupDir,
                "--exe", InstallLocation.ExeName,
                "--version", plan.Version,
                "--health-url", healthUrl,
                "--cleanup", location.ExtractDir,
                "--result", ResultPath,
                "--log", Path.Combine(dataDir, "update.log"),
            ]);

            await Task.Delay(ExitDelay, ct);
            desktop?.RequestExit();
        }
        catch (Exception ex) when (ex is MiraException or HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            TryDelete(location.ExtractDir);
            TryDelete(work);
            Fail(ex is MiraException known ? known.Code : ex is HttpRequestException or TaskCanceledException ? "network" : "update-failed", ex.Message);
        }
    }

    /// <summary>Estrae il pacchetto e dà la cartella che contiene <c>Mira.exe</c> (il zip ha una cartella in cima).</summary>
    internal static string Extract(string zipPath, string extractDir)
    {
        TryDelete(extractDir);
        // ExtractToDirectory rifiuta i percorsi che escono dalla cartella di destinazione.
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var root = File.Exists(Path.Combine(extractDir, InstallLocation.ExeName))
            ? extractDir
            : Directory.GetDirectories(extractDir) is [var only] && File.Exists(Path.Combine(only, InstallLocation.ExeName)) ? only : null;
        if (root is null)
            throw new MiraException("update-bad-package", "Il pacchetto non contiene Mira.exe.");
        return root;
    }

    private static async Task<string> DownloadTextAsync(HttpClient http, UpdateAsset asset, CancellationToken ct)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxSumsBytes)
            throw new MiraException("update-no-checksums", "SHA256SUMS.txt è troppo grande.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static async Task DownloadFileAsync(HttpClient http, UpdateAsset asset, string destination, Action<int> progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        if (total > MaxPackageBytes || asset.Size > MaxPackageBytes)
            throw new MiraException("update-too-large", "Il pacchetto è più grande del previsto.");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            received += read;
            if (received > MaxPackageBytes)
                throw new MiraException("update-too-large", "Il pacchetto è più grande del previsto.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            var percent = total > 0 ? (int)Math.Min(100, received * 100 / total) : 0;
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress(percent);
            }
        }
    }

    private void SetState(string state, int? percent)
    {
        lock (_lock)
        {
            _state = state;
            _percent = percent;
        }
    }

    private void Fail(string code, string? detail = null)
    {
        lock (_lock)
        {
            _state = "failed";
            _percent = null;
            _error = new ErrorInfo(code, detail);
        }
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Le cartelle temporanee di aggiornamenti passati (il programma di appoggio non può cancellare se stesso).</summary>
    private static void CleanOldWorkDirs()
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "Mira-update-*"))
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > TimeSpan.FromHours(6))
                    TryDelete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
