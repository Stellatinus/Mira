using System.Diagnostics;
using System.Text.Json;
using Mira.Updater;

// Uso (lo lancia il server di Mira, non l'utente):
//   Mira.Updater --pid N --install DIR --staged DIR --backup DIR --exe Mira.exe --version X.Y.Z
//                --health-url URL [--cleanup DIR] [--result FILE] [--log FILE]
//                [--exit-timeout-seconds N] [--health-timeout-seconds N]
var options = ParseArgs(args);
string? Get(string name) => options.GetValueOrDefault(name);

string[] required = ["pid", "install", "staged", "backup", "exe", "version", "health-url"];
var missing = required.Where(name => Get(name) is null).ToList();
if (missing.Count > 0 || !int.TryParse(Get("pid"), out var pid))
{
    Console.Error.WriteLine("Mira.Updater: argomenti mancanti o non validi: " + string.Join(", ", missing.Count > 0 ? missing : ["pid"]));
    return 2;
}

var logFile = Get("log");
void Log(string message)
{
    var line = $"{DateTimeOffset.Now:O} {message}";
    Console.WriteLine(line);
    if (logFile is null) return;
    try { File.AppendAllText(logFile, line + Environment.NewLine); } catch (IOException) { } catch (UnauthorizedAccessException) { }
}

var job = new UpdateJob(
    pid,
    Path.GetFullPath(Get("install")!),
    Path.GetFullPath(Get("staged")!),
    Path.GetFullPath(Get("backup")!),
    Get("exe")!,
    Get("version")!,
    Get("health-url")!,
    Get("cleanup") is { } cleanup ? Path.GetFullPath(cleanup) : null,
    Get("exit-timeout-seconds") is { } e && int.TryParse(e, out var es) ? TimeSpan.FromSeconds(es) : null,
    Get("health-timeout-seconds") is { } h && int.TryParse(h, out var hs) ? TimeSpan.FromSeconds(hs) : null);

Log($"Aggiornamento a {job.ExpectedVersion}: {job.InstallDir}");
var outcome = await new Swapper(new RealHost(), Log).RunAsync(job, CancellationToken.None);
Log($"Esito: {outcome.Result}{(outcome.Error is null ? "" : $" ({outcome.Error})")}");

// L'esito resta nella cartella dei dati: alla prossima apertura Mira può dire com'è andata.
if (Get("result") is { } resultFile)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultFile))!);
        using var stream = File.Create(resultFile);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("version", job.ExpectedVersion);
        writer.WriteBoolean("ok", outcome.Ok);
        writer.WriteString("result", outcome.Result.ToString());
        if (outcome.Error is null) writer.WriteNull("error"); else writer.WriteString("error", outcome.Error);
        writer.WriteString("at", DateTimeOffset.UtcNow);
        writer.WriteEndObject();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Log($"Non riesco a scrivere l'esito: {ex.Message}");
    }
}

return outcome.Ok ? 0 : 1;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i + 1 < args.Length; i += 2)
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            result[args[i][2..]] = args[i + 1];
    return result;
}

internal sealed class RealHost : IUpdateHost
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return true; // già chiuso
        }

        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    public IStartedApp Launch(string exePath)
    {
        var process = Process.Start(new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Process.Start non ha restituito un processo.");
        return new StartedProcess(process);
    }

    public async Task<string?> ReadVersionAsync(string url, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);

    private sealed class StartedProcess(Process process) : IStartedApp
    {
        public bool HasExited => process.HasExited;
        public void Kill() => process.Kill(entireProcessTree: true);
        public void Dispose() => process.Dispose();
    }
}
