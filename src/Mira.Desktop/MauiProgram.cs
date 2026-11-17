namespace Mira.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
#if WINDOWS
        Integration.WindowsShell.ConfigureWebView();
#endif
        return builder.Build();
    }
}
