using Avalonia;

namespace Cato;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        SelfCheck.Run();
        // A second launch (menu, or a desktop shortcut bound to --toggle/--game) talks to the running Cato and exits.
        if (Ipc.TrySend(Ipc.CommandFrom(args))) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { EnableIme = false })
            .WithInterFont()
            .LogToTrace();
}
