using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Mira.Server.GitHub;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Tests.Support;
using Mira.Server.Updates;

namespace Mira.Server.Tests;

/// <summary>Una cartella temporanea che si cancella da sola.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mira-test-" + Guid.NewGuid().ToString("N")[..10]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public class UpdatePackageSelectionTests
{
    private const string Repo = "Stellatinus/Mira";

    private static string Url(string file, string tag = "v1.3.2") => $"https://github.com/{Repo}/releases/download/{tag}/{file}";

    private static GhRelease Release(string tag = "v1.3.2", bool draft = false, bool prerelease = false, params string[] files) =>
        new(tag, null, "https://github.com/x", prerelease, draft, null, files.Select(f => new GhAsset(f, 100, Url(f, tag))).ToList());

    private static readonly string[] Full =
    [
        "Mira-1.3.2-win-x64.zip", "Mira-Desktop-1.3.2-win-x64.zip", "Mira-Desktop-1.3.2-win-x64.zip.sigstore.json",
        "Mira-1.3.2-linux-x64.tar.gz", "SHA256SUMS.txt",
    ];

    [Fact]
    public void Picks_the_desktop_package_for_this_system_and_not_the_server_one()
    {
        var (plan, error) = UpdatePackages.Select(Release(files: Full), "win-x64", Repo);

        Assert.Null(error);
        Assert.Equal("1.3.2", plan!.Version);
        Assert.Equal("Mira-Desktop-1.3.2-win-x64.zip", plan.Package.Name);
        Assert.Equal("SHA256SUMS.txt", plan.Sums.Name);
        Assert.Equal("Mira-Desktop-1.3.2-win-x64.zip.sigstore.json", plan.Bundle!.Name);
    }

    [Fact]
    public void The_bundle_is_optional()
    {
        var (plan, error) = UpdatePackages.Select(Release(files: ["Mira-Desktop-1.3.2-win-x64.zip", "SHA256SUMS.txt"]), "win-x64", Repo);

        Assert.Null(error);
        Assert.Null(plan!.Bundle);
    }

    [Theory]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    [InlineData("win-arm64")]
    public void A_system_without_a_desktop_package_gets_no_plan(string rid)
    {
        var (plan, error) = UpdatePackages.Select(Release(files: Full), rid, Repo);

        Assert.Null(plan);
        Assert.Equal("update-no-package", error);
    }

    [Fact]
    public void Without_the_checksums_nothing_is_installed()
    {
        var (plan, error) = UpdatePackages.Select(Release(files: ["Mira-Desktop-1.3.2-win-x64.zip"]), "win-x64", Repo);

        Assert.Null(plan);
        Assert.Equal("update-no-checksums", error);
    }

    [Fact]
    public void A_package_of_another_version_is_not_taken()
    {
        var (plan, error) = UpdatePackages.Select(Release("v1.3.2", files: ["Mira-Desktop-1.3.1-win-x64.zip", "SHA256SUMS.txt"]), "win-x64", Repo);

        Assert.Null(plan);
        Assert.Equal("update-no-package", error);
    }

    [Theory]
    [InlineData("v1.4.0-rc.1", false, false)]
    [InlineData("v1.4.0", true, false)]
    [InlineData("v1.4.0", false, true)]
    [InlineData("nightly", false, false)]
    public void Only_stable_published_releases_are_installed(string tag, bool draft, bool prerelease)
    {
        var (plan, error) = UpdatePackages.Select(Release(tag, draft, prerelease, "Mira-Desktop-1.4.0-win-x64.zip", "SHA256SUMS.txt"), "win-x64", Repo);

        Assert.Null(plan);
        Assert.Equal("update-not-stable", error);
    }

    [Theory]
    [InlineData("https://evil.example/Stellatinus/Mira/releases/download/v1.3.2/SHA256SUMS.txt")]
    [InlineData("http://github.com/Stellatinus/Mira/releases/download/v1.3.2/SHA256SUMS.txt")]
    [InlineData("https://github.com/evil/Mira/releases/download/v1.3.2/SHA256SUMS.txt")]
    [InlineData("https://github.com.evil.example/Stellatinus/Mira/releases/download/v1.3.2/SHA256SUMS.txt")]
    [InlineData("not a url")]
    public void A_file_that_does_not_live_in_the_projects_releases_is_refused(string sumsUrl)
    {
        var release = new GhRelease("v1.3.2", null, "https://github.com/x", false, false, null,
        [
            new GhAsset("Mira-Desktop-1.3.2-win-x64.zip", 100, Url("Mira-Desktop-1.3.2-win-x64.zip")),
            new GhAsset("SHA256SUMS.txt", 10, sumsUrl),
        ]);

        var (plan, error) = UpdatePackages.Select(release, "win-x64", Repo);

        Assert.Null(plan);
        Assert.Equal("update-untrusted", error);
    }
}

public class Sha256SumsTests
{
    private const string Zip = "Mira-Desktop-1.3.2-win-x64.zip";
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData("{0}  {1}\n")]
    [InlineData("{0} *{1}\n")]
    [InlineData("{0}  {1}")]
    [InlineData("other\n{0}  {1}\r\n")]
    public void Finds_the_sum_of_a_file_in_the_sha256sum_format(string format)
    {
        Assert.Equal(Hash, Sha256Sums.Find(string.Format(format, Hash, Zip), Zip));
    }

    [Fact]
    public void The_sum_is_returned_in_lowercase()
    {
        Assert.Equal(Hash, Sha256Sums.Find($"{Hash.ToUpperInvariant()}  {Zip}\n", Zip));
    }

    [Fact]
    public void Matches_the_whole_name_and_ignores_malformed_lines()
    {
        var text = $"{Hash}  x{Zip}\n{Hash}  {Zip}.sigstore.json\nnon-hex-{new string('z', 57)}  {Zip}\n{new string('b', 63)}  {Zip}\n";

        Assert.Null(Sha256Sums.Find(text, Zip));
    }

    [Fact]
    public async Task A_file_with_the_right_sum_verifies_and_a_changed_one_does_not()
    {
        using var dir = new TempDir();
        var file = dir.Sub("a.zip");
        await File.WriteAllBytesAsync(file, "pacchetto"u8.ToArray());
        var sum = Convert.ToHexStringLower(SHA256.HashData("pacchetto"u8));

        Assert.True(await Sha256Sums.VerifyAsync(file, sum, CancellationToken.None));
        Assert.True(await Sha256Sums.VerifyAsync(file, sum.ToUpperInvariant(), CancellationToken.None));

        await File.WriteAllBytesAsync(file, "pacchetto!"u8.ToArray());
        Assert.False(await Sha256Sums.VerifyAsync(file, sum, CancellationToken.None));
    }
}

public class InstallLocationTests
{
    private static InstallLocation Install(TempDir dir, bool exe = true, bool updater = true)
    {
        var install = dir.Sub("Mira");
        Directory.CreateDirectory(install);
        if (exe) File.WriteAllText(Path.Combine(install, "Mira.exe"), "x");
        if (updater) File.WriteAllText(Path.Combine(install, "Mira.Updater.exe"), "x");
        return new InstallLocation(install + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void A_complete_writable_installation_can_be_updated()
    {
        using var dir = new TempDir();
        var location = Install(dir);

        Assert.Null(location.Check());
        Assert.Equal(dir.Sub("Mira.update"), location.ExtractDir);
        Assert.Equal(dir.Sub("Mira.previous"), location.BackupDir);
    }

    [Fact]
    public void Without_the_desktop_executable_it_is_not_an_installation()
    {
        using var dir = new TempDir();

        Assert.Equal("update-not-installed", Install(dir, exe: false).Check());
    }

    [Fact]
    public void Without_the_helper_program_it_cannot_update()
    {
        using var dir = new TempDir();

        Assert.Equal("update-no-updater", Install(dir, updater: false).Check());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_folder_that_cannot_be_written_is_refused(bool parentWritable, bool installWritable)
    {
        using var dir = new TempDir();
        var location = Install(dir);

        var result = location.Check(path => path == dir.Path ? parentWritable : installWritable);

        Assert.Equal("update-not-writable", result);
    }

    [Fact]
    public void A_folder_with_a_real_write_ban_is_refused()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Su Linux e macOS i permessi si tolgono con chmod; chi gira come root scrive ovunque e non si può provare.
            if (Environment.UserName == "root") return;
            using var dir = new TempDir();
            var location = Install(dir);
            File.SetUnixFileMode(dir.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { Assert.Equal("update-not-writable", location.Check()); }
            finally { File.SetUnixFileMode(dir.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            return;
        }

        using var winDir = new TempDir();
        var winLocation = Install(winDir);
        var info = new DirectoryInfo(winDir.Path);
        var security = info.GetAccessControl();
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            me,
            System.Security.AccessControl.FileSystemRights.CreateFiles | System.Security.AccessControl.FileSystemRights.CreateDirectories,
            System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            Assert.Equal("update-not-writable", winLocation.Check());
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void The_probe_leaves_nothing_behind()
    {
        using var dir = new TempDir();

        Assert.True(InstallLocation.CanWriteTo(dir.Path));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
        Assert.False(InstallLocation.CanWriteTo(dir.Sub("manca")));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public async Task Checking_for_updates_at_startup_is_off_by_default_and_the_choice_is_kept()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Path);
        Assert.False((await store.GetAsync()).CheckUpdatesOnStart);

        await store.SetAsync(new UserSettings(true));

        Assert.True((await new SettingsStore(dir.Path).GetAsync()).CheckUpdatesOnStart);
    }

    [Fact]
    public async Task A_broken_file_means_defaults()
    {
        using var dir = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(dir.Path, SettingsStore.FileName), "{ non json");

        Assert.False((await new SettingsStore(dir.Path).GetAsync()).CheckUpdatesOnStart);
    }
}

public class UpdateInstallerTests : IDisposable
{
    private const string Repo = "Stellatinus/Mira";
    private const string ZipName = "Mira-Desktop-1.3.2-win-x64.zip";

    private readonly TempDir _dir = new();
    private readonly TempDir _data = new();
    private readonly FakeDesktop _desktop = new();
    private readonly FakeLauncher _launcher = new();
    private readonly InstallLocation _location;
    private byte[] _zip;
    private string _sums;
    private int _downloads;

    public UpdateInstallerTests()
    {
        var install = _dir.Sub("Mira");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Mira.exe"), "vecchia");
        File.WriteAllText(Path.Combine(install, "Mira.Updater.exe"), "updater");
        _location = new InstallLocation(install);
        _zip = MakeZip(withFolder: true);
        _sums = $"{Hex(_zip)}  {ZipName}\n{new string('0', 64)}  Mira-1.3.2-linux-x64.tar.gz\n";
    }

    public void Dispose()
    {
        _dir.Dispose();
        _data.Dispose();
    }

    private sealed class FakeDesktop : IDesktopIntegration
    {
        public bool AutostartEnabled => false;
        public bool ExitRequested { get; private set; }
        public void SetAutostart(bool enabled) { }
        public void RequestExit() => ExitRequested = true;
    }

    private sealed class FakeLauncher : IUpdateLauncher
    {
        public string? Exe { get; private set; }
        public List<string> Args { get; } = [];
        public void Launch(string updaterExe, IReadOnlyList<string> arguments)
        {
            Exe = updaterExe;
            Args.AddRange(arguments);
        }
        public string Arg(string name) => Args[Args.IndexOf(name) + 1];
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] MakeZip(bool withFolder, bool withExe = true)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var prefix = withFolder ? "Mira-Desktop-1.3.2-win-x64/" : "";
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(prefix + name).Open());
                writer.Write(text);
            }
            if (withExe) Add("Mira.exe", "nuova");
            Add("Mira.Updater.exe", "updater nuovo");
            Add("wwwroot/index.html", "<html/>");
        }
        return stream.ToArray();
    }

    private UpdateInstaller Make(string current = "1.3.1", string tag = "v1.3.2", bool desktop = true, bool prerelease = false)
    {
        var handler = new FakeHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("releases/latest", StringComparison.Ordinal))
            {
                var assets = string.Join(",", new[] { ZipName, "SHA256SUMS.txt" }.Select(n =>
                    $$"""{"name":"{{n}}","size":{{(n == ZipName ? _zip.Length : _sums.Length)}},"browser_download_url":"https://github.com/{{Repo}}/releases/download/{{tag}}/{{n}}"}"""));
                return Gh.Json($$"""{"tag_name":"{{tag}}","html_url":"https://github.com/x","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[{{assets}}]}""");
            }
            _downloads++;
            if (path.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_sums, Encoding.UTF8) };
            if (path.EndsWith(ZipName, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_zip) };
            return Gh.Status(HttpStatusCode.NotFound);
        });

        return new UpdateInstaller(
            new UpdateService(Gh.Api(handler), current),
            new FakeFactory(handler),
            desktop ? _desktop : null,
            _location,
            _data.Path,
            _launcher)
        {
            Runtime = () => "win-x64",
            ExitDelay = TimeSpan.Zero,
        };
    }

    private static async Task<InstallStatus> Finish(UpdateInstaller installer)
    {
        if (installer.Running is { } running) await running;
        return installer.GetStatus();
    }

    [Fact]
    public async Task Downloads_verifies_extracts_and_hands_over_to_the_helper_without_touching_the_installation()
    {
        var installer = Make();

        var refusal = await installer.StartAsync("1.3.2", "http://127.0.0.1:5080/api/version", CancellationToken.None);
        var status = await Finish(installer);

        Assert.Null(refusal);
        Assert.Equal("restarting", status.State);
        Assert.True(_desktop.ExitRequested);

        // L'installazione in uso è intatta; la nuova versione è estratta accanto, senza la cartella in cima al zip.
        Assert.Equal("vecchia", File.ReadAllText(_location.ExePath));
        Assert.Equal(_location.ExtractDir + Path.DirectorySeparatorChar + "Mira-Desktop-1.3.2-win-x64", _launcher.Arg("--staged"));
        Assert.Equal("nuova", File.ReadAllText(Path.Combine(_launcher.Arg("--staged"), "Mira.exe")));

        Assert.Equal("1.3.2", _launcher.Arg("--version"));
        Assert.Equal(_location.InstallDir, _launcher.Arg("--install"));
        Assert.Equal(_location.BackupDir, _launcher.Arg("--backup"));
        Assert.Equal("http://127.0.0.1:5080/api/version", _launcher.Arg("--health-url"));
        Assert.Equal(Path.Combine(_data.Path, "update-result.json"), _launcher.Arg("--result"));

        // Il programma di appoggio gira da una copia fuori dall'installazione, quella vecchia (il protocollo è il suo).
        Assert.NotEqual(_location.UpdaterPath, _launcher.Exe);
        Assert.Equal("updater", File.ReadAllText(_launcher.Exe!));
    }

    [Fact]
    public async Task A_package_with_the_wrong_checksum_is_never_extracted_or_launched()
    {
        var installer = Make();
        _sums = $"{new string('0', 64)}  {ZipName}\n";

        await installer.StartAsync("1.3.2", "http://127.0.0.1:5080/api/version", CancellationToken.None);
        var status = await Finish(installer);

        Assert.Equal("failed", status.State);
        Assert.Equal("update-checksum-mismatch", status.Error!.Code);
        Assert.False(Directory.Exists(_location.ExtractDir));
        Assert.Null(_launcher.Exe);
        Assert.False(_desktop.ExitRequested);
    }

    [Fact]
    public async Task Checksums_that_do_not_list_the_package_stop_the_update()
    {
        var installer = Make();
        _sums = $"{new string('0', 64)}  Mira-1.3.2-linux-x64.tar.gz\n";

        await installer.StartAsync("1.3.2", "http://127.0.0.1:5080/api/version", CancellationToken.None);
        var status = await Finish(installer);

        Assert.Equal("update-no-checksums", status.Error!.Code);
        Assert.Null(_launcher.Exe);
    }

    [Fact]
    public async Task A_package_without_the_executable_is_refused_and_leaves_no_leftovers()
    {
        _zip = MakeZip(withFolder: true, withExe: false);
        _sums = $"{Hex(_zip)}  {ZipName}\n";
        var installer = Make();

        await installer.StartAsync("1.3.2", "http://127.0.0.1:5080/api/version", CancellationToken.None);
        var status = await Finish(installer);

        Assert.Equal("update-bad-package", status.Error!.Code);
        Assert.False(Directory.Exists(_location.ExtractDir));
        Assert.Null(_launcher.Exe);
    }

    [Fact]
    public async Task A_zip_without_a_top_folder_works_too()
    {
        _zip = MakeZip(withFolder: false);
        _sums = $"{Hex(_zip)}  {ZipName}\n";
        var installer = Make();

        await installer.StartAsync("1.3.2", "http://127.0.0.1:5080/api/version", CancellationToken.None);
        await Finish(installer);

        Assert.Equal(_location.ExtractDir, _launcher.Arg("--staged"));
    }

    [Theory]
    [InlineData("1.3.3")]
    [InlineData("1.3.1")]
    [InlineData("9.9.9")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Only_the_version_offered_by_the_notice_can_be_installed(string? requested)
    {
        var installer = Make();

        var refusal = await installer.StartAsync(requested, "http://127.0.0.1:5080/api/version", CancellationToken.None);

        Assert.Equal("update-not-offered", refusal);
        Assert.Equal(0, _downloads);
        Assert.Null(_launcher.Exe);
    }

    [Fact]
    public async Task Nothing_is_offered_when_the_release_is_not_newer_or_is_a_prerelease()
    {
        Assert.Equal("update-not-offered", await Make(current: "1.3.2").StartAsync("1.3.2", "u", CancellationToken.None));
        // L'ultima release non è mai una pre-release per GitHub, ma se lo fosse non si installa comunque.
        Assert.Equal("update-not-stable", await Make(prerelease: true).StartAsync("1.3.2", "u", CancellationToken.None));
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task The_standalone_server_cannot_update_itself()
    {
        var installer = Make(desktop: false);

        Assert.Equal("update-desktop-only", await installer.StartAsync("1.3.2", "u", CancellationToken.None));
        Assert.False(installer.GetStatus().Supported);
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_is_refused_before_downloading_anything()
    {
        var installer = Make();
        // Senza programma di appoggio non si può aggiornare: il caso più semplice da provocare senza toccare i permessi.
        File.Delete(_location.UpdaterPath);

        Assert.Equal("update-no-updater", await installer.StartAsync("1.3.2", "u", CancellationToken.None));
        Assert.Equal(0, _downloads);

        var reason = installer.UnsupportedReason(canWrite: _ => false);
        Assert.Equal("update-no-updater", reason);
        File.WriteAllText(_location.UpdaterPath, "x");
        Assert.Equal("update-not-writable", installer.UnsupportedReason(canWrite: _ => false));
    }

    [Fact]
    public async Task A_failed_update_can_be_retried_but_two_at_once_are_refused()
    {
        var installer = Make();
        _sums = $"{new string('0', 64)}  {ZipName}\n";
        await installer.StartAsync("1.3.2", "u", CancellationToken.None);
        Assert.Equal("failed", (await Finish(installer)).State);

        _sums = $"{Hex(_zip)}  {ZipName}\n";
        Assert.Null(await installer.StartAsync("1.3.2", "u", CancellationToken.None));
        Assert.Equal("restarting", (await Finish(installer)).State);

        Assert.Equal("update-busy", await installer.StartAsync("1.3.2", "u", CancellationToken.None));
    }

    [Fact]
    public async Task The_last_result_written_by_the_helper_is_shown_and_can_be_dismissed()
    {
        var installer = Make();
        var path = Path.Combine(_data.Path, UpdateInstaller.ResultFileName);
        await File.WriteAllTextAsync(path, """{"version":"1.3.2","ok":false,"result":"RestoredPrevious","error":"health-check-failed","at":"2026-11-17T10:00:00Z"}""");

        var last = installer.GetStatus().LastResult!;
        Assert.False(last.Ok);
        Assert.Equal("health-check-failed", last.Error);

        installer.DismissLastResult();
        Assert.Null(installer.GetStatus().LastResult);
    }

    [Fact]
    public async Task A_success_is_reported_only_when_it_is_the_version_in_use()
    {
        var installer = Make();
        var path = Path.Combine(_data.Path, UpdateInstaller.ResultFileName);
        await File.WriteAllTextAsync(path, """{"version":"1.3.2","ok":true,"result":"Updated","at":"2026-11-17T10:00:00Z"}""");

        // I test girano con la versione 0.0.0-dev: un esito riuscito di un'altra versione è roba vecchia.
        Assert.Null(installer.GetStatus().LastResult);
    }
}
