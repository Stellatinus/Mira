using System.Runtime.InteropServices;
using Mira.Server.GitHub;

namespace Mira.Server.Updates;

/// <summary>Un file della release da scaricare.</summary>
public sealed record UpdateAsset(string Name, long Size, string Url);

/// <summary>Cosa scaricare per aggiornare: il pacchetto, le somme di controllo e (se c'è) il bundle della firma Sigstore.</summary>
public sealed record UpdatePlan(string Version, UpdateAsset Package, UpdateAsset Sums, UpdateAsset? Bundle);

/// <summary>Sceglie, tra i file di una release, quelli giusti per questa installazione.</summary>
public static class UpdatePackages
{
    public const string SumsName = "SHA256SUMS.txt";
    public const string BundleSuffix = ".sigstore.json";

    /// <summary>L'identificativo del sistema usato nei nomi dei pacchetti (<c>win-x64</c>), o null dove non c'è un pacchetto desktop.</summary>
    public static string? CurrentRuntime() =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" : null;

    /// <summary>Il nome del pacchetto desktop di una versione, come lo crea release.yml.</summary>
    public static string DesktopPackageName(string version, string rid) => $"Mira-Desktop-{version}-{rid}.zip";

    /// <summary>
    /// Il pacchetto desktop di una release stabile. Errori: <c>update-not-stable</c> (bozza, versione di prova o tag non
    /// leggibile), <c>update-no-package</c> (la release non ha il pacchetto per questo sistema), <c>update-no-checksums</c>
    /// (manca <c>SHA256SUMS.txt</c>: senza non si installa), <c>update-untrusted</c> (un file non sta nel repository di Mira).
    /// </summary>
    public static (UpdatePlan? Plan, string? Error) Select(GhRelease release, string rid, string repository)
    {
        if (release.Draft || release.Prerelease || !SemVer.TryParse(release.TagName, out var semver) || semver.Prerelease is not null)
            return (null, "update-not-stable");

        var version = semver.ToString();
        var assets = release.Assets ?? [];
        var package = assets.FirstOrDefault(a => a.Name == DesktopPackageName(version, rid));
        if (package is null)
            return (null, "update-no-package");

        var sums = assets.FirstOrDefault(a => a.Name == SumsName);
        if (sums is null)
            return (null, "update-no-checksums");

        var bundle = assets.FirstOrDefault(a => a.Name == package.Name + BundleSuffix);

        UpdateAsset Map(GhAsset a) => new(a.Name, a.Size, a.BrowserDownloadUrl);
        var plan = new UpdatePlan(version, Map(package), Map(sums), bundle is null ? null : Map(bundle));
        var all = new[] { plan.Package, plan.Sums }.Concat(plan.Bundle is null ? [] : [plan.Bundle]);
        return all.All(a => IsTrustedDownload(a.Url, repository)) ? (plan, null) : (null, "update-untrusted");
    }

    /// <summary>Si scarica solo da github.com, in https, dai file di una release del repository di Mira.</summary>
    public static bool IsTrustedDownload(string? url, string repository) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase);
}
