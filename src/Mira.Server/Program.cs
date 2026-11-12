using Microsoft.AspNetCore.Diagnostics;
using Mira.Server;
using Mira.Server.Api;
using Mira.Server.GitHub;
using Mira.Server.Models;
using Mira.Server.Services;
using Mira.Server.Storage;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ContentRoot.Find(),
});

// Solo locale: la dashboard non deve essere raggiungibile da altre macchine.
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("MIRA_URLS") ?? "http://127.0.0.1:5080");

builder.Services.AddSingleton<ISecretStore>(_ => new EncryptedFileSecretStore(DataDirectory.Resolve()));
builder.Services.AddHttpClient("github", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mira/0.1 (+https://github.com/Stellatinus/Mira)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<GitHubApi>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<DashboardService>();

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

app.Run();
