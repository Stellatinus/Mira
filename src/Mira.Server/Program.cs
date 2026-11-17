using Mira.Server;

// Il programma autonomo: tutto il lavoro sta in MiraHost, che usa anche il guscio desktop.
WebApplication app;
try
{
    app = MiraHost.Build(args);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

app.Run();
return 0;
