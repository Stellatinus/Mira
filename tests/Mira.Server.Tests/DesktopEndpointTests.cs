using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mira.Server.Api;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

/// <summary>Le opzioni del guscio desktop: assenti nel programma autonomo, presenti quando il guscio registra <see cref="IDesktopIntegration"/>.</summary>
public sealed class DesktopEndpointTests
{
    private sealed class FakeDesktop : IDesktopIntegration
    {
        public bool AutostartEnabled { get; private set; }
        public bool Fail { get; set; }

        public void RequestExit()
        {
        }

        public void SetAutostart(bool enabled)
        {
            if (Fail)
                throw new UnauthorizedAccessException();
            AutostartEnabled = enabled;
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(IDesktopIntegration? desktop)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // MapMiraApi ha bisogno dei servizi registrati per riconoscere i parametri degli altri endpoint.
        builder.Services.AddSingleton<Mira.Server.Storage.ISecretStore>(new InMemorySecretStore());
        builder.Services.AddSingleton(Gh.Api(new Router().Handler()));
        builder.Services.AddSingleton<Mira.Server.Services.AccountService>();
        builder.Services.AddSingleton<Mira.Server.Services.DashboardService>();
        builder.Services.AddSingleton<Mira.Server.Services.IssueService>();
        builder.Services.AddSingleton<Mira.Server.Services.NotificationService>();
        builder.Services.AddSingleton<Mira.Server.Services.SectionService>();
        builder.Services.AddSingleton(new Mira.Server.Storage.FollowStore(Path.Combine(Path.GetTempPath(), "mira-follow-" + Guid.NewGuid().ToString("N"))));
        builder.Services.AddSingleton<Mira.Server.Services.ReleaseService>();
        builder.Services.AddSingleton<Mira.Server.Services.RepoPageService>();
        builder.Services.AddSingleton(provider => new Mira.Server.Services.UpdateService(provider.GetRequiredService<Mira.Server.GitHub.GitHubApi>(), "1.2.0"));
        builder.Services.AddSingleton(new Mira.Server.Storage.SeenStore(Path.Combine(Path.GetTempPath(), "mira-desktop-" + Guid.NewGuid().ToString("N"))));
        var dataDir = Path.Combine(Path.GetTempPath(), "mira-data-" + Guid.NewGuid().ToString("N"));
        builder.Services.AddSingleton(new Mira.Server.Storage.SettingsStore(dataDir));
        builder.Services.AddSingleton<Mira.Server.Updates.IUpdateLauncher, Mira.Server.Updates.ProcessUpdateLauncher>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(provider => new Mira.Server.Updates.UpdateInstaller(
            provider.GetRequiredService<Mira.Server.Services.UpdateService>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetService<IDesktopIntegration>(),
            new Mira.Server.Updates.InstallLocation(Path.Combine(dataDir, "non-installata")),
            dataDir,
            provider.GetRequiredService<Mira.Server.Updates.IUpdateLauncher>()));
        if (desktop is not null)
            builder.Services.AddSingleton(desktop);
        var app = builder.Build();
        app.MapMiraApi();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task Without_the_desktop_shell_nothing_is_available()
    {
        var (app, client) = await StartAsync(null);
        await using var _ = app;

        var status = await client.GetFromJsonAsync<DesktopStatus>("/api/desktop");
        Assert.Equal(new DesktopStatus(false, false), status);

        var put = await client.PutAsJsonAsync("/api/desktop/autostart", new AutostartRequest(true));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task The_autostart_can_be_turned_on_and_off()
    {
        var fake = new FakeDesktop();
        var (app, client) = await StartAsync(fake);
        await using var _ = app;

        var on = await client.PutAsJsonAsync("/api/desktop/autostart", new AutostartRequest(true));
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal(new DesktopStatus(true, true), await client.GetFromJsonAsync<DesktopStatus>("/api/desktop"));

        await client.PutAsJsonAsync("/api/desktop/autostart", new AutostartRequest(false));
        Assert.Equal(new DesktopStatus(true, false), await client.GetFromJsonAsync<DesktopStatus>("/api/desktop"));
    }

    [Fact]
    public async Task A_refusal_of_the_system_becomes_a_coded_error()
    {
        var (app, client) = await StartAsync(new FakeDesktop { Fail = true });
        await using var _ = app;

        var response = await client.PutAsJsonAsync("/api/desktop/autostart", new AutostartRequest(true));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("autostart-failed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Checking_for_updates_at_startup_is_off_by_default_and_the_choice_is_saved()
    {
        var (app, client) = await StartAsync(null);
        await using var _ = app;

        var initial = await client.GetFromJsonAsync<Mira.Server.Storage.UserSettings>("/api/settings");
        Assert.False(initial!.CheckUpdatesOnStart);

        var put = await client.PutAsJsonAsync("/api/settings", new SettingsRequest(true));
        Assert.True((await put.Content.ReadFromJsonAsync<Mira.Server.Storage.UserSettings>())!.CheckUpdatesOnStart);
        Assert.True((await client.GetFromJsonAsync<Mira.Server.Storage.UserSettings>("/api/settings"))!.CheckUpdatesOnStart);

        // Una richiesta senza il campo non cambia niente.
        await client.PutAsJsonAsync("/api/settings", new SettingsRequest(null));
        Assert.True((await client.GetFromJsonAsync<Mira.Server.Storage.UserSettings>("/api/settings"))!.CheckUpdatesOnStart);
    }

    [Fact]
    public async Task The_standalone_server_reports_that_in_app_update_is_not_supported_and_refuses_it()
    {
        var (app, client) = await StartAsync(null);
        await using var _ = app;

        var status = await client.GetFromJsonAsync<Mira.Server.Updates.InstallStatus>("/api/update/install");
        Assert.False(status!.Supported);
        Assert.Equal("update-desktop-only", status.Reason);

        var response = await client.PostAsJsonAsync("/api/update/install", new InstallUpdateRequest("9.9.9"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_desktop_shell_refuses_to_update_when_it_is_not_an_installed_package()
    {
        var (app, client) = await StartAsync(new FakeDesktop());
        await using var _ = app;

        var status = await client.GetFromJsonAsync<Mira.Server.Updates.InstallStatus>("/api/update/install");

        Assert.False(status!.Supported);
        // O non c'è un pacchetto per questo sistema, o (da sorgente) Mira.exe non è in questa cartella.
        Assert.Contains(status.Reason, new[] { "update-unsupported-os", "update-not-installed" });
    }
}
