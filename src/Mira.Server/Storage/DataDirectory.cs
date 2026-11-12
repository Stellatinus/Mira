namespace Mira.Server.Storage;

public static class DataDirectory
{
    /// <summary>
    /// MIRA_DATA_DIR se impostata, altrimenti la cartella di configurazione dell'utente
    /// (Windows: %APPDATA%\Mira, Linux/macOS: ~/.config/Mira). Sempre fuori dal repository.
    /// </summary>
    public static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("MIRA_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
            return Path.GetFullPath(custom);

        var baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(baseDir, "Mira");
    }
}
