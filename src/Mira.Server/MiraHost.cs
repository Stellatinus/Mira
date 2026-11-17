using Microsoft.AspNetCore.Diagnostics;
using Mira.Server.Api;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Services;
using Mira.Server.Storage;
using Mira.Server.Updates;

namespace Mira.Server;

/// <summary>
/// Il punto d'ingresso del server di Mira: lo usano sia il programma autonomo (<c>Program.cs</c>) sia il guscio
/// desktop (<c>Mira.Desktop</c>), che lo avvia dentro il proprio processo. Il comportamento è lo stesso: solo
/// 127.0.0.1, porta da MIRA_URLS, poi da config.json, poi 5080.
/// </summary>
public static class MiraHost
{
    /// <summary>
    /// Costruisce l'applicazione web senza avviarla. <paramref name="configureServices"/> permette a chi ospita
    /// Mira di aggiungere i propri servizi (per esempio <see cref="IDesktopIntegration"/>).
    /// config.json non valido: <see cref="InvalidOperationException"/> con un messaggio chiaro.
    /// </summary>
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = ContentRoot.Find(),
        });

        // Solo locale: la dashboard non deve essere raggiungibile da altre macchine.
        var config = AppConfig.Load(DataDirectory.Resolve());
        builder.WebHost.UseUrls(config.ResolveUrls(Environment.GetEnvironmentVariable("MIRA_URLS")));

        builder.Services.AddSingleton<ISecretStore>(_ => new EncryptedFileSecretStore(DataDirectory.Resolve()));
        builder.Services.AddSingleton(_ => new SeenStore(DataDirectory.Resolve()));
        builder.Services.AddHttpClient("github", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mira/0.1 (+https://github.com/Stellatinus/Mira)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        // I pacchetti degli aggiornamenti si scaricano da github.com (e dai suoi CDN, seguendo i redirect): nessun token.
        builder.Services.AddHttpClient("update", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mira/0.1 (+https://github.com/Stellatinus/Mira)");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
        builder.Services.AddSingleton<GitHubApi>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<DashboardService>();
        builder.Services.AddSingleton<IssueService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<SectionService>();
        builder.Services.AddSingleton(_ => new FollowStore(DataDirectory.Resolve()));
        builder.Services.AddSingleton<ReleaseService>();
        builder.Services.AddSingleton<RepoPageService>();
        builder.Services.AddSingleton(provider => new UpdateService(provider.GetRequiredService<GitHubApi>(), AppVersion.Current));
        builder.Services.AddSingleton(_ => new SettingsStore(DataDirectory.Resolve()));
        builder.Services.AddSingleton<IUpdateLauncher, ProcessUpdateLauncher>();
        builder.Services.AddSingleton(provider => new UpdateInstaller(
            provider.GetRequiredService<UpdateService>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetService<IDesktopIntegration>(),
            InstallLocation.Current(),
            DataDirectory.Resolve(),
            provider.GetRequiredService<IUpdateLauncher>()));

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            // Solo le MiraException hanno un codice pensato per l'utente; il resto è un errore generico.
            var code = exception is MiraException known ? known.Code : "internal";
            await context.Response.WriteAsJsonAsync(new { error = new ErrorInfo(code) });
        }));

        app.UseLocalOnly();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapMiraApi();
        app.MapFallbackToFile("index.html");

        return app;
    }

    /// <summary>L'indirizzo su cui il server sta ascoltando (dopo <c>StartAsync</c>), per esempio <c>http://127.0.0.1:5080</c>.</summary>
    public static Uri ListeningUrl(WebApplication app)
    {
        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Il server non è in ascolto.");
        return new Uri(address);
    }
}
