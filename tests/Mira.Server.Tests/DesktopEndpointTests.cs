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
}
