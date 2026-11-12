namespace Mira.Server;

/// <summary>
/// Trova la cartella che contiene wwwroot (il frontend compilato), senza dipendere
/// dalla directory da cui viene lanciato il comando.
/// </summary>
public static class ContentRoot
{
    public static string Find()
    {
        // Da sorgente (dotnet run): risale da bin/Debug/net10.0 fino alla cartella del .csproj.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mira.Server.csproj")))
                return dir.FullName;
        }

        // Versione pubblicata: wwwroot sta accanto all'eseguibile.
        return AppContext.BaseDirectory;
    }
}
