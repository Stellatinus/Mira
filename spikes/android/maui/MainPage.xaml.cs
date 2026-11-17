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
            var json = System.Text.Json.JsonSerializer.Serialize(new { method = e.Method, path = e.Uri.PathAndQuery, version = Mira.Server.AppVersion.Current });
            e.SetResponse(200, "OK", "application/json", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        };
    }
}

/// <summary>Ponte JS -> .NET e prove a runtime (log su logcat, tag MIRA).</summary>
public sealed class ApiBridge
{
    public object Api(string method, string path, string? body) =>
        new { status = 200, body = System.Text.Json.JsonSerializer.Serialize(new { method, path, bodyLength = body?.Length ?? -1, body }) };

    public void Report(string line) => Android.Util.Log.Info("MIRA", line);

    public async Task<object> Selftest()
    {
        var res = new Dictionary<string, string>();
        try
        {
            await SecureStorage.Default.SetAsync("mira-test", "segreto");
            res["SecureStorage"] = await SecureStorage.Default.GetAsync("mira-test") ?? "null";
        }
        catch (Exception ex) { res["SecureStorage"] = "ERRORE " + ex.GetType().Name + ": " + ex.Message; }
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mira-spike");
            res["GitHub"] = (await http.GetStringAsync("https://api.github.com/rate_limit"))[..80];
        }
        catch (Exception ex) { res["GitHub"] = "ERRORE " + ex.GetType().Name + ": " + ex.Message; }
        try
        {
            var dir = Mira.Server.Storage.DataDirectory.Resolve();
            var store = new EncryptedFileSecretStore(Path.Combine(dir, "t"));
            await store.SaveAsync("a", "b");
            res["DataDir"] = dir + " / EncryptedFileSecretStore=" + (await store.LoadAllAsync())["a"];
        }
        catch (Exception ex) { res["DataDir"] = "ERRORE " + ex.GetType().Name + ": " + ex.Message; }
        return res;
    }
}
