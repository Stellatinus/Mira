using Mira.Updater;

namespace Mira.Server.Tests;

/// <summary>Le prove del programma di appoggio: sostituzione, ripristino e rifiuti, con cartelle vere e processi finti.</summary>
public class SwapperTests : IDisposable
{
    private readonly TempDir _root = new();
    private readonly FakeHost _host = new();
    private readonly string _install;
    private readonly string _staged;
    private readonly string _backup;
    private readonly string _data;

    public SwapperTests()
    {
        _install = _root.Sub("Mira");
        _staged = _root.Sub("Mira.update", "Mira-Desktop-1.3.2-win-x64");
        _backup = _root.Sub("Mira.previous");
        _data = _root.Sub("dati");
        Write(_install, "Mira.exe", "vecchia");
        Write(_install, "wwwroot/index.html", "vecchio");
        Write(_staged, "Mira.exe", "nuova");
        Write(_staged, "wwwroot/index.html", "nuovo");
        Write(_data, "config.json", "{}");
        _host.OnLaunch = path => Path.Combine(_install, "Mira.exe") == path ? File.ReadAllText(path) : null;
    }

    public void Dispose() => _root.Dispose();

    private static void Write(string dir, string file, string text)
    {
        var path = Path.Combine(dir, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string dir, string file) => File.ReadAllText(Path.Combine(dir, file));

    private UpdateJob Job(TimeSpan? health = null) => new(
        1234, _install, _staged, _backup, "Mira.exe", "1.3.2", "http://127.0.0.1:5080/api/version",
        CleanupDir: _root.Sub("Mira.update"), ExitTimeout: TimeSpan.FromSeconds(5), HealthTimeout: health ?? TimeSpan.FromSeconds(3));

    private Task<UpdateOutcome> Run(UpdateJob? job = null) => new Swapper(_host).RunAsync(job ?? Job(), CancellationToken.None);

    private sealed class FakeApp(Func<bool> exited, Action? onKill = null) : IStartedApp
    {
        public bool HasExited => exited();
        public void Kill() => onKill?.Invoke();
        public void Dispose() { }
    }

    /// <summary>Un «sistema» finto: ogni avvio legge quale versione è installata e risponde di conseguenza (o non risponde).</summary>
    private sealed class FakeHost : IUpdateHost
    {
        public bool ExitsInTime { get; set; } = true;
        public bool NewVersionResponds { get; set; } = true;
        public bool NewVersionCrashes { get; set; }
        public bool LaunchFailsForNew { get; set; }
        public Func<string, string?>? OnLaunch { get; set; }
        public List<string> Launched { get; } = [];
        public int Killed { get; private set; }
        private string? _running;

        public Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct) => Task.FromResult(ExitsInTime);

        public IStartedApp Launch(string exePath)
        {
            var content = File.ReadAllText(exePath);
            Launched.Add(content);
            if (content == "nuova" && LaunchFailsForNew)
                throw new IOException("non si avvia");
            _running = content;
            return new FakeApp(() => content == "nuova" && NewVersionCrashes, () => Killed++);
        }

        public Task<string?> ReadVersionAsync(string url, CancellationToken ct) =>
            Task.FromResult<string?>(_running switch
            {
                "nuova" => NewVersionResponds ? "1.3.2" : null,
                "vecchia" => "1.3.1",
                _ => null,
            });

        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_healthy_new_version_replaces_the_old_one_which_is_kept_aside()
    {
        var outcome = await Run();

        Assert.Equal(UpdateResult.Updated, outcome.Result);
        Assert.Equal("nuova", Read(_install, "Mira.exe"));
        Assert.Equal("nuovo", Read(_install, "wwwroot/index.html"));
        Assert.Equal("vecchia", Read(_backup, "Mira.exe"));
        Assert.Equal(["nuova"], _host.Launched);
        Assert.False(Directory.Exists(_root.Sub("Mira.update")));
    }

    [Fact]
    public async Task The_user_data_is_never_touched()
    {
        await Run();
        Assert.Equal("{}", Read(_data, "config.json"));

        _host.NewVersionResponds = false;
        Write(_staged, "Mira.exe", "nuova");
        await Run();
        Assert.Equal("{}", Read(_data, "config.json"));
    }

    [Fact]
    public async Task A_new_version_that_does_not_answer_is_rolled_back_to_the_old_one()
    {
        _host.NewVersionResponds = false;

        var outcome = await Run();

        Assert.Equal(UpdateResult.RestoredPrevious, outcome.Result);
        Assert.Equal("health-check-failed", outcome.Error);
        // La vecchia è tornata al suo posto, intera, ed è stata riavviata; la nuova è stata fermata e tolta.
        Assert.Equal("vecchia", Read(_install, "Mira.exe"));
        Assert.Equal("vecchio", Read(_install, "wwwroot/index.html"));
        Assert.Equal(["nuova", "vecchia"], _host.Launched);
        Assert.Equal(1, _host.Killed);
        Assert.False(Directory.Exists(_backup));
        Assert.False(Directory.Exists(_install + ".failed"));
    }

    [Fact]
    public async Task A_new_version_that_crashes_at_startup_is_rolled_back_without_waiting_for_the_timeout()
    {
        _host.NewVersionCrashes = true;

        var outcome = await Run(Job(health: TimeSpan.FromHours(1)));

        Assert.Equal(UpdateResult.RestoredPrevious, outcome.Result);
        Assert.Equal("vecchia", Read(_install, "Mira.exe"));
    }

    [Fact]
    public async Task A_new_version_that_cannot_even_be_started_is_rolled_back()
    {
        _host.LaunchFailsForNew = true;

        var outcome = await Run();

        Assert.Equal(UpdateResult.RestoredPrevious, outcome.Result);
        Assert.Equal("vecchia", Read(_install, "Mira.exe"));
        Assert.Equal(["nuova", "vecchia"], _host.Launched);
    }

    [Fact]
    public async Task If_mira_does_not_close_in_time_nothing_is_changed()
    {
        _host.ExitsInTime = false;

        var outcome = await Run();

        Assert.Equal(UpdateResult.NotChanged, outcome.Result);
        Assert.Equal("exit-timeout", outcome.Error);
        Assert.Equal("vecchia", Read(_install, "Mira.exe"));
        Assert.True(Directory.Exists(_staged));
        Assert.Empty(_host.Launched);
    }

    [Fact]
    public async Task A_missing_staged_version_changes_nothing_and_restarts_the_old_one()
    {
        Directory.Delete(_root.Sub("Mira.update"), recursive: true);

        var outcome = await Run();

        Assert.Equal(UpdateResult.NotChanged, outcome.Result);
        Assert.Equal("vecchia", Read(_install, "Mira.exe"));
        Assert.Equal(["vecchia"], _host.Launched);
    }

    [Fact]
    public async Task Only_one_previous_version_is_kept()
    {
        Write(_backup, "Mira.exe", "vecchissima");

        await Run();

        Assert.Equal("vecchia", Read(_backup, "Mira.exe"));
    }

    [Fact]
    public async Task If_the_old_version_is_locked_the_swap_fails_cleanly_and_the_old_one_restarts()
    {
        // Un file aperto in lettura da un altro processo impedisce di spostare la cartella (su Windows).
        if (!OperatingSystem.IsWindows()) return;
        using var held = new FileStream(Path.Combine(_install, "Mira.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);

        var outcome = await Run();

        Assert.Equal(UpdateResult.NotChanged, outcome.Result);
        Assert.Equal("swap-failed", outcome.Error);
        Assert.True(Directory.Exists(_staged));
    }
}
