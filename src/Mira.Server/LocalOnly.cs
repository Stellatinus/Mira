namespace Mira.Server;

/// <summary>
/// Difende l'API locale: accetta solo richieste dirette a localhost (contro il DNS rebinding)
/// e, quando il browser invia l'header Origin, solo da pagine servite da localhost.
/// </summary>
public static class LocalOnly
{
    public static void UseLocalOnly(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var originOk = true;
            var origin = context.Request.Headers["Origin"].ToString();
            if (!string.IsNullOrEmpty(origin))
                originOk = Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsLoopback(uri.Host);

            if (!IsLoopback(context.Request.Host.Host) || !originOk)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden");
                return;
            }

            await next();
        });
    }

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" ||
        host == "[::1]" ||
        host == "::1";
}
