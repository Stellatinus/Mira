using Mira.Server.Storage;
namespace Spike.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Web.SetInvokeJavaScriptTarget(new ApiBridge());
        // Intercetta fetch('/api/...') dentro il processo, senza socket: il frontend resta invariato.
        Web.WebResourceRequested += (_, e) =>
        {
            if (!e.Uri.AbsolutePath.StartsWith("/api/")) return;
            e.Handled = true;
            var json = System.Text.Json.JsonSerializer.Serialize(new { version = Mira.Server.AppVersion.Current });
            e.SetResponse(200, "OK", "application/json", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        };
    }
}

/// <summary>Il ponte: il JS chiama Api(path) e il .NET risponde senza passare da HTTP.</summary>
public sealed class ApiBridge
{
    public object Api(string path) => path == "/api/version" ? new { version = Mira.Server.AppVersion.Current } : new { error = "todo" };
}
#if KESTREL
public static class KestrelProbe
{
    public static void Start()
    {
        var b = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        b.WebHost.UseUrls("http://127.0.0.1:5080");
        var app = b.Build();
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(app, "/api/version", () => "ok");
        Task.Run(() => app.Run());
    }
}
#endif
