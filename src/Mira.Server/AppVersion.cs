using System.Reflection;

namespace Mira.Server;

/// <summary>
/// La versione di Mira, nel formato MAJOR.MINOR.PATCH (es. "1.0.0"). Nelle release viene dal tag (vedi
/// docs/RELEASING.md); compilando dal sorgente vale "0.0.0-dev", così si vede che non è una release.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var version = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";

        // Il compilatore aggiunge "+<commit>": a chi guarda interessa solo il numero.
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
