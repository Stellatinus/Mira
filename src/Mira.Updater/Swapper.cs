namespace Mira.Updater;

/// <summary>Tutto ciò che serve a un aggiornamento, passato dal server da riga di comando.</summary>
internal sealed record UpdateJob(
    int Pid,
    string InstallDir,
    string StagedDir,
    string BackupDir,
    string ExeName,
    string ExpectedVersion,
    string HealthUrl,
    string? CleanupDir = null,
    TimeSpan? ExitTimeout = null,
    TimeSpan? HealthTimeout = null)
{
    public TimeSpan ExitWait => ExitTimeout ?? TimeSpan.FromSeconds(60);
    public TimeSpan HealthWait => HealthTimeout ?? TimeSpan.FromSeconds(45);

    /// <summary>Dove finisce la versione nuova che non ha risposto, prima di essere cancellata.</summary>
    public string FailedDir => InstallDir + ".failed";
}

internal enum UpdateResult
{
    /// <summary>La versione nuova è al suo posto e risponde.</summary>
    Updated,
    /// <summary>La nuova non ha funzionato: è tornata la vecchia.</summary>
    RestoredPrevious,
    /// <summary>Non è cambiato niente (per esempio Mira non si è chiusa in tempo).</summary>
    NotChanged,
    /// <summary>Né la nuova né la vecchia sono tornate a posto: serve un intervento a mano.</summary>
    Broken,
}

internal sealed record UpdateOutcome(UpdateResult Result, string? Error = null)
{
    public bool Ok => Result == UpdateResult.Updated;
}

/// <summary>Il processo di Mira avviato dall'aggiornamento.</summary>
internal interface IStartedApp : IDisposable
{
    bool HasExited { get; }
    void Kill();
}

/// <summary>Il mondo esterno dell'aggiornamento (processi, rete, tempo): finto nei test.</summary>
internal interface IUpdateHost
{
    /// <summary>Aspetta che il processo si chiuda; falso se è ancora vivo allo scadere del tempo.</summary>
    Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct);

    IStartedApp Launch(string exePath);

    /// <summary>La versione dichiarata da /api/version, o null se non risponde.</summary>
    Task<string?> ReadVersionAsync(string url, CancellationToken ct);

    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>
/// Sostituisce la cartella di Mira con quella nuova e torna indietro se qualcosa va storto. L'ordine è pensato perché
/// in ogni momento esista una cartella di Mira completa: la vecchia viene spostata (non copiata, non cancellata), la nuova
/// presa al suo posto, e solo quando risponde la versione giusta l'aggiornamento è finito. La vecchia resta in
/// <see cref="UpdateJob.BackupDir"/> fino al prossimo aggiornamento.
/// </summary>
internal sealed class Swapper(IUpdateHost host, Action<string>? log = null)
{
    private const int MoveAttempts = 20;
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan HealthPoll = TimeSpan.FromMilliseconds(750);

    private void Log(string message) => log?.Invoke(message);

    public async Task<UpdateOutcome> RunAsync(UpdateJob job, CancellationToken ct)
    {
        var exe = Path.Combine(job.InstallDir, job.ExeName);

        // 1. Mira deve essersi chiusa: finché gira, i suoi file sono bloccati. Se non si chiude, non si tocca niente.
        Log($"Aspetto la chiusura di Mira (pid {job.Pid}).");
        if (!await host.WaitForExitAsync(job.Pid, job.ExitWait, ct))
            return new UpdateOutcome(UpdateResult.NotChanged, "exit-timeout");

        if (!Directory.Exists(job.StagedDir) || !File.Exists(Path.Combine(job.StagedDir, job.ExeName)))
            return Restart(exe, new UpdateOutcome(UpdateResult.NotChanged, "staged-missing"));

        // 2. Fuori la copia di riserva dell'aggiornamento precedente: ne tengo una sola.
        if (Directory.Exists(job.BackupDir) && !await TryDeleteAsync(job.BackupDir, ct))
            return Restart(exe, new UpdateOutcome(UpdateResult.NotChanged, "backup-busy"));

        // 3. La versione vecchia si sposta, non si cancella.
        Log("Metto da parte la versione vecchia.");
        if (!await TryMoveAsync(job.InstallDir, job.BackupDir, ct))
            return Restart(exe, new UpdateOutcome(UpdateResult.NotChanged, "swap-failed"));

        // 4. La nuova prende il suo posto.
        Log("Metto al suo posto la versione nuova.");
        if (!await TryMoveAsync(job.StagedDir, job.InstallDir, ct))
        {
            Log("Non riesco a mettere la nuova: rimetto la vecchia.");
            return await RestoreAsync(job, exe, "swap-failed", ct);
        }

        // 5. Si riavvia e si aspetta la risposta con la versione giusta.
        Log("Riavvio Mira.");
        if (await StartAndCheckAsync(job, exe, ct))
        {
            if (job.CleanupDir is not null && Directory.Exists(job.CleanupDir))
                await TryDeleteAsync(job.CleanupDir, ct);
            return new UpdateOutcome(UpdateResult.Updated);
        }

        // 6. La nuova non risponde: via lei, torna la vecchia.
        Log("La nuova versione non risponde: ripristino la vecchia.");
        if (Directory.Exists(job.FailedDir))
            await TryDeleteAsync(job.FailedDir, ct);
        if (!await TryMoveAsync(job.InstallDir, job.FailedDir, ct))
            return new UpdateOutcome(UpdateResult.Broken, "restore-failed");
        var restored = await RestoreAsync(job, exe, "health-check-failed", ct);
        await TryDeleteAsync(job.FailedDir, ct);
        return restored;
    }

    private async Task<bool> StartAndCheckAsync(UpdateJob job, string exe, CancellationToken ct)
    {
        IStartedApp app;
        try
        {
            app = host.Launch(exe);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"Avvio non riuscito: {ex.Message}");
            return false;
        }

        using (app)
        {
            var waited = TimeSpan.Zero;
            while (waited < job.HealthWait)
            {
                if (app.HasExited)
                    break;
                var version = await host.ReadVersionAsync(job.HealthUrl, ct);
                if (version == job.ExpectedVersion)
                    return true;
                await host.DelayAsync(HealthPoll, ct);
                waited += HealthPoll;
            }

            try { app.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            // Il tempo perché Windows rilasci i file del processo appena chiuso.
            await host.DelayAsync(TimeSpan.FromSeconds(1), ct);
            return false;
        }
    }

    /// <summary>La vecchia versione torna al suo posto (la nuova è già stata spostata altrove o non c'è) e si riavvia.</summary>
    private async Task<UpdateOutcome> RestoreAsync(UpdateJob job, string exe, string error, CancellationToken ct)
    {
        if (!await TryMoveAsync(job.BackupDir, job.InstallDir, ct))
            return new UpdateOutcome(UpdateResult.Broken, "restore-failed");
        return Restart(exe, new UpdateOutcome(UpdateResult.RestoredPrevious, error));
    }

    /// <summary>Riavvia la versione in uso (quella vecchia) dopo un aggiornamento non riuscito.</summary>
    private UpdateOutcome Restart(string exe, UpdateOutcome outcome)
    {
        try
        {
            host.Launch(exe).Dispose();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"Non riesco a riavviare la versione vecchia: {ex.Message}");
        }
        return outcome;
    }

    private async Task<bool> TryMoveAsync(string from, string to, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MoveAttempts; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Un antivirus o Esplora risorse può tenere un file aperto ancora per un attimo.
                Log($"Spostamento {Path.GetFileName(from)} → {Path.GetFileName(to)}, tentativo {attempt}: {ex.Message}");
                await host.DelayAsync(MoveRetryDelay, ct);
            }
        }
        return false;
    }

    private async Task<bool> TryDeleteAsync(string dir, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await host.DelayAsync(MoveRetryDelay, ct);
            }
        }
        return !Directory.Exists(dir);
    }
}
