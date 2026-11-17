namespace Mira.Server.Updates;

/// <summary>
/// Dove sta installata Mira e dove si appoggia l'aggiornamento. La cartella del programma si sostituisce spostandola, quindi
/// serve poter scrivere nella cartella che la contiene (non solo dentro di lei): <c>%LOCALAPPDATA%\Mira</c> sì, <c>Program Files</c> no.
/// L'aggiornamento non chiede mai i permessi di amministratore: se non può scrivere, rifiuta e lo dice.
/// </summary>
public sealed class InstallLocation
{
    public const string ExeName = "Mira.exe";
    public const string UpdaterName = "Mira.Updater.exe";

    public string InstallDir { get; }

    /// <summary>Dove si estrae il pacchetto nuovo (accanto all'installazione, sullo stesso disco).</summary>
    public string ExtractDir => InstallDir + ".update";

    /// <summary>Dove resta la versione precedente dopo un aggiornamento.</summary>
    public string BackupDir => InstallDir + ".previous";

    public string ExePath => Path.Combine(InstallDir, ExeName);
    public string UpdaterPath => Path.Combine(InstallDir, UpdaterName);

    public InstallLocation(string installDir)
    {
        InstallDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
    }

    /// <summary>La cartella del programma in esecuzione.</summary>
    public static InstallLocation Current() => new(AppContext.BaseDirectory);

    /// <summary>
    /// Vero se si può aggiornare qui. Altrimenti il codice dell'errore: <c>update-not-installed</c> (Mira.exe non è in questa
    /// cartella, per esempio si esegue dal sorgente), <c>update-no-updater</c> (manca il programma di appoggio),
    /// <c>update-not-writable</c> (non si può scrivere accanto alla cartella). <paramref name="canWrite"/> permette di
    /// provare il controllo senza toccare i permessi veri.
    /// </summary>
    public string? Check(Func<string, bool>? canWrite = null)
    {
        canWrite ??= CanWriteTo;
        if (!File.Exists(ExePath))
            return "update-not-installed";
        if (!File.Exists(UpdaterPath))
            return "update-no-updater";

        var parent = Path.GetDirectoryName(InstallDir);
        if (parent is null || !canWrite(parent) || !canWrite(InstallDir))
            return "update-not-writable";
        return null;
    }

    /// <summary>Prova davvero a creare (e togliere) un file nella cartella: l'unico controllo affidabile dei permessi.</summary>
    public static bool CanWriteTo(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".mira-write-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
