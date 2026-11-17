namespace Mira.Desktop;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage())
        {
            Title = "Mira",
            Width = 1280,
            Height = 860,
            MinimumWidth = 800,
            MinimumHeight = 560,
        };
#if WINDOWS
        Integration.WindowsShell.Attach(window);
#endif
        return window;
    }
}
