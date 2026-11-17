using System.Security.Cryptography;

namespace Mira.Server.Updates;

/// <summary>
/// Le somme di controllo SHA-256 di una release (<c>SHA256SUMS.txt</c>, il formato di <c>sha256sum</c>: «somma␠␠nome» o
/// «somma␠*nome»). Protegge da un download corrotto o troncato; da sola NON protegge da una release compromessa, perché il
/// file sta sulla stessa pagina del pacchetto (vedi docs/RELEASING.md).
/// </summary>
public static class Sha256Sums
{
    /// <summary>La somma attesa per un file, in minuscolo. Null se il file non c'è o la riga non è una somma valida.</summary>
    public static string? Find(string sumsText, string fileName)
    {
        foreach (var raw in sumsText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66) continue;

            var hash = line[..64];
            var rest = line[64..];
            if (!hash.All(Uri.IsHexDigit) || rest.Length < 3 || rest[0] != ' ' || rest[1] is not (' ' or '*'))
                continue;

            if (rest[2..] == fileName)
                return hash.ToLowerInvariant();
        }
        return null;
    }

    public static async Task<string> ComputeAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    /// <summary>Vero se il file ha esattamente la somma attesa.</summary>
    public static async Task<bool> VerifyAsync(string path, string expected, CancellationToken ct) =>
        string.Equals(await ComputeAsync(path, ct), expected, StringComparison.OrdinalIgnoreCase);
}
