using System.Globalization;

namespace Mira.Server;

/// <summary>
/// Una versione MAJOR.MINOR.PATCH con un'eventuale fase di prova dopo il trattino (<c>1.3.0-rc.1</c>), come in
/// docs/RELEASING.md. Il confronto segue il versionamento semantico: una versione di prova viene prima della
/// stessa versione stabile, e le fasi si confrontano identificatore per identificatore.
/// </summary>
public sealed record SemVer(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemVer>
{
    /// <summary>Accetta anche una «v» davanti (il tag di una release: <c>v1.2.0</c>) e ignora i metadati di build dopo il «+».</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = new SemVer(0, 0, 0, null);
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text[0] is 'v' or 'V') text = text[1..];

        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        string? pre = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(id => id.Length == 0)) return false;
        }

        var parts = text.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        version = new SemVer(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;

        // A parità di numeri, la versione stabile viene dopo quella di prova.
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var a = mine[i];
            var b = theirs[i];
            var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1, // gli identificatori numerici vengono prima di quelli con lettere
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (result != 0) return result;
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease is null ? "" : "-" + Prerelease);
}
