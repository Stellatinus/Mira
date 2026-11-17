using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Android.App;
using Android.OS;
[assembly: Android.App.UsesPermission(Android.Manifest.Permission.Internet)]
namespace Spike;
[Activity(Label = "Spike", MainLauncher = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? b)
    {
        base.OnCreate(b);
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:5080");
        var app = builder.Build();
        app.MapGet("/api/version", () => "ok");
        Task.Run(async () =>
        {
            try
            {
                await app.StartAsync();
                using var http = new HttpClient();
                Android.Util.Log.Info("MIRA", "Kestrel: " + await http.GetStringAsync("http://127.0.0.1:5080/api/version"));
            }
            catch (Exception ex) { Android.Util.Log.Error("MIRA", "Kestrel ERRORE " + ex); }
        });
    }
}
